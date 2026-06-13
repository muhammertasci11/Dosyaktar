using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dosyaktar.Services
{
    // ─── Transfer İlerleme DTO ────────────────────────────────────────────────
    public sealed record TransferProgress(
        long     BytesSent,
        long     TotalBytes,
        double   SpeedMBps,
        TimeSpan RemainingTime,
        string   FileName,
        int      CurrentFileIndex = 1,
        int      TotalFiles       = 1
    )
    {
        public double Percentage => TotalBytes > 0 ? BytesSent / (double)TotalBytes * 100 : 0;
    }

    // ─── Transfer Sonuç DTO ───────────────────────────────────────────────────
    public sealed record TransferResult(bool Success, string Message);

    // ─── Protokol Modları ─────────────────────────────────────────────────────
    internal enum TransferMode { Files = 0, Folder = 1, Flash = 2 }

    /// <summary>
    /// TCP tabanlı çoklu dosya / klasör transferi.
    /// Protokol:
    ///   [int: mod (0=dosyalar, 1=klasör)]
    ///   [int: dosya_sayısı]
    ///   foreach dosya:
    ///     [int: göreceliYolUzunluğu][bytes: göreceliYol (UTF-8)]
    ///     [long: boyut]
    ///     [bytes: veri]
    /// </summary>
    public class FileTransferService
    {
        // ── Sabitler ──────────────────────────────────────────────────────────
        public const int DefaultPort = 5001;
        public const int BufferSize  = 65536;   // 64 KB
        private const int SpeedWindow = 10;

        // ── Olaylar ───────────────────────────────────────────────────────────
        public event EventHandler<TransferProgress>? ProgressChanged;
        public event EventHandler<string>?           StatusChanged;
        public event EventHandler<string>?           Error;
        public event EventHandler<FlashProgressEventArgs>? FlashProgressChanged;

        private void OnProgress(TransferProgress p) => ProgressChanged?.Invoke(this, p);
        private void OnStatus(string msg)            => StatusChanged?.Invoke(this, msg);
        private void OnError(string msg)             => Error?.Invoke(this, msg);

        // ── İptal ─────────────────────────────────────────────────────────────
        private CancellationTokenSource? _cts;
        public void Cancel() => _cts?.Cancel();

        // ══════════════════════════════════════════════════════════════════════
        //  GÖNDERME — Dosyalar
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>Tek dosya gönderim sarmalayıcısı (geriye dönük uyumluluk).</summary>
        public Task<TransferResult> SendFileAsync(string filePath, string receiverIP,
                                                  int port = DefaultPort)
            => SendFilesAsync(new[] { filePath }, receiverIP, port);

        /// <summary>Birden fazla dosyayı tek bağlantıda gönderir.</summary>
        public async Task<TransferResult> SendFilesAsync(
            IReadOnlyList<string> filePaths,
            string receiverIP,
            int port = DefaultPort)
        {
            if (filePaths.Count == 0)
                return new TransferResult(false, "Gönderilecek dosya seçilmedi.");

            // Tüm dosyaları ve boyutlarını topla
            var entries = new List<(string path, string relName, long size)>();
            foreach (var p in filePaths)
            {
                if (!File.Exists(p)) continue;
                var fi = new FileInfo(p);
                entries.Add((p, fi.Name, fi.Length));
            }
            if (entries.Count == 0)
                return new TransferResult(false, "Seçilen dosyalar bulunamadı.");

            long totalBytes = 0;
            foreach (var e in entries) totalBytes += e.size;

            return await SendEntriesAsync(entries, TransferMode.Files, receiverIP, port, totalBytes);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  GÖNDERME — Klasör
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Tüm klasörü içindeki dosyalarla birlikte gönderir.
        /// Alıcı tarafta göreceli yol korunur.
        /// </summary>
        public async Task<TransferResult> SendFolderAsync(
            string folderPath,
            string receiverIP,
            int port = DefaultPort)
        {
            if (!Directory.Exists(folderPath))
                return new TransferResult(false, "Klasör bulunamadı.");

            OnStatus($"Klasör taranıyor: {Path.GetFileName(folderPath)}...");

            var entries = new List<(string path, string relName, long size)>();
            // Klasörün kendisini kök olarak baz alarak içeriğin dağılmasını engelle (Steam common/OyunAdı vb.)
            string baseDir = Path.GetDirectoryName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? folderPath;

            foreach (var file in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
            {
                var fi = new FileInfo(file);
                string rel = Path.GetRelativePath(baseDir, file);
                entries.Add((file, rel, fi.Length));
            }

            if (entries.Count == 0)
                return new TransferResult(false, "Klasör boş.");

            long totalBytes = 0;
            foreach (var e in entries) totalBytes += e.size;

            OnStatus($"{entries.Count} dosya bulundu ({FormatSize(totalBytes)}), gönderiliyor...");
            return await SendEntriesAsync(entries, TransferMode.Folder, receiverIP, port, totalBytes);
        }

        // ── Ortak gönderim çekirdeği ──────────────────────────────────────────
        private async Task<TransferResult> SendEntriesAsync(
            List<(string path, string relName, long size)> entries,
            TransferMode mode,
            string receiverIP,
            int port,
            long totalBytes)
        {
            var settings = SettingsService.Load();
            if (settings.UseFlashMode)
            {
                var flash = new FlashTransferService();
                flash.StatusChanged += (s, msg) => OnStatus(msg);
                flash.Error += (s, msg) => OnError(msg);
                flash.ProgressChanged += (s, p) => 
                {
                    OnProgress(new TransferProgress(
                        p.TotalBytesReceived, totalBytes, p.SpeedMBps, p.RemainingTime, 
                        "Flash Modu Çoklu Aktarım", 1, entries.Count));
                    FlashProgressChanged?.Invoke(this, p);
                };
                
                // Add event handler logic to MainWindow to handle the new UI later
                // The actual ThreadProgress will be consumed if the UI supports it.
                
                return await flash.SendFlashAsync(entries, receiverIP, port, settings.FlashConnections);
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            OnStatus($"Bağlanılıyor: {receiverIP}:{port}...");
            TcpClient? client = null;
            try
            {
                client = new TcpClient { SendBufferSize = BufferSize * 4, NoDelay = true };
                await client.ConnectAsync(IPAddress.Parse(receiverIP), port, token);
                OnStatus("Bağlantı kuruldu, transfer başlıyor...");

                await using var stream = client.GetStream();
                using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

                // ── Protokol başlığı ──
                writer.Write((int)mode);        // mod
                writer.Write(entries.Count);    // dosya sayısı
                writer.Flush();

                long globalSent = 0;
                var speedQueue = new Queue<(long bytes, DateTime time)>();
                DateTime lastUiUpdate = DateTime.MinValue;

                for (int i = 0; i < entries.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var (path, relName, fileSize) = entries[i];

                    OnStatus($"Gönderiliyor ({i + 1}/{entries.Count}): {Path.GetFileName(relName)}");

                    // Dosya başlığı
                    byte[] relBytes = Encoding.UTF8.GetBytes(relName);
                    writer.Write(relBytes.Length);
                    writer.Write(relBytes);
                    writer.Write(fileSize);
                    writer.Flush();

                    // Dosya verisi
                    await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                                        FileShare.Read, BufferSize, useAsync: true);
                    var buf = new byte[BufferSize];
                    int read;
                    while ((read = await fs.ReadAsync(buf, token)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        await stream.WriteAsync(buf.AsMemory(0, read), token);
                        globalSent += read;

                        var now = DateTime.UtcNow;
                        speedQueue.Enqueue((globalSent, now));

                        // 5 saniyeden eski verileri kuyruktan çıkar (Hareketli Ortalama)
                        while (speedQueue.Count > 1 && (now - speedQueue.Peek().time).TotalSeconds > 5)
                        {
                            speedQueue.Dequeue();
                        }

                        // UI Throttling: Arayüzü sadece 500ms'de bir güncelle
                        if ((now - lastUiUpdate).TotalMilliseconds >= 500 || globalSent == totalBytes)
                        {
                            var oldest = speedQueue.Peek();
                            var delta = now - oldest.time;
                            long dBytes = globalSent - oldest.bytes;
                            
                            double speedMBps = delta.TotalSeconds > 0 ? (dBytes / delta.TotalSeconds) / 1048576.0 : 0;
                            long remaining   = totalBytes - globalSent;
                            double seconds = speedMBps > 0 ? remaining / (speedMBps * 1048576.0) : double.MaxValue;
                            
                            var remTime = seconds > TimeSpan.MaxValue.TotalSeconds 
                                ? TimeSpan.MaxValue 
                                : TimeSpan.FromSeconds(seconds);

                            OnProgress(new TransferProgress(globalSent, totalBytes, speedMBps,
                                                            remTime, Path.GetFileName(relName),
                                                            i + 1, entries.Count));
                            lastUiUpdate = now;
                        }
                    }
                }

                await stream.FlushAsync(token);
                
                OnStatus("Alıcının onay (ACK) göndermesi bekleniyor...");
                var ackBuf = new byte[1];
                int ackRead = await stream.ReadAsync(ackBuf, token);
                if (ackRead == 0) throw new IOException("Alıcı aktarımı tamamlamadan bağlantıyı kesti.");

                OnStatus($"✓ Transfer tamamlandı: {entries.Count} dosya, {FormatSize(totalBytes)}");
                return new TransferResult(true, "Transfer başarılı.");
            }
            catch (OperationCanceledException)
            {
                OnStatus("Transfer iptal edildi.");
                return new TransferResult(false, "Kullanıcı tarafından iptal edildi.");
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
            {
                OnError("Alıcı uygulama dinlemiyor. Alıcı tarafı başlatın.");
                return new TransferResult(false, "Bağlantı reddedildi.");
            }
            catch (SocketException ex)
            {
                OnError($"Ağ hatası: {ex.Message}");
                return new TransferResult(false, ex.Message);
            }
            catch (IOException ex)
            {
                OnError($"Bağlantı kesildi: {ex.Message}");
                return new TransferResult(false, "Bağlantı kesildi.");
            }
            finally { client?.Close(); }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  ALMA
        // ══════════════════════════════════════════════════════════════════════

        public async Task<TransferResult> StartReceivingAsync(
            string saveDirectory,
            int    port     = DefaultPort,
            string listenIP = "0.0.0.0")
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Directory.CreateDirectory(saveDirectory);
            OnStatus($"Dinleniyor: {listenIP}:{port}...");

            TcpListener? listener = null;
            TcpClient?   client   = null;
            try
            {
                listener = new TcpListener(IPAddress.Parse(listenIP), port);
                listener.Server.SetSocketOption(SocketOptionLevel.Socket,
                                                SocketOptionName.ReuseAddress, true);
                listener.Start();
                OnStatus("Bağlantı bekleniyor...");

                client = await AcceptWithCancellationAsync(listener, token);
                string senderIP = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
                OnStatus($"Bağlantı alındı: {senderIP}");

                client.ReceiveBufferSize = BufferSize * 4;
                client.NoDelay           = true;

                await using var stream = client.GetStream();
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
                using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

                var mode      = (TransferMode)reader.ReadInt32();
                
                if (mode == TransferMode.Flash)
                {
                    var flash = new FlashTransferService();
                    flash.StatusChanged += (s, msg) => OnStatus(msg);
                    flash.Error += (s, msg) => OnError(msg);
                    flash.ProgressChanged += (s, p) => 
                    {
                        OnProgress(new TransferProgress(
                            p.TotalBytesReceived, 1, p.SpeedMBps, p.RemainingTime, 
                            "Flash Modu Çoklu Aktarım", 1, 1));
                        FlashProgressChanged?.Invoke(this, p);
                    };
                    return await flash.ReceiveFlashAsync(client, reader, writer, saveDirectory);
                }

                int fileCount = reader.ReadInt32();

                OnStatus($"{fileCount} dosya alınacak (mod: {mode})...");

                long totalBytes = 0; // Bilinmiyor — toplam sonradan hesaplanır
                long totalRecv  = 0;
                var speedQueue = new Queue<(long bytes, DateTime time)>();
                DateTime lastUiUpdate = DateTime.MinValue;

                for (int i = 0; i < fileCount; i++)
                {
                    token.ThrowIfCancellationRequested();

                    // Dosya başlığı
                    int relLen        = reader.ReadInt32();
                    byte[] relBytes   = reader.ReadBytes(relLen);
                    string relPath    = Encoding.UTF8.GetString(relBytes);
                    long   fileSize   = reader.ReadInt64();
                    totalBytes       += fileSize;

                    // Hedef yol belirle
                    string savePath;
                    if (mode == TransferMode.Folder)
                        savePath = Path.Combine(saveDirectory, relPath);
                    else
                        savePath = Path.Combine(saveDirectory, Path.GetFileName(relPath));

                    // Çakışma önleme (sadece tek dosya modunda)
                    if (mode == TransferMode.Files && File.Exists(savePath))
                    {
                        string ext   = Path.GetExtension(relPath);
                        string base_ = Path.GetFileNameWithoutExtension(relPath);
                        savePath = Path.Combine(saveDirectory,
                                                $"{base_}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

                    OnStatus($"Alınıyor ({i + 1}/{fileCount}): {Path.GetFileName(savePath)} ({FormatSize(fileSize)})");

                    await using var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write,
                                                        FileShare.None, BufferSize, useAsync: true);
                    var buf       = new byte[BufferSize];
                    long received = 0;

                    while (received < fileSize)
                    {
                        token.ThrowIfCancellationRequested();
                        int toRead  = (int)Math.Min(BufferSize, fileSize - received);
                        int rd      = await stream.ReadAsync(buf.AsMemory(0, toRead), token);
                        if (rd == 0) throw new IOException("Gönderici bağlantıyı kapattı.");

                        await fs.WriteAsync(buf.AsMemory(0, rd), token);
                        received   += rd;
                        totalRecv  += rd;

                        var now = DateTime.UtcNow;
                        speedQueue.Enqueue((totalRecv, now));

                        // 5 saniyeden eski verileri kuyruktan çıkar (Hareketli Ortalama)
                        while (speedQueue.Count > 1 && (now - speedQueue.Peek().time).TotalSeconds > 5)
                        {
                            speedQueue.Dequeue();
                        }

                        // UI Throttling: Arayüzü sadece 500ms'de bir güncelle
                        if ((now - lastUiUpdate).TotalMilliseconds >= 500 || received == fileSize)
                        {
                            var oldest = speedQueue.Peek();
                            var delta = now - oldest.time;
                            long dBytes = totalRecv - oldest.bytes;
                            
                            double speedMBps = delta.TotalSeconds > 0 ? (dBytes / delta.TotalSeconds) / 1048576.0 : 0;
                            long remaining   = fileSize - received;
                            double seconds = speedMBps > 0 ? remaining / (speedMBps * 1048576.0) : double.MaxValue;
                            
                            var remTime = seconds > TimeSpan.MaxValue.TotalSeconds 
                                ? TimeSpan.MaxValue 
                                : TimeSpan.FromSeconds(seconds);

                            OnProgress(new TransferProgress(totalRecv, totalBytes > 0 ? totalBytes : fileSize,
                                                            speedMBps, remTime, Path.GetFileName(savePath),
                                                            i + 1, fileCount));
                            lastUiUpdate = now;
                        }
                    }
                }

                OnStatus("Veriler alındı, onay (ACK) gönderiliyor...");
                await stream.WriteAsync(new byte[] { 1 }, token);
                await stream.FlushAsync(token);

                OnStatus($"✓ {fileCount} dosya alındı → {saveDirectory}");
                return new TransferResult(true, $"{fileCount} dosya kaydedildi: {saveDirectory}");
            }
            catch (OperationCanceledException)
            {
                OnStatus("Dinleme iptal edildi.");
                return new TransferResult(false, "İptal edildi.");
            }
            catch (IOException ex)
            {
                OnError($"Bağlantı kesildi: {ex.Message}");
                return new TransferResult(false, "Bağlantı kesildi.");
            }
            catch (Exception ex)
            {
                OnError($"Hata: {ex.Message}");
                return new TransferResult(false, ex.Message);
            }
            finally
            {
                client?.Close();
                listener?.Stop();
            }
        }

        // ─── Yardımcılar ──────────────────────────────────────────────────────
        private static async Task<TcpClient> AcceptWithCancellationAsync(
            TcpListener listener, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<TcpClient>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var reg = token.Register(() => tcs.TrySetCanceled(token));
            var acceptTask = listener.AcceptTcpClientAsync();
            var winner = await Task.WhenAny(acceptTask, tcs.Task);
            if (winner == tcs.Task)
            {
                // AcceptTask'in daha sonra hata fırlatması durumunda programı çökertmesini engelle
                _ = acceptTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                throw new OperationCanceledException(token);
            }
            return await acceptTask;
        }

        public static string FormatSize(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F2} GB";
            if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F2} MB";
            if (bytes >= 1024)          return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} B";
        }
    }
}
