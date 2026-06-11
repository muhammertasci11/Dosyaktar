using System;
using System.Windows;
using System.Windows.Controls;
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

            // Set language dropdown
            foreach (ComboBoxItem item in CmbLanguage.Items)
            {
                if (item.Tag?.ToString() == current.Language)
                {
                    CmbLanguage.SelectedItem = item;
                    break;
                }
            }

            // Alt sürüm etiketi
            TxtVersion.Text = $"Dosyaktar v{UpdateService.GetCurrentVersion()}";

            CmbLanguage.SelectionChanged += CmbLanguage_SelectionChanged;
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLanguage.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                string lang = item.Tag.ToString() ?? "tr";
                var dict = new ResourceDictionary();
                dict.Source = new Uri($"pack://application:,,,/Resources/Strings.{lang}.xaml", UriKind.Absolute);
                // Language dictionary is at index 1
                Application.Current.Resources.MergedDictionaries[1] = dict;
            }
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

        private async void BtnFixFirewall_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show(
                "Bu işlem, uygulamanın Windows Güvenlik Duvarı'ndaki eski yasaklarını silecek ve " +
                "Kablolu/Kablosuz tüm ağlarda çalışabilmesi için tam erişim izni verecektir.\n\n" +
                "Devam etmek için Yönetici (Administrator) onayı vermeniz gereklidir. Onaylıyor musunuz?",
                "Güvenlik Duvarını Onar", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes) return;

            try
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exePath)) return;

                // Eski kuralları sil ve yenisini ekle
                string commands = $@"
netsh advfirewall firewall delete rule name=""Dosyaktar""
netsh advfirewall firewall add rule name=""Dosyaktar"" dir=in action=allow program=""{exePath}"" enable=yes profile=any
netsh advfirewall firewall add rule name=""Dosyaktar"" dir=out action=allow program=""{exePath}"" enable=yes profile=any
";
                string tempScript = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dosyaktar_fw_fix.bat");
                System.IO.File.WriteAllText(tempScript, commands);

                var proc = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = tempScript,
                        Verb = "runas", // Yönetici ayrıcalığı iste
                        UseShellExecute = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    }
                };
                
                proc.Start();
                await proc.WaitForExitAsync();
                
                MessageBox.Show("Güvenlik Duvarı başarıyla onarıldı! Artık diğer cihazlarla (Ethernet dahil) sorunsuz bağlantı kurabilirsiniz.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Onarım sırasında hata oluştu. Yönetici onayı vermemiş olabilirsiniz.\n\nHata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void BtnCloseWindow_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (!int.TryParse(TxtPort.Text, out int port) || port < 1024 || port > 65535)
            {
                port = FileTransferService.DefaultPort; // Geçersiz port ise varsayılana dön
            }

            string lang = "tr";
            if (CmbLanguage.SelectedItem is ComboBoxItem item && item.Tag != null)
                lang = item.Tag.ToString() ?? "tr";

            var newSettings = new AppSettings
            {
                SaveDirectory = TxtSaveDir.Text,
                Port          = port,
                CheckUpdates  = ChkUpdates.IsChecked   == true,
                AutoUpdate    = ChkAutoUpdate.IsChecked == true,
                AutoReceive   = ChkAutoReceive.IsChecked == true,
                DarkMode      = ChkDarkMode.IsChecked   == true,
                LastTargetIP  = _originalSettings.LastTargetIP,
                Language      = lang
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
            LastTargetIP  = s.LastTargetIP,
            Language      = s.Language
        };
    }
}
