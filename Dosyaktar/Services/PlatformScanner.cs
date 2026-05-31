using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;

namespace Dosyaktar.Services
{
    // ─── Tespit Edilen Oyun / Uygulama ────────────────────────────────────────
    public sealed record InstalledGame(
        string Name,
        string Platform,   // "Steam" | "Epic" | "Folder"
        string InstallPath,
        string? ManifestPath,
        long   SizeBytes
    )
    {
        public string SizeText => FileTransferService.FormatSize(SizeBytes);
    }

    // ─── Platform Tarayıcısı ──────────────────────────────────────────────────
    public static class PlatformScanner
    {
        // ══════════════════════════════════════════════════════════════════════
        //  STEAM
        // ══════════════════════════════════════════════════════════════════════

        public static List<InstalledGame> ScanSteam()
        {
            var results = new List<InstalledGame>();
            try
            {
                string? steamPath = GetSteamPath();
                if (steamPath == null) return results;

                // Path separator normalize et
                steamPath = NormalizePath(steamPath);

                if (!Directory.Exists(steamPath)) return results;

                var libraryPaths = GetSteamLibraries(steamPath);
                foreach (var lib in libraryPaths)
                {
                    string manifestDir = Path.Combine(lib, "steamapps");
                    if (!Directory.Exists(manifestDir)) continue;

                    foreach (var acf in Directory.GetFiles(manifestDir, "appmanifest_*.acf"))
                    {
                        try
                        {
                            var game = ParseSteamManifest(acf, lib);
                            if (game != null) results.Add(game);
                        }
                        catch { /* hatalı manifest → atla */ }
                    }
                }
            }
            catch { /* Steam yüklü değil */ }
            return results;
        }

        private static string? GetSteamPath()
        {
            // Yöntem 1: CurrentUser
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                var val = key?.GetValue("SteamPath") as string
                       ?? key?.GetValue("steampath") as string;
                if (!string.IsNullOrEmpty(val)) return val;
            }
            catch { }

            // Yöntem 2: LocalMachine 32-bit
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\WOW6432Node\Valve\Steam");
                var val = key?.GetValue("InstallPath") as string;
                if (!string.IsNullOrEmpty(val)) return val;
            }
            catch { }

            // Yöntem 3: LocalMachine 64-bit
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Valve\Steam");
                var val = key?.GetValue("InstallPath") as string;
                if (!string.IsNullOrEmpty(val)) return val;
            }
            catch { }

            // Yöntem 4: Bilinen varsayılan yollar
            var defaults = new[]
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam",
                Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            };
            return defaults.FirstOrDefault(Directory.Exists);
        }

        private static List<string> GetSteamLibraries(string steamPath)
        {
            var libs = new List<string> { steamPath };

            string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) return libs;

            try
            {
                string content = File.ReadAllText(vdf);
                // Yeni format (2021+): "path"    "D:\\Games\\Steam"
                // Eski format: "1"    "D:\\Games\\Steam"
                bool inLibraryEntry = false; // format ayırt edici

                foreach (var rawLine in content.Split('\n'))
                {
                    var line = rawLine.Trim();

                    // Her iki formatı da tara
                    // Yeni: satır "path" içeriyor
                    if (line.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase))
                    {
                        var path = ExtractQuotedValue(line, 1);
                        if (path != null)
                        {
                            path = NormalizePath(path);
                            if (Directory.Exists(path) && !libs.Contains(path, StringComparer.OrdinalIgnoreCase))
                                libs.Add(path);
                        }
                        continue;
                    }

                    // Eski format: satır sayısal bir key içeriyor, değeri yol
                    // "1"    "D:\\Games"
                    var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[0].Trim(), out _))
                    {
                        string candidate = NormalizePath(parts[1]);
                        if (Directory.Exists(candidate) &&
                            !libs.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                            libs.Add(candidate);
                    }
                }
            }
            catch { /* VDF okunamadı */ }

            return libs;
        }

        private static string? ExtractQuotedValue(string line, int valueIndex)
        {
            // "key"   "value" → valueIndex=1 → value döner
            var parts = new List<string>();
            bool inQuote = false;
            var current  = new System.Text.StringBuilder();

            foreach (char c in line)
            {
                if (c == '"')
                {
                    if (inQuote) { parts.Add(current.ToString()); current.Clear(); }
                    inQuote = !inQuote;
                }
                else if (inQuote) current.Append(c);
            }

            return parts.Count > valueIndex ? parts[valueIndex] : null;
        }

        private static InstalledGame? ParseSteamManifest(string acfPath, string libraryRoot)
        {
            string? name       = null;
            string? installDir = null;

            foreach (var line in File.ReadAllLines(acfPath))
            {
                var t = line.Trim();
                if (t.StartsWith("\"name\"", StringComparison.OrdinalIgnoreCase) && name == null)
                    name = ExtractQuotedValue(t, 1);
                else if (t.StartsWith("\"installdir\"", StringComparison.OrdinalIgnoreCase) && installDir == null)
                    installDir = ExtractQuotedValue(t, 1);

                if (name != null && installDir != null) break;
            }

            if (name == null || installDir == null) return null;
            string gamePath = Path.Combine(libraryRoot, "steamapps", "common", installDir);
            if (!Directory.Exists(gamePath)) return null;

            long size = DirectorySize(gamePath);
            return new InstalledGame(name, "Steam", gamePath, acfPath, size);
        }

        private static string NormalizePath(string path)
        {
            // Forward slash → backslash, çift backslash → tek
            return path.Replace('/', Path.DirectorySeparatorChar)
                       .Replace(@"\\", @"\");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  EPIC GAMES
        // ══════════════════════════════════════════════════════════════════════

        public static List<InstalledGame> ScanEpic()
        {
            var results = new List<InstalledGame>();
            try
            {
                string manifestDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Epic", "EpicGamesLauncher", "Data", "Manifests");

                if (!Directory.Exists(manifestDir)) return results;

                foreach (var item in Directory.GetFiles(manifestDir, "*.item"))
                {
                    try
                    {
                        var game = ParseEpicManifest(item);
                        if (game != null) results.Add(game);
                    }
                    catch { /* hatalı manifest → atla */ }
                }
            }
            catch { /* Epic yüklü değil */ }
            return results;
        }

        private static InstalledGame? ParseEpicManifest(string itemPath)
        {
            string json = File.ReadAllText(itemPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete)
                && incomplete.GetBoolean()) return null;

            string? name        = root.TryGetProperty("DisplayName",     out var n) ? n.GetString() : null;
            string? installPath = root.TryGetProperty("InstallLocation", out var l) ? l.GetString() : null;

            if (name == null || installPath == null) return null;
            installPath = NormalizePath(installPath);
            if (!Directory.Exists(installPath)) return null;

            long size = DirectorySize(installPath);
            return new InstalledGame(name, "Epic", installPath, null, size);
        }

        // ─── Yardımcı: Klasör boyutu ──────────────────────────────────────────
        public static long DirectorySize(string path)
        {
            try
            {
                long total = 0;
                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; }
                    catch { /* erişim engeli → atla */ }
                }
                return total;
            }
            catch { return 0; }
        }
    }
}
