using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Dosyaktar.Services
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Oyun paketi — alıcı tarafın oyunu tanıması için gereken her şey
    // ═══════════════════════════════════════════════════════════════════════
    public sealed class GameTransferMeta
    {
        public string Format      { get; set; } = "dosyaktar.game/1";
        public string Name        { get; set; } = "";
        public string Provider    { get; set; } = "folder";
        public string? AppId      { get; set; }
        public string InstallDir  { get; set; } = "";
        public long   TotalBytes  { get; set; }
        public DateTime SentUtc   { get; set; } = DateTime.UtcNow;

        public SteamPart? Steam   { get; set; }
        public EpicPart?  Epic    { get; set; }

        public sealed class SteamPart
        {
            public string AcfName { get; set; } = "";
            public string Acf     { get; set; } = "";
        }

        public sealed class EpicPart
        {
            public string ManifestName { get; set; } = "";
            public string Json         { get; set; } = "";
        }
    }

    public sealed record GameInstallResult(
        bool   Registered,
        string Message,
        string? TargetPath = null);

    // ═══════════════════════════════════════════════════════════════════════
    //  Gönderim + alıcı taraf kurulum
    // ═══════════════════════════════════════════════════════════════════════
    public static class GameTransferService
    {
        /// <summary>Paket içinde taşınan meta dosyasının göreli yolu.</summary>
        public const string MetaRelPath = "__dosyaktar/game.json";

        // ── gönderim ──────────────────────────────────────────────────────
        public static async Task<TransferResult> SendAsync(
            FileTransferService sender,
            GameEntry game,
            string receiverIp,
            int port,
            long sizeHint = 0)
        {
            if (!Directory.Exists(game.InstallPath))
                return new TransferResult(false, "Oyun klasörü bulunamadı.");

            var meta = BuildMeta(game, sizeHint);
            string tempDir = Path.Combine(Path.GetTempPath(), "dosyaktar");
            Directory.CreateDirectory(tempDir);
            string tempFile = Path.Combine(tempDir, $"game-{Guid.NewGuid():N}.json");

            try
            {
                File.WriteAllText(tempFile, JsonSerializer.Serialize(meta,
                    new JsonSerializerOptions { WriteIndented = false }));

                long metaSize = new FileInfo(tempFile).Length;
                var extra = new List<(string path, string relName, long size)>
                {
                    (tempFile, MetaRelPath, metaSize)
                };

                return await sender.SendPackageAsync(
                    game.InstallPath, extra, receiverIp, port);
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }

        public static GameTransferMeta BuildMeta(GameEntry game, long sizeHint = 0)
        {
            var meta = new GameTransferMeta
            {
                Name       = game.Name,
                Provider   = game.ProviderId,
                AppId      = game.AppId,
                InstallDir = game.FolderName,
                TotalBytes = sizeHint > 0 ? sizeHint : game.SizeBytes
            };

            if (game.Provider == GameProvider.Steam && game.ManifestPath != null
                && File.Exists(game.ManifestPath))
            {
                meta.Steam = new GameTransferMeta.SteamPart
                {
                    AcfName = Path.GetFileName(game.ManifestPath),
                    Acf     = File.ReadAllText(game.ManifestPath)
                };
            }

            if (game.Provider == GameProvider.Epic && game.ManifestPath != null
                && File.Exists(game.ManifestPath))
            {
                meta.Epic = new GameTransferMeta.EpicPart
                {
                    ManifestName = Path.GetFileName(game.ManifestPath),
                    Json         = File.ReadAllText(game.ManifestPath)
                };
            }

            return meta;
        }

        // ── alıcı taraf ───────────────────────────────────────────────────
        /// <summary>
        /// İndirme klasöründe bir oyun paketi varsa onu işler: Steam oyununu
        /// kütüphaneye yerleştirip manifestini yazar, Epic manifestini günceller.
        /// Paket yoksa null döner (normal dosya aktarımı).
        /// </summary>
        public static GameInstallResult? TryFinalize(string receivedDir)
        {
            string metaPath = Path.Combine(receivedDir, MetaRelPath.Replace('/', '\\'));
            if (!File.Exists(metaPath)) return null;

            GameTransferMeta? meta;
            try
            {
                meta = JsonSerializer.Deserialize<GameTransferMeta>(File.ReadAllText(metaPath));
            }
            catch { meta = null; }

            if (meta == null)
            {
                CleanupMeta(receivedDir);
                return new GameInstallResult(false, "Oyun bilgisi okunamadı; dosyalar klasörde kaldı.");
            }

            string staging = Path.Combine(receivedDir, meta.InstallDir);
            if (!Directory.Exists(staging))
            {
                CleanupMeta(receivedDir);
                return new GameInstallResult(false,
                    "Oyun klasörü beklenen yerde değil; dosyalar indirme klasöründe kaldı.");
            }

            try
            {
                return meta.Provider switch
                {
                    "steam" => FinalizeSteam(meta, staging, receivedDir),
                    "epic"  => FinalizeEpic(meta, staging),
                    _       => new GameInstallResult(false,
                        $"{meta.Name} klasör olarak kaydedildi: {staging}")
                };
            }
            catch (Exception ex)
            {
                return new GameInstallResult(false,
                    $"{meta.Name} taşınamadı: {ex.Message}. Dosyalar: {staging}");
            }
            finally
            {
                CleanupMeta(receivedDir);
            }
        }

        // ── Steam ─────────────────────────────────────────────────────────
        private static GameInstallResult FinalizeSteam(
            GameTransferMeta meta, string staging, string receivedDir)
        {
            if (meta.Steam == null || string.IsNullOrWhiteSpace(meta.Steam.Acf))
                return new GameInstallResult(false,
                    $"{meta.Name} Steam manifesti olmadan geldi; klasör olarak kaldı: {staging}");

            string? steamRoot = PlatformScanner.GetSteamPath();
            if (steamRoot == null || !Directory.Exists(steamRoot))
                return new GameInstallResult(false,
                    "Bu bilgisayarda Steam bulunamadı. Oyun klasörü indirme klasöründe: " + staging);

            var libraries = PlatformScanner.GetSteamLibraryPaths(steamRoot)
                .Where(Directory.Exists)
                .ToList();
            if (libraries.Count == 0) libraries.Add(steamRoot);

            string stagingDrive = Path.GetPathRoot(Path.GetFullPath(staging)) ?? "C:\\";

            string library = libraries
                .OrderByDescending(lib => string.Equals(
                    Path.GetPathRoot(lib), stagingDrive, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(FreeSpace)
                .First();

            string common = Path.Combine(library, "steamapps", "common");
            Directory.CreateDirectory(common);

            string target = Path.Combine(common, meta.InstallDir);
            if (Directory.Exists(target))
                return new GameInstallResult(false,
                    $"{meta.Name} bu kütüphanede zaten kurulu: {target}");

            MoveDirectory(staging, target);

            // manifesti kütüphaneye yaz
            string apps = Path.Combine(library, "steamapps");
            Directory.CreateDirectory(apps);
            string acfPath = Path.Combine(apps, meta.Steam.AcfName);
            File.WriteAllText(acfPath,
                RewriteAcf(meta.Steam.Acf, library, GameLibraryService.MeasureFolder(target)),
                new UTF8Encoding(false));

            return new GameInstallResult(true,
                $"{meta.Name} Steam kütüphanesine eklendi: {library}", target);
        }

        private static long FreeSpace(string path)
        {
            try { return new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace; }
            catch { return 0; }
        }

        /// <summary>
        /// ACF metnindeki yol, boyut ve durum alanlarını hedef kütüphaneye göre yeniden yazar.
        /// Diğer tüm alanlar (appid, name, installdir, buildid) korunur.
        /// </summary>
        internal static string RewriteAcf(string acf, string libraryPath, long sizeBytes)
        {
            string escaped = libraryPath.Replace("\\", "\\\\");
            var output = new List<string>();

            foreach (var raw in acf.Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = raw.TrimStart();
                int tab = raw.Length - trimmed.Length;
                string indent = raw[..tab];

                if (trimmed.StartsWith("\"Path\"", StringComparison.OrdinalIgnoreCase))
                {
                    output.Add($"{indent}\"Path\"\t\t\"{escaped}\"");
                }
                else if (trimmed.StartsWith("\"SizeOnDisk\"", StringComparison.OrdinalIgnoreCase))
                {
                    output.Add($"{indent}\"SizeOnDisk\"\t\t\"{sizeBytes}\"");
                }
                else if (trimmed.StartsWith("\"StateFlags\"", StringComparison.OrdinalIgnoreCase))
                {
                    output.Add($"{indent}\"StateFlags\"\t\t\"4\"");
                }
                else if (trimmed.StartsWith("\"StagingPath\"", StringComparison.OrdinalIgnoreCase)
                      || trimmed.StartsWith("\"ContentStatsPath\"", StringComparison.OrdinalIgnoreCase)
                      || trimmed.StartsWith("\"SharedDepots\"", StringComparison.OrdinalIgnoreCase))
                {
                    // taşınan oyun için geçersiz alanlar
                }
                else
                {
                    output.Add(raw);
                }
            }

            string result = string.Join("\n", output);
            if (!result.EndsWith("\n")) result += "\n";
            return result;
        }

        // ── Epic ──────────────────────────────────────────────────────────
        private static GameInstallResult FinalizeEpic(GameTransferMeta meta, string staging)
        {
            if (meta.Epic == null || string.IsNullOrWhiteSpace(meta.Epic.Json))
                return new GameInstallResult(false,
                    $"{meta.Name} Epic manifesti olmadan geldi; klasör olarak kaldı: {staging}");

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");

            if (!Directory.Exists(dir))
                return new GameInstallResult(false,
                    "Epic Games Launcher bu bilgisayarda yok. Oyun klasörü: " + staging);

            try
            {
                using var doc = JsonDocument.Parse(meta.Epic.Json);
                var root = doc.RootElement;
                var values = new Dictionary<string, object?>();

                foreach (var prop in root.EnumerateObject())
                {
                    values[prop.Name] = prop.Name switch
                    {
                        "InstallLocation"      => staging,
                        "bIsIncompleteInstall" => false,
                        "StagingLocation"      => "",
                        _ => prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString(),
                            JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                            JsonValueKind.True   => true,
                            JsonValueKind.False  => false,
                            _ => null
                        }
                    };
                }

                string outPath = Path.Combine(dir, meta.Epic.ManifestName);
                File.WriteAllText(outPath,
                    JsonSerializer.Serialize(values,
                        new JsonSerializerOptions { WriteIndented = true }),
                    new UTF8Encoding(false));

                return new GameInstallResult(true,
                    $"{meta.Name} Epic kütüphanesine kaydedildi. Launcher'da 'Doğrula' demen gerekebilir.",
                    staging);
            }
            catch (Exception ex)
            {
                return new GameInstallResult(false,
                    $"{meta.Name} Epic'e kaydedilemedi: {ex.Message}. Klasör: {staging}");
            }
        }

        // ── yardımcılar ───────────────────────────────────────────────────
        private static void CleanupMeta(string receivedDir)
        {
            try
            {
                string dir = Path.Combine(receivedDir, "__dosyaktar");
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch { }
        }

        /// <summary>Aynı sürücüde anında taşır; farklı sürücüde kopyalayıp kaynağı siler.</summary>
        internal static void MoveDirectory(string source, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            string srcRoot = Path.GetPathRoot(Path.GetFullPath(source)) ?? "";
            string dstRoot = Path.GetPathRoot(Path.GetFullPath(target)) ?? "";

            if (string.Equals(srcRoot, dstRoot, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(source, target);
                return;
            }

            CopyDirectory(source, target);
            try { Directory.Delete(source, recursive: true); } catch { }
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);

            foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(source, dir);
                Directory.CreateDirectory(Path.Combine(target, rel));
            }

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(source, file);
                string dst = Path.Combine(target, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(file, dst, overwrite: false);
            }
        }
    }
}
