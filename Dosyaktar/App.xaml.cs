using System;
using System.Windows;
using System.Threading.Tasks;

namespace Dosyaktar
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
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

            base.OnStartup(e);
        }
    }
}
