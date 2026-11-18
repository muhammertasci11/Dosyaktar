using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Dosyaktar.Services;

namespace Dosyaktar
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            RegisterCrashHandlers();
            EnsureDesktopShortcut();

            var settings = SettingsService.Load();

            // Theme first — every window resolves DynamicResource against it.
            ThemeManager.Apply(string.IsNullOrWhiteSpace(settings.Theme) ? "prism" : settings.Theme);

            LoadLanguage(settings.Language);

            var window = new MainWindow(settings);
            MainWindow = window;
            window.Show();
        }

        // ─── language ────────────────────────────────────────────────────────
        private static void LoadLanguage(string? lang)
        {
            string code = string.IsNullOrWhiteSpace(lang) ? "tr" : lang.Trim();
            if (code == "tr") return;

            try
            {
                var dict = new ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/Resources/Strings.{code}.xaml", UriKind.Absolute)
                };

                var merged = Current.Resources.MergedDictionaries;
                if (merged.Count > 1) merged[1] = dict;
                else merged.Add(dict);
            }
            catch
            {
                // Unknown language pack — stay on Turkish.
            }
        }

        // ─── crash reporting ─────────────────────────────────────────────────
        private void RegisterCrashHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex) ShowFatal(ex, "AppDomain");
            };

            TaskScheduler_Unobserved();

            DispatcherUnhandledException += (_, args) =>
            {
                var inner = args.Exception;
                while (inner.InnerException != null) inner = inner.InnerException;

                MessageBox.Show(
                    $"Beklenmeyen bir arayüz hatası oluştu.\n\n{inner.Message}\n\nTür: {inner.GetType().Name}",
                    "Dosyaktar",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                args.Handled = true;
            };
        }

        private static void TaskScheduler_Unobserved()
        {
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                args.SetObserved();
            };
        }

        private static void ShowFatal(Exception ex, string source)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Dosyaktar");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "crash.log"),
                    $"[{DateTime.Now:O}] ({source}) {ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { }

            MessageBox.Show(
                $"Dosyaktar beklenmedik bir hata nedeniyle kapanıyor.\n\n{ex.Message}\n\n" +
                "Ayrıntılar %APPDATA%\\Dosyaktar\\crash.log dosyasına yazıldı.",
                "Dosyaktar",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        // ─── first-run desktop shortcut ──────────────────────────────────────
        private static void EnsureDesktopShortcut()
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;

                // Only for the packaged single-file build, not `dotnet run`.
                if (exePath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return;

                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string shortcutPath = Path.Combine(desktop, "Dosyaktar.lnk");
                if (File.Exists(shortcutPath)) return;

                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;

                object? shell = Activator.CreateInstance(shellType);
                if (shell == null) return;

                object? shortcut = shellType.InvokeMember(
                    "CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
                    null, shell, new object[] { shortcutPath });

                if (shortcut == null) return;

                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty,
                    null, shortcut, new object[] { exePath });
                shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty,
                    null, shortcut, new object[] { Path.GetDirectoryName(exePath) ?? string.Empty });
                shortcutType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty,
                    null, shortcut, new object[] { exePath + ",0" });
                shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod,
                    null, shortcut, null);
            }
            catch
            {
                // A shortcut is a nicety — never block startup on it.
            }
        }
    }
}
