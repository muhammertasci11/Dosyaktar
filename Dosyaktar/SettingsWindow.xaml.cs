using System.Windows;
using Dosyaktar.Helpers;
using Dosyaktar.Services;

namespace Dosyaktar
{
    public partial class SettingsWindow : Window
    {
        private AppSettings _originalSettings;

        public SettingsWindow(AppSettings current)
        {
            InitializeComponent();
            _originalSettings = Clone(current);

            TxtSaveDir.Text          = current.SaveDirectory;
            TxtPort.Text             = current.Port.ToString();
            ChkUpdates.IsChecked     = current.CheckUpdates;
            ChkAutoUpdate.IsChecked  = current.AutoUpdate;
            ChkAutoReceive.IsChecked = current.AutoReceive;
            ChkDarkMode.IsChecked    = current.DarkMode;

            // Alt sürüm etiketi
            TxtVersion.Text = $"Dosyaktar v{UpdateService.GetCurrentVersion()}";
        }

        // Koyu Mod toggle → anında önizleme
        private void ChkDarkMode_Changed(object sender, RoutedEventArgs e)
        {
            bool dark = ChkDarkMode.IsChecked == true;
            ThemeManager.Apply(dark);
        }

        private void BtnBrowseDir_Click(object sender, RoutedEventArgs e)
        {
            string? picked = FolderPicker.ShowDialog(this, "Alınan Dosyalar Klasörü");
            if (picked != null)
                TxtSaveDir.Text = picked;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (!int.TryParse(TxtPort.Text, out int port) || port < 1024 || port > 65535)
            {
                port = FileTransferService.DefaultPort; // Geçersiz port ise varsayılana dön
            }

            var newSettings = new AppSettings
            {
                SaveDirectory = TxtSaveDir.Text,
                Port          = port,
                CheckUpdates  = ChkUpdates.IsChecked   == true,
                AutoUpdate    = ChkAutoUpdate.IsChecked == true,
                AutoReceive   = ChkAutoReceive.IsChecked == true,
                DarkMode      = ChkDarkMode.IsChecked   == true,
                LastTargetIP  = _originalSettings.LastTargetIP
            };
            
            SettingsService.Save(newSettings);
            base.OnClosed(e);
        }

        private static AppSettings Clone(AppSettings s) => new()
        {
            SaveDirectory = s.SaveDirectory,
            Port          = s.Port,
            CheckUpdates  = s.CheckUpdates,
            AutoUpdate    = s.AutoUpdate,
            AutoReceive   = s.AutoReceive,
            DarkMode      = s.DarkMode,
            LastTargetIP  = s.LastTargetIP
        };
    }
}
