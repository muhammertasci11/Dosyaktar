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
        //  Güvenlik Duvarı İzni
        // ═════════════════════════════════════════════════════════════════════

        public static void AddFirewallRule()
        {
            string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exePath)) return;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall add rule name=\"Dosyaktar (TCP/UDP)\" dir=in action=allow program=\"{exePath}\" enable=yes profile=any",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            System.Diagnostics.Process.Start(psi)?.WaitForExit();
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Yerel IP Tespiti
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Makineye ait en uygun yerel IPv4 adresini döndürür.
        /// Önce aktif Ethernet, sonra Wi-Fi, son çare tüm arayüzler.
        /// </summary>
        public static string GetLocalIP()
        {
            // Telefonun ulaşabileceği adresi seç: varsayılan ağ geçidi olan gerçek
            // Ethernet/Wi-Fi kartı kazanır. Hyper-V, VirtualBox, WSL, WARP ve diğer
            // sanal/tünel arayüzler puan kaybeder; erişilemez bir adres QR'a yazılmaz.
            try
            {
                var candidates = new List<(int score, string ip)>();

                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    string label = (nic.Name + " " + nic.Description).ToLowerInvariant();
                    bool virtualNic =
                        label.Contains("virtual") || label.Contains("hyper-v") || label.Contains("vethernet") ||
                        label.Contains("wsl") || label.Contains("vmware") || label.Contains("tailscale") ||
                        label.Contains("zerotier") || label.Contains("radmin") || label.Contains("tap-windows") ||
                        label.Contains("wintun") || label.Contains("tunnel") || label.Contains("warp") ||
                        label.Contains("pdanet") || label.Contains("bluetooth") || label.Contains("sanal");

                    var props = nic.GetIPProperties();
                    bool hasGateway = props.GatewayAddresses.Any(g =>
                        g.Address != null &&
                        g.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !g.Address.Equals(IPAddress.Any));

                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(addr.Address)) continue;

                        string ip = addr.Address.ToString();
                        if (ip.StartsWith("169.254.", StringComparison.Ordinal)) continue;
                        if (ip.StartsWith("192.168.56.", StringComparison.Ordinal)) continue;

                        int score = nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 3
                                  : nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 2
                                  : 1;
                        if (hasGateway) score += 10;
                        if (virtualNic) score -= 20;

                        candidates.Add((score, ip));
                    }
                }

                if (candidates.Count > 0)
                    return candidates.OrderByDescending(c => c.score).First().ip;
            }
            catch { }

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
                // Zaman aşımı durumunda, arka planda fail olan Task'in Exception'ını yut
                _ = connectTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
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
