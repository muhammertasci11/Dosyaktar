using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Dosyaktar.Services
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Sağlayıcı kimliği
    // ═══════════════════════════════════════════════════════════════════════
    public enum GameProvider
    {
        Steam,
        Epic,
        Gog,
        BattleNet,
        Ubisoft,
        Ea,
        Riot,
        Folder
    }

    public static class GameProviderInfo
    {
        public static string Key(GameProvider p) => p switch
        {
            GameProvider.Steam     => "steam",
            GameProvider.Epic      => "epic",
            GameProvider.Gog       => "gog",
            GameProvider.BattleNet => "battlenet",
            GameProvider.Ubisoft   => "ubisoft",
            GameProvider.Ea        => "ea",
            GameProvider.Riot      => "riot",
            _                      => "folder"
        };

        public static string Name(GameProvider p) => p switch
        {
            GameProvider.Steam     => "Steam",
            GameProvider.Epic      => "Epic Games",
            GameProvider.Gog       => "GOG",
            GameProvider.BattleNet => "Battle.net",
            GameProvider.Ubisoft   => "Ubisoft",
            GameProvider.Ea        => "EA",
            GameProvider.Riot      => "Riot",
            _                      => "Klasör"
        };

        public static string TintKey(GameProvider p) => p switch
        {
            GameProvider.Steam     => "SteamTintBrush",
            GameProvider.Epic      => "EpicTintBrush",
            GameProvider.Gog       => "GogTintBrush",
            GameProvider.BattleNet => "BattleNetTintBrush",
            GameProvider.Ubisoft   => "UbisoftTintBrush",
            GameProvider.Ea        => "EaTintBrush",
            GameProvider.Riot      => "RiotTintBrush",
            _                      => "FolderTintBrush"
        };

        public static string DotKey(GameProvider p) => p switch
        {
            GameProvider.Steam     => "SteamDotBrush",
            GameProvider.Epic      => "EpicDotBrush",
            GameProvider.Gog       => "GogDotBrush",
            GameProvider.BattleNet => "BattleNetDotBrush",
            GameProvider.Ubisoft   => "UbisoftDotBrush",
            GameProvider.Ea        => "EaDotBrush",
            GameProvider.Riot      => "RiotDotBrush",
            _                      => "FolderDotBrush"
        };

        public static GameProvider Parse(string? key) => (key ?? "").Trim().ToLowerInvariant() switch
        {
            "steam"     => GameProvider.Steam,
            "epic"      => GameProvider.Epic,
            "gog"       => GameProvider.Gog,
            "battlenet" => GameProvider.BattleNet,
            "ubisoft"   => GameProvider.Ubisoft,
            "ea"        => GameProvider.Ea,
            "riot"      => GameProvider.Riot,
            _           => GameProvider.Folder
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Oyun kaydı
    // ═══════════════════════════════════════════════════════════════════════
    public sealed class GameEntry : INotifyPropertyChanged
    {
        private long _sizeBytes;

        public required string Name        { get; init; }
        public required string InstallPath { get; init; }
        public required GameProvider Provider { get; init; }

        public string? AppId          { get; init; }
        public string? ManifestPath   { get; init; }
        public string? ExecutablePath { get; init; }
        public string? LaunchUri      { get; init; }
        public string? CoverPath      { get; set; }
        public DateTime? LastPlayed   { get; init; }
        public bool   IsCustom        { get; init; }
        public bool   SizeFromManifest{ get; init; }

        public string ProviderId   => GameProviderInfo.Key(Provider);
        public string ProviderName => GameProviderInfo.Name(Provider);
        public string ProviderTintKey => GameProviderInfo.TintKey(Provider);
        public string ProviderDotKey  => GameProviderInfo.DotKey(Provider);

        public string Id => $"{ProviderId}:{AppId ?? Path.GetFileName(InstallPath)}";
        public string FolderName => Path.GetFileName(InstallPath.TrimEnd('\\', '/'));
        public string Initial => string.IsNullOrWhiteSpace(Name)
            ? "?"
            : Name.Trim()[..1].ToUpperInvariant();
        public bool IsSteam => Provider == GameProvider.Steam;
        public bool IsEpic  => Provider == GameProvider.Epic;
        public bool CanLaunch => !string.IsNullOrEmpty(LaunchUri) || !string.IsNullOrEmpty(ExecutablePath);

        public long SizeBytes
        {
            get => _sizeBytes;
            set
            {
                if (_sizeBytes == value) return;
                _sizeBytes = value;
                Raise(nameof(SizeBytes));
                Raise(nameof(SizeText));
            }
        }

        public string SizeText => _sizeBytes > 0
            ? FileTransferService.FormatSize(_sizeBytes)
            : "—";

        public string LastPlayedText => LastPlayed.HasValue
            ? $"Son oynama: {LastPlayed.Value:dd MMM yyyy}"
            : "Hiç oynanmadı";

        public string StatusText => SizeFromManifest ? "Dosya boyutu manifestten" : "";

        private ImageSource? _cover;
        public ImageSource? Cover
        {
            get => _cover;
            set { _cover = value; Raise(nameof(Cover)); Raise(nameof(HasCover)); }
        }

        public bool HasCover => _cover != null;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public void LoadCover()
        {
            if (_cover != null || string.IsNullOrWhiteSpace(CoverPath)) return;

            var image = DecodeCover(CoverPath);
            if (image != null) Cover = image;
        }

        /// <summary>Kapak dosyasını UI dışında çözülebilecek biçimde dondurur.</summary>
        public static ImageSource? DecodeCover(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                if (!File.Exists(path)) return null;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = 460;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null; // bozuk görsel → rozet görünür kalır
            }
        }

        public GameEntry WithSize(long bytes)
        {
            SizeBytes = bytes;
            return this;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Boyut önbelleği — 600 GB oyunu her açılışta yeniden taramak yok
    // ═══════════════════════════════════════════════════════════════════════
    internal static class GameSizeCache
    {
        private sealed class Row
        {
            public long Size { get; set; }
            public DateTime Utc { get; set; }
        }

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Dosyaktar", "game-sizes.json");

        private static readonly Dictionary<string, Row> _rows =
            new(StringComparer.OrdinalIgnoreCase);

        private static bool _loaded;
        private static readonly object _gate = new();

        public static void Load()
        {
            lock (_gate)
            {
                if (_loaded) return;
                _loaded = true;
                try
                {
                    if (!File.Exists(FilePath)) return;
                    var data = JsonSerializer.Deserialize<Dictionary<string, Row>>(
                        File.ReadAllText(FilePath));
                    if (data == null) return;
                    foreach (var kv in data) _rows[kv.Key] = kv.Value;
                }
                catch { /* bozuk önbellek → yok say */ }
            }
        }

        public static long? Get(string path)
        {
            Load();
            lock (_gate)
                return _rows.TryGetValue(path, out var row) ? row.Size : null;
        }

        public static bool IsFresh(string path, TimeSpan maxAge)
        {
            Load();
            lock (_gate)
                return _rows.TryGetValue(path, out var row)
                    && DateTime.UtcNow - row.Utc < maxAge;
        }

        public static void Put(string path, long size)
        {
            Load();
            lock (_gate)
                _rows[path] = new Row { Size = size, Utc = DateTime.UtcNow };
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                lock (_gate)
                    File.WriteAllText(FilePath,
                        JsonSerializer.Serialize(_rows,
                            new JsonSerializerOptions { WriteIndented = false }));
            }
            catch { /* yazılamazsa önbellek sadece bu oturumda kalır */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Kullanıcının elle eklediği klasörler
    // ═══════════════════════════════════════════════════════════════════════
    internal static class CustomGameStore
    {
        private sealed class Row
        {
            public string Name { get; set; } = "";
            public string Path { get; set; } = "";
        }

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Dosyaktar", "game-folders.json");

        public static List<(string Name, string Path)> Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new();
                var rows = JsonSerializer.Deserialize<List<Row>>(File.ReadAllText(FilePath));
                return rows?.Where(r => !string.IsNullOrWhiteSpace(r.Path))
                           .Select(r => (r.Name, r.Path)).ToList() ?? new();
            }
            catch { return new(); }
        }

        public static void Save(IEnumerable<(string Name, string Path)> rows)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(
                    rows.Select(r => new Row { Name = r.Name, Path = r.Path })));
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Tarayıcı
    // ═══════════════════════════════════════════════════════════════════════
    public sealed class GameLibraryService
    {
        public event EventHandler<string>? Status;

        private CancellationTokenSource? _sizeCts;
        public bool IsScanning { get; private set; }
        public bool IsSizing   { get; private set; }

        // ── tarama ────────────────────────────────────────────────────────
        public async Task<List<GameEntry>> ScanAsync(CancellationToken ct = default)
        {
            IsScanning = true;
            Status?.Invoke(this, "Kütüphaneler taranıyor…");

            var tasks = new (string label, Func<CancellationToken, Task<List<GameEntry>>> run)[]
            {
                ("Steam",      ScanSteamAsync),
                ("Epic",       ScanEpicAsync),
                ("GOG",        ScanGogAsync),
                ("Battle.net", ScanBattleNetAsync),
                ("Ubisoft",    ScanUbisoftAsync),
                ("EA",         ScanEaAsync),
                ("Riot",       ScanRiotAsync)
            };

            var found = new List<GameEntry>();
            foreach (var (label, run) in tasks)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    var part = await run(ct);
                    if (part.Count > 0)
                    {
                        Status?.Invoke(this, $"{label}: {part.Count} oyun");
                        found.AddRange(part);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Status?.Invoke(this, $"{label} taranamadı: {ex.Message}");
                }
            }

            // elle eklenen klasörler
            foreach (var (name, path) in CustomGameStore.Load())
            {
                if (!Directory.Exists(path)) continue;
                found.Add(new GameEntry
                {
                    Name        = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : name,
                    InstallPath = path,
                    Provider    = GameProvider.Folder,
                    IsCustom    = true,
                    CoverPath   = FindLocalIcon(path)
                });
            }

            // aynı klasörü iki kez gösterme
            var unique = found
                .GroupBy(g => Path.GetFullPath(g.InstallPath), StringComparer.OrdinalIgnoreCase)
                .Select(g => g
                    .OrderByDescending(x => x.Provider != GameProvider.Folder)
                    .ThenByDescending(x => x.SizeBytes)
                    .First())
                .ToList();

            // önbellekteki boyutları uygula
            foreach (var g in unique)
            {
                if (g.SizeBytes > 0) continue;
                if (GameSizeCache.IsFresh(g.InstallPath, TimeSpan.FromDays(30)))
                    g.SizeBytes = GameSizeCache.Get(g.InstallPath) ?? 0;
                else
                {
                    var cached = GameSizeCache.Get(g.InstallPath);
                    if (cached is > 0) g.SizeBytes = cached.Value;
                }
            }

            unique.Sort((a, b) =>
            {
                int byProvider = a.Provider.CompareTo(b.Provider);
                if (byProvider != 0 && (a.LastPlayed == null) != (b.LastPlayed == null))
                    return byProvider;

                if (a.LastPlayed.HasValue && b.LastPlayed.HasValue)
                {
                    int byDate = b.LastPlayed.Value.CompareTo(a.LastPlayed.Value);
                    if (byDate != 0) return byDate;
                }
                else if (a.LastPlayed.HasValue != b.LastPlayed.HasValue)
                    return a.LastPlayed.HasValue ? -1 : 1;

                int bySize = b.SizeBytes.CompareTo(a.SizeBytes);
                if (bySize != 0) return bySize;

                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });

            IsScanning = false;
            Status?.Invoke(this, $"{unique.Count} oyun bulundu");
            return unique;
        }

        // ── boyutları arka planda tamamla ─────────────────────────────────
        public async Task SizeMissingAsync(IReadOnlyList<GameEntry> games,
                                           IProgress<GameEntry>? progress = null,
                                           CancellationToken ct = default)
        {
            if (IsSizing) return;
            IsSizing = true;
            _sizeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                var todo = games.Where(g => g.SizeBytes <= 0 && Directory.Exists(g.InstallPath))
                                .ToList();
                int done = 0;

                foreach (var g in todo)
                {
                    ct.ThrowIfCancellationRequested();
                    long size = await Task.Run(() => MeasureFolder(g.InstallPath, ct), ct);
                    g.SizeBytes = size;
                    GameSizeCache.Put(g.InstallPath, size);
                    done++;
                    progress?.Report(g);
                }

                if (todo.Count > 0) GameSizeCache.Save();
            }
            catch (OperationCanceledException) { }
            finally
            {
                IsSizing = false;
                _sizeCts?.Dispose();
                _sizeCts = null;
            }
        }

        public void CancelSizing() => _sizeCts?.Cancel();

        public static long MeasureFolder(string path, CancellationToken ct = default)
        {
            long total = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        total += info.Length;
                    }
                    catch { /* erişim yok → atla */ }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* yarım sonuç yine işe yarar */ }
            return total;
        }

        // ══════════════════════════════════════════════════════════════════
        //  STEAM
        // ══════════════════════════════════════════════════════════════════
        private async Task<List<GameEntry>> ScanSteamAsync(CancellationToken ct)
        {
            await Task.Yield();
            var games = new List<GameEntry>();

            string? root = PlatformScanner.GetSteamPath();
            if (root == null || !Directory.Exists(root)) return games;

            var libraries = PlatformScanner.GetSteamLibraryPaths(root);
            foreach (var lib in libraries)
            {
                ct.ThrowIfCancellationRequested();
                string apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;

                foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var manifest = SteamManifest.Parse(acf);
                        if (manifest == null) continue;
                        if (!manifest.IsFullyInstalled) continue;
                        if (manifest.AppId == "228980") continue; // Steamworks ortak bileşenleri

                        string installDir = Path.Combine(apps, "common", manifest.InstallDir);
                        if (!Directory.Exists(installDir)) continue;

                        games.Add(new GameEntry
                        {
                            Name         = manifest.Name,
                            InstallPath  = installDir,
                            Provider     = GameProvider.Steam,
                            AppId        = manifest.AppId,
                            ManifestPath = acf,
                            LaunchUri    = $"steam://rungameid/{manifest.AppId}",
                            LastPlayed   = manifest.LastPlayed,
                            CoverPath    = FindSteamCover(lib, manifest.AppId),
                            SizeFromManifest = manifest.SizeOnDisk > 0
                        }.WithSize(manifest.SizeOnDisk));
                    }
                    catch { /* bozuk manifest → atla */ }
                }
            }

            await Task.CompletedTask;
            return games;
        }

        private static string? FindSteamCover(string library, string appId)
        {
            try
            {
                string cache = Path.Combine(library, "steamapps", "librarycache");
                if (!Directory.Exists(cache)) return null;

                foreach (var name in new[]
                         {
                             $"{appId}_header.jpg",
                             $"{appId}_library_600x900.jpg"
                         })
                {
                    string p = Path.Combine(cache, name);
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            return null;
        }

        // ══════════════════════════════════════════════════════════════════
        //  EPIC
        // ══════════════════════════════════════════════════════════════════
        private async Task<List<GameEntry>> ScanEpicAsync(CancellationToken ct)
        {
            await Task.Yield();
            var games = new List<GameEntry>();

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");

            if (!Directory.Exists(dir)) return games;

            foreach (var item in Directory.EnumerateFiles(dir, "*.item"))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(item));
                    var root = doc.RootElement;

                    if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete)
                        && incomplete.ValueKind == JsonValueKind.True) continue;

                    string? name = Str(root, "DisplayName");
                    string? path = Str(root, "InstallLocation");
                    string? app  = Str(root, "AppName");
                    string? ns   = Str(root, "CatalogNamespace");
                    string? cat  = Str(root, "CatalogItemId");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                    if (!Directory.Exists(path)) continue;

                    string? uri = null;
                    if (!string.IsNullOrWhiteSpace(ns) && !string.IsNullOrWhiteSpace(cat)
                        && !string.IsNullOrWhiteSpace(app))
                        uri = $"com.epicgames.launcher://apps/{ns}%3A{cat}%3A{app}?action=launch&silent=true";

                    string? exe = null;
                    var launchExe = Str(root, "LaunchExecutable");
                    if (!string.IsNullOrWhiteSpace(launchExe))
                    {
                        string candidate = Path.Combine(path, launchExe.Replace('/', '\\'));
                        if (File.Exists(candidate)) exe = candidate;
                    }

                    games.Add(new GameEntry
                    {
                        Name           = name,
                        InstallPath    = path,
                        Provider       = GameProvider.Epic,
                        AppId          = app,
                        ManifestPath   = item,
                        LaunchUri      = uri,
                        ExecutablePath = exe,
                        CoverPath      = FindLocalIcon(path)
                    });
                }
                catch { }
            }

            await Task.CompletedTask;
            return games;
        }

        // ══════════════════════════════════════════════════════════════════
        //  GOG
        // ══════════════════════════════════════════════════════════════════
        private async Task<List<GameEntry>> ScanGogAsync(CancellationToken ct)
        {
            await Task.Yield();
            var games = new List<GameEntry>();

            foreach (var hive in new[] { Registry.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view)
                        .OpenSubKey(@"SOFTWARE\GOG.com\Games");
                    if (root == null) continue;

                    foreach (var id in root.GetSubKeyNames())
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            using var key = root.OpenSubKey(id);
                            if (key == null) continue;

                            string? name = key.GetValue("gameName") as string;
                            string? path = key.GetValue("path") as string;
                            string? exe  = key.GetValue("exe") as string;
                            string? launch = key.GetValue("launchCommand") as string;
                            string? gameId = key.GetValue("gameID") as string ?? id;

                            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) continue;
                            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileName(path);

                            games.Add(new GameEntry
                            {
                                Name           = name!,
                                InstallPath    = path!,
                                Provider       = GameProvider.Gog,
                                AppId          = gameId,
                                ExecutablePath = string.IsNullOrWhiteSpace(exe) ? null : exe,
                                LaunchUri      = string.IsNullOrWhiteSpace(launch)
                                    ? $"goggalaxy://openGameView/{gameId}"
                                    : launch,
                                CoverPath      = FindLocalIcon(path!)
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }

            await Task.CompletedTask;
            return games;
        }

        // ══════════════════════════════════════════════════════════════════
        //  BATTLE.NET
        // ══════════════════════════════════════════════════════════════════
        private async Task<List<GameEntry>> ScanBattleNetAsync(CancellationToken ct)
        {
            await Task.Yield();
            var games = new List<GameEntry>();

            try
            {
                using var root = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\WOW6432Node\Blizzard Entertainment");
                if (root == null) return games;

                foreach (var product in root.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();
                    if (product.Equals("Launcher", StringComparison.OrdinalIgnoreCase)) continue;

                    try
                    {
                        using var key = root.OpenSubKey(product);
                        string? path = key?.GetValue("InstallPath") as string;
                        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) continue;

                        games.Add(new GameEntry
                        {
                            Name        = PrettyBlizzardName(product),
                            InstallPath = path!,
                            Provider    = GameProvider.BattleNet,
                            AppId       = product,
                            CoverPath   = FindLocalIcon(path!)
                        });
                    }
                    catch { }
                }
            }
            catch { }

            await Task.CompletedTask;
            return games;
        }

        private static string PrettyBlizzardName(string product) => product switch
        {
            "WoW"       => "World of Warcraft",
            "D3"        => "Diablo III",
            "Heroes"    => "Heroes of the Storm",
            "Overwatch" => "Overwatch 2",
            "SC2"       => "StarCraft II",
            "W3"        => "Warcraft III: Reforged",
            "Destiny2"  => "Destiny 2",
            _           => product
        };

        // ══════════════════════════════════════════════════════════════════
        //  UBISOFT
        // ══════════════════════════════════════════════════════════════════
        private async Task<List<GameEntry>> ScanUbisoftAsync(CancellationToken ct)
        {
            await Task.Yield();
            var games = new List<GameEntry>();

            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view)
                        .OpenSubKey(@"SOFTWARE\Ubisoft\Launcher\Installs");
                    if (root == null) continue;

                    foreach (var id in root.GetSubKeyNames())
                    {
                        try
                        {
                            using var key = root.OpenSubKey(id);
                            string? path = key?.GetValue("InstallDir") as string;
                            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) continue;

                            games.Add(new GameEntry
                            {
                                Name        = UbisoftDisplayName(id) ?? Path.GetFileName(path.TrimEnd('\\')),
                                InstallPath = path!,
                                Provider    = GameProvider.Ubisoft,
                                AppId       = id,
                                LaunchUri   = $"uplay://launch/{id}/0",
                                CoverPath   = FindLocalIcon(path!)
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }

            await Task.CompletedTask;
            return games;
        }

        private static string? UbisoftDisplayName(string id)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Uplay Install {id}");
                return key?.GetValue("DisplayName") as string;
            }
            catch { return null; }
        }

        // ══════════════════════════════════════════════════════════════════
        //  EA
        // ══════════════════════════════════════════════════════════════════
        private async Task<List<GameEntry>> ScanEaAsync(CancellationToken ct)
        {
            await Task.Yield();
            var games = new List<GameEntry>();

            foreach (var path in new[]
                     {
                         @"SOFTWARE\WOW6432Node\Electronic Arts\EA Games",
                         @"SOFTWARE\Electronic Arts\EA Games"
                     })
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var root = Registry.LocalMachine.OpenSubKey(path);
                    if (root == null) continue;

                    foreach (var game in root.GetSubKeyNames())
                    {
                        try
                        {
                            using var key = root.OpenSubKey(game);
                            string? dir = key?.GetValue("InstallDir") as string;
                            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;

                            games.Add(new GameEntry
                            {
                                Name        = key?.GetValue("DisplayName") as string ?? game,
                                InstallPath = dir!,
                                Provider    = GameProvider.Ea,
                                AppId       = game,
                                CoverPath   = FindLocalIcon(dir!)
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }

            await Task.CompletedTask;
            return games;
        }

        // ══════════════════════════════════════════════════════════════════
        //  RIOT
        // ══════════════════════════════════════════════════════════════════
        private async Task<List<GameEntry>> ScanRiotAsync(CancellationToken ct)
        {
            await Task.Yield();
            var games = new List<GameEntry>();

            var roots = new[]
            {
                @"C:\Riot Games",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Riot Games")
            };

            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                if (!Directory.Exists(root)) continue;

                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    ct.ThrowIfCancellationRequested();
                    string folder = Path.GetFileName(dir);
                    if (folder.StartsWith("Riot Client", StringComparison.OrdinalIgnoreCase)) continue;

                    string? uri = folder.ToLowerInvariant() switch
                    {
                        "league of legends" => "riotclient://launch-product=league_of_legends&launch-patchline=live",
                        "valorant"          => "riotclient://launch-product=valorant&launch-patchline=live",
                        "legends of runeterra" => "riotclient://launch-product=bacon&launch-patchline=live",
                        "2xko"              => "riotclient://launch-product=2xko&launch-patchline=live",
                        _ => null
                    };

                    games.Add(new GameEntry
                    {
                        Name        = folder,
                        InstallPath = dir,
                        Provider    = GameProvider.Riot,
                        AppId       = folder,
                        LaunchUri   = uri,
                        CoverPath   = FindLocalIcon(dir)
                    });
                }
            }

            await Task.CompletedTask;
            return games;
        }

        // ══════════════════════════════════════════════════════════════════
        //  yardımcılar
        // ══════════════════════════════════════════════════════════════════
        private static string? Str(JsonElement root, string name)
            => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        internal static string? FindLocalIcon(string folder)
        {
            try
            {
                var ico = Directory.EnumerateFiles(folder, "*.ico", SearchOption.TopDirectoryOnly)
                                   .FirstOrDefault();
                if (ico != null) return ico;

                var png = Directory.EnumerateFiles(folder, "*.png", SearchOption.TopDirectoryOnly)
                                   .FirstOrDefault(f =>
                                   {
                                       var n = Path.GetFileName(f).ToLowerInvariant();
                                       return n.Contains("icon") || n.Contains("logo") || n.Contains("cover");
                                   });
                return png;
            }
            catch { return null; }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Steam manifest okuyucu (ACF)
    // ═══════════════════════════════════════════════════════════════════════
    public sealed class SteamManifest
    {
        public string AppId       { get; init; } = "";
        public string Name        { get; init; } = "";
        public string InstallDir  { get; init; } = "";
        public long   SizeOnDisk  { get; init; }
        public DateTime? LastPlayed { get; init; }
        public string RawText     { get; init; } = "";
        public string ManifestPath{ get; init; } = "";

        public bool IsFullyInstalled => (StateFlags & 4) != 0;
        public int StateFlags { get; init; }

        public static SteamManifest? Parse(string acfPath)
        {
            if (!File.Exists(acfPath)) return null;
            return ParseText(File.ReadAllText(acfPath), acfPath);
        }

        public static SteamManifest? ParseText(string text, string acfPath = "")
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            string? Val(string key)
            {
                var match = Regex.Match(
                    text,
                    $"\"{Regex.Escape(key)}\"\\s+\"([^\"]*)\"",
                    RegexOptions.IgnoreCase);

                return match.Success ? match.Groups[1].Value : null;
            }

            string? appId = Val("appid");
            string? name  = Val("name");
            string? dir   = Val("installdir");
            if (appId == null || name == null || dir == null) return null;

            long.TryParse(Val("SizeOnDisk"), out long size);
            int.TryParse(Val("StateFlags"), out int flags);

            DateTime? played = null;
            if (long.TryParse(Val("LastPlayed"), out long unix) && unix > 0)
            {
                try { played = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime; }
                catch { }
            }

            return new SteamManifest
            {
                AppId        = appId,
                Name         = name,
                InstallDir   = dir,
                SizeOnDisk   = size,
                StateFlags   = flags,
                LastPlayed   = played,
                RawText      = text,
                ManifestPath = acfPath
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Başlatma / klasör açma
    // ═══════════════════════════════════════════════════════════════════════
    public static class GameLauncher
    {
        public static bool Launch(GameEntry game, out string? error)
        {
            error = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(game.LaunchUri))
                {
                    Process.Start(new ProcessStartInfo(game.LaunchUri) { UseShellExecute = true });
                    return true;
                }

                string? exe = game.ExecutablePath;
                if (exe == null || !File.Exists(exe))
                    exe = FindBestExe(game.InstallPath);

                if (exe == null)
                {
                    error = "Başlatılabilir bir .exe bulunamadı. Klasörden açabilirsin.";
                    return false;
                }

                Process.Start(new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? game.InstallPath
                });
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static void OpenFolder(GameEntry game)
        {
            try
            {
                if (Directory.Exists(game.InstallPath))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{game.InstallPath}\"")
                    {
                        UseShellExecute = true
                    });
            }
            catch { }
        }

        private static string? FindBestExe(string folder)
        {
            try
            {
                var candidates = Directory.EnumerateFiles(folder, "*.exe",
                        SearchOption.TopDirectoryOnly)
                    .Where(p =>
                    {
                        var n = Path.GetFileName(p).ToLowerInvariant();
                        return !n.Contains("unins") && !n.Contains("setup") &&
                               !n.Contains("crash") && !n.Contains("report") &&
                               !n.Contains("redist") && !n.Contains("launcher_");
                    })
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.Length)
                    .ToList();

                return candidates.FirstOrDefault()?.FullName;
            }
            catch { return null; }
        }
    }
}
