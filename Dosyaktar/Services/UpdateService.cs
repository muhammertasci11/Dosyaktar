using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Dosyaktar.Services
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Güncelleme konfigürasyonu
    //  ⚠ SADECE BU İKİ SATIRI DEĞİŞTİRİN:
    // ─────────────────────────────────────────────────────────────────────────
    internal static class UpdateConfig
    {
        public const string GitHubOwner = "muhammertasci11";
        public const string GitHubRepo  = "Dosyaktar";
        public const string AssetName   = "Dosyaktar.exe";
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Güncelleme bilgisi DTO
    // ─────────────────────────────────────────────────────────────────────────
    public sealed record UpdateInfo(
        Version LatestVersion,
        string  DownloadUrl,
        string  ReleaseNotes,
        string  TagName
    );

    // ─────────────────────────────────────────────────────────────────────────
    //  Güncelleme Servisi
    //
    //  Akış:
    //    1. CheckForUpdateAsync()  → GitHub Releases API'ye bakar
    //    2. DownloadUpdateAsync()  → %TEMP%'e indirir, ilerleme bildirir
    //    3. ApplyUpdate()          → .bat script ile eski EXE'yi yenisiyle
    //                                değiştirir ve uygulamayı yeniden başlatır
    //
    //  Otomatik mod (AutoUpdate = true):
    //    MainWindow açılışında CheckAndAutoUpdateAsync() çağrılır;
    //    arka planda indirir, biter bitmez uygular (kullanıcı onayı gerekmez).
    // ─────────────────────────────────────────────────────────────────────────
    public sealed class UpdateService
    {
        private static readonly HttpClient _http = CreateHttpClient();

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<int>?    ProgressChanged; // 0-100

        private void OnStatus(string s) => StatusChanged?.Invoke(this, s);
        private void OnProgress(int p)  => ProgressChanged?.Invoke(this, p);

        // ── HTTP Client ───────────────────────────────────────────────────────
        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("Dosyaktar", GetCurrentVersion().ToString()));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        // ── Mevcut sürüm ──────────────────────────────────────────────────────
        public static Version GetCurrentVersion()
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version
                    ?? new Version(1, 0, 0, 0);
            return new Version(v.Major, v.Minor, v.Build);
        }

        // ── Güncelleme kontrolü ───────────────────────────────────────────────
        /// <summary>
        /// GitHub'daki en son release'i kontrol eder.
        /// Yeni sürüm varsa <see cref="UpdateInfo"/> döner, yoksa/hata varsa null.
        /// </summary>
        public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
        {
            try
            {
                string url = $"https://api.github.com/repos/{UpdateConfig.GitHubOwner}" +
                             $"/{UpdateConfig.GitHubRepo}/releases/latest";

                OnStatus("GitHub'da güncel sürüm aranıyor...");

                using var resp = await _http.GetAsync(url, ct);

                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Repo henüz yok, sessizce geç. (Kullanıcı manuel güncelleme yapmak istemiyor/kurulum yapmamış)
                    return null;
                }
                if (!resp.IsSuccessStatusCode)
                {
                    OnStatus($"⚠ GitHub API hatası: HTTP {(int)resp.StatusCode}");
                    return null;
                }

                string json = await resp.Content.ReadAsStringAsync(ct);
                using var doc  = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tag = root.GetProperty("tag_name").GetString() ?? "";
                if (!TryParseVersion(tag, out var latest))
                {
                    OnStatus($"⚠ Geçersiz sürüm etiketi: {tag}");
                    return null;
                }

                var current = GetCurrentVersion();
                if (latest <= current)
                {
                    OnStatus($"✓ Güncelsiniz (v{current})");
                    return null;
                }

                string? downloadUrl = FindAssetUrl(root);
                if (downloadUrl == null)
                {
                    OnStatus($"⚠ Release'de '{UpdateConfig.AssetName}' bulunamadı — release'e Dosyaktar.exe ekleyin.");
                    return null;
                }

                string notes = root.TryGetProperty("body", out var body) ? (body.GetString() ?? "") : "";
                OnStatus($"🎉 Yeni sürüm bulundu: {tag} (şu an: v{current})");
                return new UpdateInfo(latest, downloadUrl, notes, tag);
            }
            catch (HttpRequestException)
            {
                OnStatus("⚠ İnternet bağlantısı yok — güncelleme kontrol edilemedi.");
                return null;
            }
            catch (Exception ex)
            {
                OnStatus($"⚠ Güncelleme hatası: {ex.Message}");
                return null;
            }
        }

        private static string? FindAssetUrl(JsonElement root)
        {
            if (!root.TryGetProperty("assets", out var assets)) return null;
            foreach (var asset in assets.EnumerateArray())
            {
                string? name = asset.GetProperty("name").GetString();
                if (string.Equals(name, UpdateConfig.AssetName,
                                  StringComparison.OrdinalIgnoreCase))
                    return asset.GetProperty("browser_download_url").GetString();
            }
            return null;
        }

        // ── İndirme ───────────────────────────────────────────────────────────
        /// <summary>
        /// Yeni exe'yi %TEMP%'e indirir. İlerleme 0-100 arası <see cref="ProgressChanged"/> ile bildirilir.
        /// </summary>
        public async Task<string> DownloadUpdateAsync(
            UpdateInfo info,
            IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            OnStatus("Güncelleme indiriliyor...");
            string dest = Path.Combine(Path.GetTempPath(), "Dosyaktar_update.exe");

            using var resp = await _http.GetAsync(
                info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            long total = resp.Content.Headers.ContentLength ?? -1;

            await using var src   = await resp.Content.ReadAsStreamAsync(ct);
            await using var outFs = new FileStream(dest, FileMode.Create, FileAccess.Write,
                                                   FileShare.None, 81920, true);

            var  buf        = new byte[81920];
            long downloaded = 0;
            int  read;

            while ((read = await src.ReadAsync(buf, ct)) > 0)
            {
                await outFs.WriteAsync(buf.AsMemory(0, read), ct);
                downloaded += read;

                if (total > 0)
                {
                    int pct = (int)(downloaded * 100 / total);
                    progress?.Report(pct);
                    OnProgress(pct);
                }
            }

            progress?.Report(100);
            OnProgress(100);
            OnStatus($"İndirme tamamlandı. ({FormatBytes(downloaded)})");
            return dest;
        }

        // ── Uygula (Tüm PC'lerde geçerli) ───────────────────────────────────
        /// <summary>
        /// Geçici bir .bat dosyası oluşturur:
        ///   1. Bu process kapanana kadar bekler.
        ///   2. İndirilen EXE'yi mevcut EXE'nin üzerine kopyalar.
        ///   3. Uygulamayı yeniden başlatır.
        ///
        /// Bu mekanizma sayesinde hem gönderici hem alıcı PC'deki uygulama
        /// bir sonraki açılışta güncellenmiş olur — ayrı kurulum gerekmez.
        /// </summary>
        public static void ApplyUpdate(string newExePath)
        {
            string currentExe = Process.GetCurrentProcess().MainModule!.FileName;
            int    currentPid = Process.GetCurrentProcess().Id;
            string batPath    = Path.Combine(Path.GetTempPath(), "dosyaktar_updater.bat");

            string bat = $"""
@echo off
chcp 65001 >nul
title Dosyaktar Guncelleyici

:WAIT
tasklist /FI "PID eq {currentPid}" 2>NUL | find /I "{currentPid}" >NUL
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto WAIT
)

echo Guncelleniyor...
copy /Y "{newExePath}" "{currentExe}"
if errorlevel 1 (
    echo HATA: Dosya kopyalanamadi. Yonetici hakki gerekebilir.
    pause
    goto END
)

echo Baslatiliyor...
explorer.exe "{currentExe}"

:END
del "{newExePath}" 2>nul
del "%~f0" 2>nul
""";

            File.WriteAllText(batPath, bat, System.Text.Encoding.UTF8);

            Process.Start(new ProcessStartInfo
            {
                FileName        = batPath,
                UseShellExecute = true,
                WindowStyle     = ProcessWindowStyle.Hidden,
                Verb            = "runas" // yönetici olarak çalıştır
            });

            System.Windows.Application.Current.Dispatcher.Invoke(
                System.Windows.Application.Current.Shutdown);
        }

        // ── Tam otomatik güncelleme (sessiz mod) ──────────────────────────────
        /// <summary>
        /// <para>Uygulama açılışında çağrılır.</para>
        /// <para>
        /// Eğer <paramref name="silent"/> = true ise kullanıcıya sormadan
        /// indirir ve uygular (tam otomatik).
        /// </para>
        /// <para>
        /// Eğer <paramref name="silent"/> = false ise sadece kontrol eder,
        /// yeni sürüm varsa UpdateInfo döner → UI güncelleştirme penceresini açar.
        /// </para>
        /// </summary>
        public async Task<UpdateInfo?> CheckAndMaybeAutoUpdateAsync(
            bool silent,
            CancellationToken ct = default)
        {
            var info = await CheckForUpdateAsync(ct);
            if (info == null) return null;

            if (!silent) return info; // UI penceresi açılsın

            // Sessiz mod: indir ve uygula
            try
            {
                OnStatus($"Yeni sürüm bulundu: {info.TagName} — arka planda indiriliyor...");
                string exe = await DownloadUpdateAsync(info, ct: ct);
                OnStatus("Güncelleme hazır, uygulama yeniden başlatılıyor...");
                await Task.Delay(500, ct);
                ApplyUpdate(exe);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                OnStatus($"Otomatik güncelleme başarısız: {ex.Message}");
                return info; // başarısızsa UI penceresi açılsın
            }
            return null;
        }

        // ── Yardımcılar ───────────────────────────────────────────────────────
        private static bool TryParseVersion(string tag, out Version version)
        {
            version = new Version(0, 0, 0);
            string clean = tag.TrimStart('v', 'V').Trim();
            return Version.TryParse(clean, out version!);
        }

        private static string FormatBytes(long bytes) =>
            bytes >= 1_048_576
                ? $"{bytes / 1_048_576.0:F1} MB"
                : $"{bytes / 1024.0:F0} KB";
    }
}
