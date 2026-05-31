using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Dosyaktar.Services
{
    // ─── Keşfedilen cihaz bilgisi ─────────────────────────────────────────────
    public sealed class DiscoveredPeer
    {
        public string Hostname  { get; init; } = string.Empty;
        public string IP        { get; init; } = string.Empty;
        public int    Port      { get; init; }
        public string Version   { get; init; } = string.Empty;
        public bool   IsReceiving { get; set; }   // "Almaya Başla" modunda mı?
        public DateTime LastSeen  { get; set; }

        public string DisplayName =>
            string.IsNullOrEmpty(Hostname) ? IP : $"{Hostname}  ({IP})";

        public string StatusText =>
            IsReceiving ? "📥 Almaya hazır" : "🟢 Çevrimiçi";
    }

    // ─── Keşif Servisi ────────────────────────────────────────────────────────
    /// <summary>
    /// Aynı yerel ağdaki Dosyaktar örneklerini UDP broadcast ile otomatik keşfeder.
    ///
    /// Protokol:
    ///   - Discovery port : 5002  (sabit)
    ///   - Transfer port  : 5001  (ayarlanabilir)
    ///   - Her 2 saniyede bir broadcast UDP paketi yayınlanır
    ///   - Paket içeriği : UTF-8 JSON  { hostname, ip, port, version, isReceiving }
    ///   - 8 saniye cevap alınmayan peer listeden çıkarılır
    /// </summary>
    public sealed class NetworkDiscovery : IDisposable
    {
        // ── Sabitler ──────────────────────────────────────────────────────────
        public  const int DiscoveryPort  = 5002;
        private const int BroadcastMs   = 2000;
        private const int PeerTimeoutMs = 8000;

        // ── Olaylar ───────────────────────────────────────────────────────────
        /// <summary>Peer listesi değiştiğinde (eklenme/çıkarılma/güncelleme) tetiklenir.</summary>
        public event EventHandler<IReadOnlyList<DiscoveredPeer>>? PeersChanged;
        public event EventHandler<string>?                         LogMessage;

        // ── İç durum ──────────────────────────────────────────────────────────
        private readonly ConcurrentDictionary<string, DiscoveredPeer> _peers = new();
        private CancellationTokenSource? _cts;
        private bool _isReceiving;
        private int  _transferPort;
        private bool _disposed;

        private void Log(string m) => LogMessage?.Invoke(this, m);

        // ═════════════════════════════════════════════════════════════════════
        //  Başlat / Durdur
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Hem broadcast hem listener döngüsünü başlatır.
        /// </summary>
        /// <param name="transferPort">Bu cihazın dosya transferi için kullandığı port.</param>
        /// <param name="isReceiving">True ise "Almaya hazır" ikonuyla görünür.</param>
        public void Start(int transferPort = FileTransferService.DefaultPort, bool isReceiving = false)
        {
            Stop();
            _transferPort = transferPort;
            _isReceiving  = isReceiving;
            _cts = new CancellationTokenSource();

            Task.Run(() => BroadcastLoopAsync(_cts.Token));
            Task.Run(() => ListenLoopAsync(_cts.Token));
            Task.Run(() => CleanupLoopAsync(_cts.Token));

            Log("Cihaz keşfi başlatıldı.");
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        /// <summary>Receiving modunu günceller — sonraki broadcast paketinde yayınlanır.</summary>
        public void SetReceivingMode(bool isReceiving) => _isReceiving = isReceiving;

        // ═════════════════════════════════════════════════════════════════════
        //  Broadcast (her 2 saniyede bir "Ben buradayım" yay)
        // ═════════════════════════════════════════════════════════════════════

        private async Task BroadcastLoopAsync(CancellationToken ct)
        {
            try
            {
                using var udp = new UdpClient();
                udp.EnableBroadcast = true;
                var endpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        string myIP = NetworkManager.GetLocalIP();
                        var payload = new
                        {
                            hostname    = Environment.MachineName,
                            ip          = myIP,
                            port        = _transferPort,
                            version     = UpdateService.GetCurrentVersion().ToString(),
                            isReceiving = _isReceiving
                        };

                        byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
                        await udp.SendAsync(data, data.Length, endpoint);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        Log($"Broadcast gönderme hatası: {ex.Message}");
                    }

                    await Task.Delay(BroadcastMs, ct).ContinueWith(_ => { }); // hata yutma
                }
            }
            catch (Exception ex)
            {
                Log($"Broadcast döngüsü durdu: {ex.Message}");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Listener (diğer cihazların paketlerini al)
        // ═════════════════════════════════════════════════════════════════════

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            try
            {
                UdpClient? udp = null;
                try
                {
                    udp = new UdpClient();
                    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                    udp.EnableBroadcast = true;

                    string myIP = NetworkManager.GetLocalIP();

                    while (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            var result = await udp.ReceiveAsync(ct);
                            string json = Encoding.UTF8.GetString(result.Buffer);

                            using var doc  = JsonDocument.Parse(json);
                            var root = doc.RootElement;

                            string ip   = root.TryGetProperty("ip",          out var ipEl)   ? ipEl.GetString()   ?? "" : result.RemoteEndPoint.Address.ToString();
                            string host = root.TryGetProperty("hostname",    out var hEl)    ? hEl.GetString()    ?? "" : ip;
                            int    port = root.TryGetProperty("port",        out var portEl) ? portEl.GetInt32()       : FileTransferService.DefaultPort;
                            string ver  = root.TryGetProperty("version",     out var verEl)  ? verEl.GetString()  ?? "" : "";
                            bool   recv = root.TryGetProperty("isReceiving", out var recvEl) && recvEl.GetBoolean();

                            // Kendi broadcast paketini yoksay
                            if (ip == myIP) continue;

                            bool changed;
                            if (_peers.TryGetValue(ip, out var existing))
                            {
                                bool wasReceiving = existing.IsReceiving;
                                existing.IsReceiving = recv;
                                existing.LastSeen    = DateTime.UtcNow;
                                changed = wasReceiving != recv;
                            }
                            else
                            {
                                var peer = new DiscoveredPeer
                                {
                                    Hostname    = host,
                                    IP          = ip,
                                    Port        = port,
                                    Version     = ver,
                                    IsReceiving = recv,
                                    LastSeen    = DateTime.UtcNow
                                };
                                _peers[ip] = peer;
                                Log($"Yeni cihaz: {host} ({ip})");
                                changed = true;
                            }

                            if (changed) RaisePeersChanged();
                        }
                        catch (Exception ex) when (!ct.IsCancellationRequested)
                        {
                            Log($"Dinleme paket hatası: {ex.Message}");
                            await Task.Delay(500, ct).ContinueWith(_ => { });
                        }
                    }
                }
                finally
                {
                    udp?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log($"Dinleme döngüsü durdu (Port kullanımda olabilir): {ex.Message}");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Cleanup (8 sn cevap vermeyen peer'ı listeden çıkar)
        // ═════════════════════════════════════════════════════════════════════

        private async Task CleanupLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(3000, ct).ContinueWith(_ => { });

                    bool changed = false;
                    var cutoff   = DateTime.UtcNow - TimeSpan.FromMilliseconds(PeerTimeoutMs);

                    foreach (var kv in _peers)
                    {
                        if (kv.Value.LastSeen < cutoff)
                        {
                            _peers.TryRemove(kv.Key, out _);
                            Log($"Cihaz gitti: {kv.Value.DisplayName}");
                            changed = true;
                        }
                    }

                    if (changed) RaisePeersChanged();
                }
            }
            catch (Exception ex)
            {
                Log($"Temizleme döngüsü hatası: {ex.Message}");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Yardımcılar
        // ═════════════════════════════════════════════════════════════════════

        public IReadOnlyList<DiscoveredPeer> GetPeers() =>
            new List<DiscoveredPeer>(_peers.Values);

        private void RaisePeersChanged() =>
            PeersChanged?.Invoke(this, GetPeers());

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
