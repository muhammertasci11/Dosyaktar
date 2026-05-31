using System;
using System.IO;
using System.Text.Json;

namespace Dosyaktar.Services
{
    // ─── Uygulama Ayarları ────────────────────────────────────────────────────
    public sealed class AppSettings
    {
        public string SaveDirectory { get; set; } = DefaultSaveDir;
        public int    Port          { get; set; } = FileTransferService.DefaultPort;
        public bool   CheckUpdates  { get; set; } = true;
        public bool   AutoUpdate    { get; set; } = true;  // Varsayılan: otomatik güncelle
        public bool   AutoReceive   { get; set; } = false;
        public bool   DarkMode      { get; set; } = false;
        public string LastTargetIP  { get; set; } = string.Empty;

        public static string DefaultSaveDir =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", "Dosyaktar");
    }

    // ─── Ayar Kaydetme / Yükleme Servisi ──────────────────────────────────────
    public static class SettingsService
    {
        private static readonly string _dir =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Dosyaktar");

        private static readonly string _path;
        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

        static SettingsService() => _path = Path.Combine(_dir, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(_path)) return new AppSettings();
                return JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(_path), _opts) ?? new AppSettings();
            }
            catch { return new AppSettings(); }
        }

        public static void Save(AppSettings s)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                File.WriteAllText(_path, JsonSerializer.Serialize(s, _opts));
            }
            catch { /* kaydetme hatası → sessizce geç */ }
        }
    }
}
