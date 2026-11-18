using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Dosyaktar.Services
{
    /// <summary>
    /// Oyun kapaklarını sağlayıcıdan bağımsız olarak Steam'in herkese açık
    /// vitrin görsellerinden bulur ve yerel önbelleğe yazar.
    ///
    /// - API anahtarı istemez; Steam araması + CDN başlık görseli kullanır.
    /// - Steam dışı oyunlar (Epic, GOG, klasör) ad üzerinden eşleştirilir.
    /// - Çevrimdışıyken sessizce baş harf rozetine düşer, hata göstermez.
    /// </summary>
    public static class GameCoverService
    {
        private static readonly HttpClient Http = CreateClient();
        private static readonly SemaphoreSlim Gate = new(2, 2);

        // Aynı oyun için oturum boyunca tek deneme: olumsuz sonuç da saklanır.
        private static readonly ConcurrentDictionary<string, string?> Memo = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Steam mağaza aramasının eşleşmesi: uygulama kimliği + kapsül görseli.</summary>
        private readonly record struct SteamMatch(int AppId, string? CapsuleUrl);

        public static string CacheDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Dosyaktar", "covers");

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(9) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Dosyaktar/2.1");
            return client;
        }

        /// <summary>
        /// Oyunun kapağını döndürür: önce yerel dosya, sonra önbellek, sonra ağ.
        /// Bulunamazsa null döner; çağıran baş harf rozetini korur.
        /// </summary>
        public static async Task<string?> EnsureCoverAsync(GameEntry game, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(game.CoverPath) && File.Exists(game.CoverPath))
                return game.CoverPath;

            string key = $"{game.ProviderId}:{game.AppId ?? game.Name}";
            if (Memo.TryGetValue(key, out string? remembered))
                return remembered;

            try
            {
                string? path = null;
                int? knownId = null;

                if (game.Provider == GameProvider.Steam &&
                    int.TryParse(game.AppId, out int parsedId) &&
                    parsedId > 0)
                {
                    knownId = parsedId;
                    path = await DownloadHeaderAsync(parsedId, ct).ConfigureAwait(false);
                }

                if (path == null)
                {
                    SteamMatch? match = await SearchSteamAsync(game.Name, ct).ConfigureAwait(false);

                    if (match is SteamMatch found)
                    {
                        if (knownId != found.AppId)
                            path = await DownloadHeaderAsync(found.AppId, ct).ConfigureAwait(false);

                        // Yeni uygulamalarda genel header yolu 404; mağaza
                        // aramasının verdiği hash'li kapsül her zaman vardır.
                        if (path == null && !string.IsNullOrWhiteSpace(found.CapsuleUrl))
                            path = await DownloadImageAsync(
                                found.CapsuleUrl!,
                                Path.Combine(CacheDir, $"steam-{found.AppId}-capsule.jpg"),
                                ct).ConfigureAwait(false);
                    }
                }

                Memo[key] = path;
                return path;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                Memo[key] = null;
                return null;
            }
        }

        // ── ağ ────────────────────────────────────────────────────────────

        private static async Task<SteamMatch?> SearchSteamAsync(string name, CancellationToken ct)
        {
            string term = CleanName(name);
            if (term.Length < 2) return null;

            string url = "https://store.steampowered.com/api/storesearch/?term=" +
                         Uri.EscapeDataString(term) + "&l=tr&cc=TR";

            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                                           .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array)
                return null;

            SteamMatch? best = null;
            double bestScore = 0;

            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idElement) ||
                    !idElement.TryGetInt32(out int appId) ||
                    appId <= 0)
                    continue;

                string? itemName = item.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(itemName)) continue;

                double score = Similarity(term, itemName!);
                if (score > bestScore)
                {
                    bestScore = score;

                    string? capsule = item.TryGetProperty("tiny_image", out var imageElement)
                        ? imageElement.GetString()
                        : null;

                    best = new SteamMatch(appId, capsule);
                }

                if (bestScore >= 0.95) break;
            }

            return bestScore >= 0.42 ? best : null;
        }

        private static async Task<string?> DownloadHeaderAsync(int appId, CancellationToken ct)
        {
            return await DownloadImageAsync(
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg",
                Path.Combine(CacheDir, $"steam-{appId}.jpg"),
                ct,
                $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg")
                .ConfigureAwait(false);
        }

        private static async Task<string?> DownloadImageAsync(
            string url,
            string path,
            CancellationToken ct,
            params string[] fallbackUrls)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 2048)
                return path;

            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Kilit beklerken başka bir çağrı indirmiş olabilir.
                if (File.Exists(path) && new FileInfo(path).Length > 2048)
                    return path;

                Directory.CreateDirectory(CacheDir);

                var urls = new List<string>(fallbackUrls.Length + 1) { url };
                urls.AddRange(fallbackUrls);

                foreach (string candidate in urls)
                {
                    try
                    {
                        using var response = await Http.GetAsync(candidate, HttpCompletionOption.ResponseHeadersRead, ct)
                                                       .ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode) continue;

                        string media = response.Content.Headers.ContentType?.MediaType ?? "";
                        if (!media.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) continue;

                        byte[] bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                        if (bytes.Length < 2048) continue;

                        string temp = path + ".tmp";
                        await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
                        File.Move(temp, path, overwrite: true);
                        return path;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Bu CDN yanıt vermedi; sıradakini dene.
                    }
                }

                return null;
            }
            finally
            {
                Gate.Release();
            }
        }

        // ── ad eşleştirme ─────────────────────────────────────────────────

        private static string CleanName(string name)
        {
            string cleaned = (name ?? "").Trim();
            cleaned = Regex.Replace(cleaned, @"[™®©]", " ");
            cleaned = Regex.Replace(cleaned, @"[\(\[]\s*[^\)\]]*[\)\]]", " ");
            cleaned = Regex.Replace(cleaned, @"\s+", " ");
            return cleaned.Trim(' ', '-', ':', '–', '—');
        }

        private static HashSet<string> Tokens(string value)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in Regex.Split(value.ToLowerInvariant(), @"[^\p{L}\p{N}]+"))
            {
                if (raw.Length < 2) continue;
                if (raw is "the" or "and" or "of") continue;
                tokens.Add(raw);
            }
            return tokens;
        }

        private static double Similarity(string a, string b)
        {
            var ta = Tokens(a);
            var tb = Tokens(b);
            if (ta.Count == 0 || tb.Count == 0) return 0;

            int common = ta.Intersect(tb).Count();
            double dice = 2.0 * common / (ta.Count + tb.Count);

            // "GTA V" ⊂ "GTA V: Premium Edition" gibi tam kapsamalarda
            // kısa adı cezalandırma; yine de rastgele eşleşmeye izin verme.
            if (common == Math.Min(ta.Count, tb.Count) && common > 0)
                dice = Math.Max(dice, 0.66);

            return dice;
        }
    }
}
