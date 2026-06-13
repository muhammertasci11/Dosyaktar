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
            // Tüm ağ arayüzlerini tara: Önce Ethernet, sonra Wi-Fi. (İnterneti olmayan ama 1Gbps/10Gbps hızındaki lokal kablolu ağları tercih etmeli)
            // VirtualBox, Hyper-V, vEthernet, WSL, Loopback, VPN tünelleri yoksayılır.
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && 
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            !n.Description.Contains("Virtual") &&
                            !n.Description.Contains("Hyper-V") &&
                            !n.Description.Contains("Tailscale") &&
                            !n.Description.Contains("Radmin") &&
                            !n.Description.Contains("TAP-Windows") &&
                            !n.Description.Contains("Sanal") &&
                            !n.Description.Contains("ZeroTier") &&
                            !n.Name.Contains("vEthernet") &&
                            !n.Name.Contains("WSL"))
                .OrderByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                .ThenByDescending(n => n.Speed)
                .ToList();

            foreach (var nic in interfaces)
            {
                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr.Address))
                    {
                        // APIPA (169.254.x.x) adreslerini sadece başka çare yoksa kullanmak için sona atabiliriz ama şimdilik ilk bulduğumuzu dönüyoruz.
                        if (addr.Address.ToString().StartsWith("169.254.")) continue;
                        return addr.Address.ToString();
                    }
                }
            }
            
            // Eğer normal bir IP bulunamazsa (veya sadece APIPA varsa), APIPA'yı dön.
            foreach (var nic in interfaces)
            {
                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr.Address))
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
