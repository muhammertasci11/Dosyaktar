using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Dosyaktar.Services
{
    /// <summary>
    /// Ağ yöneticisi — statik IP DEĞİŞTİRMEZ.
    /// Mevcut ağ adapterlerini keşfeder, yerel IP'yi döndürür,
    /// hedef IP'ye ping atar ve erişilebilirlik kontrolü yapar.
    /// </summary>
    public class NetworkManager
    {
        // ── Olaylar ───────────────────────────────────────────────────────────
        public event EventHandler<string>? LogMessage;
        public event EventHandler<string>? Error;

        private void Log(string m) => LogMessage?.Invoke(this, m);
        private void Err(string m) => Error?.Invoke(this, m);

        // ═════════════════════════════════════════════════════════════════════
        //  Yerel IP Tespiti
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Makineye ait en uygun yerel IPv4 adresini döndürür.
        /// Önce aktif Ethernet, sonra Wi-Fi, son çare tüm arayüzler.
        /// </summary>
        public static string GetLocalIP()
        {
            try
            {
                // En güvenilir yöntem: 8.8.8.8'e UDP bağlantı kurarak kaynak IP'yi bul
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                socket.Connect("8.8.8.8", 65530);
                var ep = socket.LocalEndPoint as IPEndPoint;
                if (ep != null) return ep.Address.ToString();
            }
            catch { /* internet yoksa yedek yönteme geç */ }

            // Yedek: ağ arayüzlerini dolaş
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                         .OrderByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                         .ThenByDescending(n => n.OperationalStatus == OperationalStatus.Up))
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address))
                        return addr.Address.ToString();
            }

            return "127.0.0.1";
        }

        /// <summary>
        /// Aynı ağdaki tüm aktif IPv4 adreslerini listeler.
        /// </summary>
        public static List<string> GetAllLocalIPs()
        {
            var result = new List<string>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address))
                        result.Add(addr.Address.ToString());
            }
            return result;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Bağlantı Testi
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Hedef IP'ye ping atar. timeoutMs içinde yanıt gelmezse false döner.
        /// </summary>
        public async Task<(bool Ok, long RoundtripMs)> PingAsync(string host, int timeoutMs = 2000)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host, timeoutMs);
                bool ok = reply.Status == IPStatus.Success;
                Log(ok
                    ? $"Ping {host} → {reply.RoundtripTime} ms"
                    : $"Ping {host} → {reply.Status}");
                return (ok, reply.RoundtripTime);
            }
            catch (Exception ex)
            {
                Err($"Ping hatası ({host}): {ex.Message}");
                return (false, -1);
            }
        }

        /// <summary>
        /// Hedef IP:port'a TCP bağlantı denemesi yapar.
        /// Alıcı uygulamanın dinleyip dinlemediğini test eder.
        /// </summary>
        public async Task<bool> TestTcpPortAsync(string host, int port, int timeoutMs = 3000)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(host, port);
                if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) == connectTask)
                {
                    await connectTask; // exception'ı yüzey çıkar
                    Log($"TCP {host}:{port} → açık");
                    return true;
                }
                Log($"TCP {host}:{port} → zaman aşımı");
                return false;
            }
            catch
            {
                Log($"TCP {host}:{port} → kapalı/erişilemiyor");
                return false;
            }
        }

        /// <summary>
        /// IP adresi formatı doğrulama.
        /// </summary>
        public static bool IsValidIP(string ip) =>
            IPAddress.TryParse(ip, out var addr) &&
            addr.AddressFamily == AddressFamily.InterNetwork;

        // IDisposable — artık netsh operasyonu yok, boş bırakılabilir
        public void Dispose() { }
    }
}
