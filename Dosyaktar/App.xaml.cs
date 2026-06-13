using System;
using System.Windows;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;

namespace Dosyaktar
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Smart First-Run: Masaüstü Kısayolu Oluşturma
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName;
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string shortcutPath = Path.Combine(desktop, "Dosyaktar.lnk");

                if (exePath != null && !File.Exists(shortcutPath))
                {
                    string vbsPath = Path.Combine(Path.GetTempPath(), "CreateDosyaktarShortcut.vbs");
                    string vbsCode = $@"
Set WshShell = CreateObject(""WScript.Shell"")
Set Shortcut = WshShell.CreateShortcut(""{shortcutPath}"")
Shortcut.TargetPath = ""{exePath}""
Shortcut.WorkingDirectory = ""{Path.GetDirectoryName(exePath)}""
Shortcut.Save
";
                    File.WriteAllText(vbsPath, vbsCode);
                    var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = "wscript.exe",
                        Arguments = $"\"{vbsPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    p?.WaitForExit();
                    if (File.Exists(vbsPath)) File.Delete(vbsPath);
                }
            }
            catch { /* Sessizce devam et */ }
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                MessageBox.Show($"FATA Kilitlenmesi:\n\n{ex?.Message}\n\n{ex?.StackTrace}", "Kritik Hata (AppDomain)", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                var ex = args.Exception;
                MessageBox.Show($"Asenkron Hata:\n\n{ex?.Message}", "Kritik Hata (Task)", MessageBoxButton.OK, MessageBoxImage.Error);
                args.SetObserved();
            };

            DispatcherUnhandledException += (s, ex) =>
            {
                var inner = ex.Exception;
                while (inner.InnerException != null)
                    inner = inner.InnerException;

                MessageBox.Show(
                    $"Arayüz Hatası: {inner.Message}\n\nTür: {inner.GetType().Name}\n\nKaynak: {inner.Source}\n\n{inner.StackTrace}",
                    "Beklenmeyen Hata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                ex.Handled = true;
            };

            // Kaydedilen temayı uygula (LightTheme.xaml zaten MergedDictionaries'de, dark ise değiştir)
            var settings = Dosyaktar.Services.SettingsService.Load();
            if (settings.DarkMode)
                ThemeManager.Apply(dark: true);

            // Uygulama dili yükleme
            string lang = settings.Language ?? "tr";
            var dict = new ResourceDictionary();
            try
            {
                dict.Source = new Uri($"pack://application:,,,/Resources/Strings.{lang}.xaml", UriKind.Absolute);
                Application.Current.Resources.MergedDictionaries[1] = dict;
            }
            catch { /* Varsayılan kalır */ }

            base.OnStartup(e);
        }
    }
}
