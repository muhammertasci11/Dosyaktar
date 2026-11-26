using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
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

        public static void AddFirewallRule() => EnsureFirewallRule(out _);

        /// <summary>
        /// Gelen bağlantılar için bu EXE'ye özel güvenlik duvarı kuralı bırakır.
        /// Uygulama zaten yönetici olarak çalıştığı için netsh sessizce çağrılır;
        /// kural varsa yeniden eklenmez. İndirilen her yeni EXE konumu kendi
        /// kuralını alır, böylece "Almaya hazır" görünen cihaz gerçekten erişilebilir olur.
        /// </summary>
        public static bool EnsureFirewallRule(out string message)
        {
            message = "";

            string exePath;
            try { exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? ""; }
            catch { exePath = ""; }

            if (string.IsNullOrEmpty(exePath))
            {
                message = "Uygulama yolu okunamadı; güvenlik duvarı izni atlandı";
                return false;
            }

            string ruleName = "Dosyaktar " + Fnv(exePath);

            var (showCode, showOut) = RunNetsh($"advfirewall firewall show rule name=\"{ruleName}\" verbose");
            if (showCode == 0 && showOut.Contains(exePath, StringComparison.OrdinalIgnoreCase))
            {
                message = "Güvenlik duvarı izni hazır";
                return true;
            }

            if (showCode == 0)
                RunNetsh($"advfirewall firewall delete rule name=\"{ruleName}\"");

            var (addCode, addOut) = RunNetsh(
                $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow " +
                $"program=\"{exePath}\" enable=yes profile=any");

            bool ok = addCode == 0;
            message = ok
                ? "Güvenlik duvarına gelen bağlantı izni eklendi"
                : "Güvenlik duvarı izni eklenemedi: " + FirstLine(addOut);
            return ok;
        }

        private static (int Code, string Output) RunNetsh(string arguments)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("netsh", arguments)
                {
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                };

                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null) return (-1, "netsh başlatılamadı");

                string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                if (!p.WaitForExit(8000))
                {
                    try { p.Kill(); } catch { }
                    return (-1, output + " (zaman aşımı)");
                }
                return (p.ExitCode, output);
            }
            catch (Exception ex)
            {
                return (-1, ex.Message);
            }
        }

        private static string Fnv(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text.ToLowerInvariant())
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash.ToString("X8");
        }

        private static string FirstLine(string text)
        {
            string trimmed = text.Trim();
            int cut = trimmed.IndexOfAny(new[] { '\r', '\n' });
            return cut > 0 ? trimmed[..cut] : trimmed;
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
        /// TCP bağlantısını sınırlı sürede kurar. Süre aşımında, ne yapılacağını
        /// söyleyen Türkçe bir TimeoutException fırlatır; böylece arayüz 21 saniye
        /// boyunca sessizce beklemez.
        /// </summary>
        public static async Task ConnectTcpAsync(
            TcpClient client,
            string host,
            int port,
            CancellationToken token,
            int timeoutMs = 8000)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(timeoutMs);

            try
            {
                await client.ConnectAsync(host, port, timeout.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"{host}:{port} yanıt vermedi ({timeoutMs / 1000} sn). " +
                    "Karşı bilgisayarda Dosyaktar açık ve \"Almaya başla\" seçili olmalı; " +
                    "güvenlik duvarı iznini kontrol et.");
            }
        }

        /// <summary>
        /// Akıştan, verilen süre içinde okuyamazsa TimeoutException atar.
        /// Onay (ACK) beklerken sonsuza kadar asılı kalmayı engeller.
        /// </summary>
        public static async Task<int> ReadWithTimeoutAsync(
            Stream stream,
            Memory<byte> buffer,
            CancellationToken token,
            int timeoutMs,
            string timeoutMessage)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(timeoutMs);

            try
            {
                return await stream.ReadAsync(buffer, timeout.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException(timeoutMessage);
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
