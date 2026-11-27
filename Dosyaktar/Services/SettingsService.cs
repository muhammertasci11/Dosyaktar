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
        public bool   AutoReceive   { get; set; } = true;  // Oturum açıkken dinlemeye başla
        public bool   DarkMode      { get; set; } = false;
        public string Theme         { get; set; } = "prism";
        public string Hostname      { get; set; } = Environment.MachineName;
        public string AvatarId      { get; set; } = "kedi";
        public string LastTargetIP  { get; set; } = string.Empty;
        public string LastPage      { get; set; } = "send";
        public string Language      { get; set; } = "tr";
        public bool   UseFlashMode  { get; set; } = false;

        private int _flashConnections = 8;
        public int FlashConnections
        {
            get => _flashConnections;
            set => _flashConnections = Math.Clamp(value, 2, 32);
        }

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
            // CLI screenshot runs must leave the user's settings untouched.
            if (App.Ephemeral) return;

            try
            {
                Directory.CreateDirectory(_dir);
                File.WriteAllText(_path, JsonSerializer.Serialize(s, _opts));
            }
            catch { /* kaydetme hatası → sessizce geç */ }
        }
    }
}
