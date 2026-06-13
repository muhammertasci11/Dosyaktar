using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Dosyaktar.Services
{
    public sealed record FlashFileDict(int Id, string RelativePath, long Size);
    public sealed record FlashChunk(int FileId, long Offset, int Length);

    public class FlashProgressEventArgs : EventArgs
    {
        public double OverallPercentage { get; }
        public long TotalBytesReceived { get; }
        public long TotalBytes { get; }
        public double SpeedMBps { get; }
        public TimeSpan RemainingTime { get; }
        public int ActiveConnections { get; }
        // Bağlantı başına anlık durumlar: ThreadId -> Yüzde (0-100)
        public Dictionary<int, double> ThreadProgress { get; }

        public FlashProgressEventArgs(double percentage, long bytes, long totalBytes, double speed, TimeSpan rem, int conns, Dictionary<int, double> threadProgress)
        {
            OverallPercentage = percentage;
            TotalBytesReceived = bytes;
            TotalBytes = totalBytes;
            SpeedMBps = speed;
            RemainingTime = rem;
            ActiveConnections = conns;
            ThreadProgress = threadProgress;
        }
    }

    public class FlashTransferService
    {
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<string>? Error;
        public event EventHandler<FlashProgressEventArgs>? ProgressChanged;

        private CancellationTokenSource _cts = new();

        public void Cancel() => _cts.Cancel();

        private void OnStatus(string msg) => StatusChanged?.Invoke(this, msg);
        private void OnError(string msg) => Error?.Invoke(this, msg);

        // Maksimum parça boyutu (Örn: 8 MB)
        private const int ChunkSize = 8 * 1024 * 1024;

        // ═════════════════════════════════════════════════════════════════════
        //  GÖNDERİCİ (SENDER)
        // ═════════════════════════════════════════════════════════════════════
        public async Task<TransferResult> SendFlashAsync(
            List<(string path, string relName, long size)> entries,
            string targetIP,
            int port,
            int maxConnections)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            OnStatus($"Flash Modu: Master bağlantı kuruluyor ({targetIP}:{port})...");
            TcpClient? masterClient = null;
            try
            {
                masterClient = new TcpClient();
                await masterClient.ConnectAsync(targetIP, port, token);

                using var stream = masterClient.GetStream();
                using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

                // 1. Master Protokol Başlığı
                writer.Write(2); // TransferMode.Flash
                
                var fileDict = new List<FlashFileDict>();
                long totalBytes = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    fileDict.Add(new FlashFileDict(i, entries[i].relName, entries[i].size));
                    totalBytes += entries[i].size;
                }

                string jsonDict = JsonSerializer.Serialize(fileDict);
                
                writer.Write(maxConnections);
                writer.Write(totalBytes);
                writer.Write(jsonDict);
                await stream.FlushAsync(token);

                // 2. Alıcıdan yeni port numarasını bekle (ephemeral port)
                OnStatus("Alıcıdan çoklu bağlantı portu bekleniyor...");
                int dataPort = reader.ReadInt32();
                
                if (dataPort == 0)
                    throw new Exception("Alıcı Flash modunu desteklemiyor veya port reddedildi.");

                // Master TCP artık dataPort üzerinden bağlanan worker'ları yönetecek
                // 3. Dosyaları Chunk'lara böl
                var chunkQueue = new ConcurrentQueue<FlashChunk>();
                for (int i = 0; i < entries.Count; i++)
                {
                    long size = entries[i].size;
                    long offset = 0;
                    while (offset < size)
                    {
                        int length = (int)Math.Min(ChunkSize, size - offset);
                        chunkQueue.Enqueue(new FlashChunk(i, offset, length));
                        offset += length;
                    }
                    if (size == 0) // Boş klasör / dosya durumu
                    {
                        chunkQueue.Enqueue(new FlashChunk(i, 0, 0));
                    }
                }

                int totalChunks = chunkQueue.Count;
                OnStatus($"Flash: {totalChunks} parça {maxConnections} bağlantı üzerinden gönderilecek.");

                var tasks = new List<Task>();
                int activeConns = 0;
                long totalSent = 0;

                // UI Reporter
                var uiTask = Task.Run(async () =>
                {
                    var lastTime = DateTime.UtcNow;
                    long lastSent = 0;
                    while (!token.IsCancellationRequested && Interlocked.Read(ref totalSent) < totalBytes)
                    {
                        await Task.Delay(500, token);
                        var now = DateTime.UtcNow;
                        var deltaSec = (now - lastTime).TotalSeconds;
                        long currentSent = Interlocked.Read(ref totalSent);
                        double mbps = deltaSec > 0 ? ((currentSent - lastSent) / deltaSec) / 1048576.0 : 0;
                        
                        lastTime = now;
                        lastSent = currentSent;

                        double pct = totalBytes > 0 ? (currentSent * 100.0) / totalBytes : 100;
                        var remTime = mbps > 0 ? TimeSpan.FromSeconds((totalBytes - currentSent) / (mbps * 1048576.0)) : TimeSpan.Zero;
                        
                        ProgressChanged?.Invoke(this, new FlashProgressEventArgs(pct, currentSent, totalBytes, mbps, remTime, activeConns, new Dictionary<int, double>()));
                    }
                });

                // Worker'ları başlat
                for (int t = 0; t < maxConnections; t++)
                {
                    int threadId = t;
                    tasks.Add(Task.Run(async () =>
                    {
                        using var worker = new TcpClient();
                        try
                        {
                            await worker.ConnectAsync(targetIP, dataPort, token);
                            Interlocked.Increment(ref activeConns);

                            using var wStream = worker.GetStream();
                            using var bWriter = new BinaryWriter(wStream, Encoding.UTF8, leaveOpen: true);
                            
                            // Worker kendini tanıtıyor (opsiyonel, 1 = hello)
                            bWriter.Write((byte)1);
                            await wStream.FlushAsync(token);

                            byte[] buffer = new byte[ChunkSize];

                            while (chunkQueue.TryDequeue(out var chunk))
                            {
                                if (token.IsCancellationRequested) break;

                                string filePath = entries[chunk.FileId].path;
                                
                                bWriter.Write((byte)2); // 2 = Data chunk
                                bWriter.Write(chunk.FileId);
                                bWriter.Write(chunk.Offset);
                                bWriter.Write(chunk.Length);

                                if (chunk.Length > 0)
                                {
                                    using var handle = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous);
                                    var mem = buffer.AsMemory(0, chunk.Length);
                                    int read = await RandomAccess.ReadAsync(handle, mem, chunk.Offset, token);
                                    if (read != chunk.Length) throw new IOException("Dosya okunurken boyutta tutarsızlık.");
                                    
                                    await wStream.WriteAsync(mem, token);
                                }
                                Interlocked.Add(ref totalSent, chunk.Length);
                            }

                            // 3 = Bitti
                            bWriter.Write((byte)3);
                            await wStream.FlushAsync(token);
                        }
                        catch { /* Worker koptu */ }
                        finally { Interlocked.Decrement(ref activeConns); }
                    }));
                }

                await Task.WhenAll(tasks);
                
                if (token.IsCancellationRequested) return new TransferResult(false, "İptal edildi.");

                // Master üzerinden ACK bekle (Alıcı diske yazmayı bitirdi mi?)
                OnStatus("Alıcının dosyaları birleştirmesi bekleniyor...");
                writer.Write((byte)99); // Gönderim bitti sinyali
                await stream.FlushAsync(token);

                byte ack = reader.ReadByte();
                if (ack != 1) throw new Exception("Alıcıdan geçersiz ACK.");

                OnStatus("✓ Flash Aktarım Başarılı.");
                return new TransferResult(true, "Flash aktarım başarılı.");
            }
            catch (Exception ex)
            {
                OnError($"Flash Gönderim Hatası: {ex.Message}");
                return new TransferResult(false, ex.Message);
            }
            finally
            {
                masterClient?.Close();
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  ALICI (RECEIVER)
        // ═════════════════════════════════════════════════════════════════════
        public async Task<TransferResult> ReceiveFlashAsync(
            TcpClient masterClient,
            BinaryReader masterReader,
            BinaryWriter masterWriter,
            string saveDirectory)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            TcpListener? workerListener = null;
            try
            {
                int maxConns = masterReader.ReadInt32();
                long totalBytes = masterReader.ReadInt64();
                string jsonDict = masterReader.ReadString();

                var fileDict = JsonSerializer.Deserialize<List<FlashFileDict>>(jsonDict) ?? new();

                // Worker'lar için rastgele (ephemeral) port aç
                workerListener = new TcpListener(IPAddress.Any, 0);
                workerListener.Start();
                int dataPort = ((IPEndPoint)workerListener.LocalEndpoint).Port;

                // Göndericiye dataPort'u bildir
                masterWriter.Write(dataPort);
                await masterClient.GetStream().FlushAsync(token);

                OnStatus($"Flash Modu: Ön tahsis yapılıyor ({fileDict.Count} dosya)...");

                // Dosyaları önceden oluştur ve boyutlandır (Pre-allocation)
                // Ayrıca file handle'ları açık tutacağız
                var fileHandles = new Dictionary<int, Microsoft.Win32.SafeHandles.SafeFileHandle>();

                foreach (var fd in fileDict)
                {
                    string safePath = Path.Combine(saveDirectory, fd.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(safePath)!);
                    
                    var handle = File.OpenHandle(safePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, FileOptions.Asynchronous);
                    RandomAccess.SetLength(handle, fd.Size); // SSD fragmantasyonunu engeller
                    fileHandles[fd.Id] = handle;
                }

                OnStatus($"Flash Modu: Dinleniyor (Port {dataPort})...");

                long totalReceived = 0;
                int activeConns = 0;
                var threadProgressDict = new ConcurrentDictionary<int, double>();

                // UI Reporter Task
                var uiTask = Task.Run(async () =>
                {
                    var lastTime = DateTime.UtcNow;
                    long lastRecv = 0;
                    while (!token.IsCancellationRequested && Interlocked.Read(ref totalReceived) < totalBytes)
                    {
                        await Task.Delay(200, token); // Daha akıcı bir UI için 200ms
                        var now = DateTime.UtcNow;
                        var deltaSec = (now - lastTime).TotalSeconds;
                        long currentRecv = Interlocked.Read(ref totalReceived);
                        double mbps = deltaSec > 0 ? ((currentRecv - lastRecv) / deltaSec) / 1048576.0 : 0;
                        
                        lastTime = now;
                        lastRecv = currentRecv;

                        double pct = totalBytes > 0 ? (currentRecv * 100.0) / totalBytes : 100;
                        var remTime = mbps > 0 ? TimeSpan.FromSeconds((totalBytes - currentRecv) / (mbps * 1048576.0)) : TimeSpan.Zero;
                        
                        var tp = new Dictionary<int, double>(threadProgressDict);
                        ProgressChanged?.Invoke(this, new FlashProgressEventArgs(pct, currentRecv, totalBytes, mbps, remTime, activeConns, tp));
                    }
                });

                var workerTasks = new List<Task>();

                // Dinleme döngüsü (Bağlantılar geldikçe kabul et)
                var acceptTask = Task.Run(async () =>
                {
                    int accepted = 0;
                    while (accepted < maxConns && !token.IsCancellationRequested)
                    {
                        try
                        {
                            var worker = await workerListener.AcceptTcpClientAsync(token);
                            accepted++;
                            Interlocked.Increment(ref activeConns);
                            int threadId = accepted;
                            threadProgressDict[threadId] = 0.0;

                            workerTasks.Add(Task.Run(async () =>
                            {
                                using (worker)
                                {
                                    using var wStream = worker.GetStream();
                                    using var bReader = new BinaryReader(wStream, Encoding.UTF8, leaveOpen: true);
                                    
                                    byte hello = bReader.ReadByte();
                                    if (hello != 1) return; // Yanlış protokol

                                    byte[] buffer = new byte[ChunkSize];

                                    while (!token.IsCancellationRequested)
                                    {
                                        byte cmd = bReader.ReadByte();
                                        if (cmd == 3) break; // Bitti
                                        if (cmd != 2) throw new IOException("Bilinmeyen komut.");

                                        int fileId = bReader.ReadInt32();
                                        long offset = bReader.ReadInt64();
                                        int length = bReader.ReadInt32();

                                        if (length > 0)
                                        {
                                            // Parçayı belleğe oku
                                            var mem = buffer.AsMemory(0, length);
                                            int readTotal = 0;
                                            while (readTotal < length)
                                            {
                                                int r = await wStream.ReadAsync(buffer.AsMemory(readTotal, length - readTotal), token);
                                                if (r == 0) throw new IOException("Bağlantı koptu.");
                                                readTotal += r;
                                                
                                                // UI Anlık ilerleme (ör. 8MB'ın yüzdesi)
                                                threadProgressDict[threadId] = (readTotal * 100.0) / length;
                                            }

                                            // Diske yaz
                                            var handle = fileHandles[fileId];
                                            await RandomAccess.WriteAsync(handle, mem, offset, token);
                                        }

                                        Interlocked.Add(ref totalReceived, length);
                                    }
                                }
                                Interlocked.Decrement(ref activeConns);
                                threadProgressDict[threadId] = 100.0; // Bittiğinde dolu kalsın
                            }));
                        }
                        catch { /* İptal */ }
                    }
                });

                // Master'dan bitiş sinyali bekle
                byte masterSig = masterReader.ReadByte();
                if (masterSig != 99) throw new IOException("Master bağlantı beklenmedik bir şekilde koptu.");

                // Tüm worker görevleri bitti mi bekle
                await Task.WhenAll(workerTasks);

                // Dosyaları kapat
                foreach (var h in fileHandles.Values) h.Dispose();

                // Master'a ACK dön
                masterWriter.Write((byte)1);
                await masterClient.GetStream().FlushAsync(token);

                ProgressChanged?.Invoke(this, new FlashProgressEventArgs(100.0, totalBytes, totalBytes, 0, TimeSpan.Zero, 0, new Dictionary<int, double>()));
                OnStatus("✓ Dosyalar başarıyla birleştirildi.");

                return new TransferResult(true, "Flash transfer başarılı.");
            }
            catch (Exception ex)
            {
                OnError($"Flash Alım Hatası: {ex.Message}");
                return new TransferResult(false, ex.Message);
            }
            finally
            {
                workerListener?.Stop();
                masterClient?.Close();
            }
        }
    }
}
