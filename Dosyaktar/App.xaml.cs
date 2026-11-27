using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Dosyaktar.Localization;
using Dosyaktar.Services;

namespace Dosyaktar
{
    public partial class App : Application
    {
        /// <summary>True for CLI-driven runs (screenshots): nothing is written to settings.</summary>
        public static bool Ephemeral { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            RegisterCrashHandlers();
            EnsureDesktopShortcut();

            var settings = SettingsService.Load();

            // Theme first — every window resolves DynamicResource against it.
            ThemeManager.Apply(string.IsNullOrWhiteSpace(settings.Theme) ? "prism" : settings.Theme);

            // --lang=xx overrides the saved language for this session (used by the
            // screenshot pipeline and by people who keep several shortcuts).
            string? langArg = null;
            string? pageArg = null;
            bool offscreen = false;
            foreach (var arg in e.Args)
            {
                if (arg.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase))
                    langArg = arg.Substring("--lang=".Length);
                else if (arg.StartsWith("--page=", StringComparison.OrdinalIgnoreCase))
                    pageArg = arg.Substring("--page=".Length);
                else if (arg.Equals("--offscreen", StringComparison.OrdinalIgnoreCase))
                    offscreen = true;
            }

            if (!string.IsNullOrWhiteSpace(pageArg)) settings.LastPage = pageArg;

            // Screenshot runs (--lang / --page / --offscreen) must not overwrite the
            // user's saved session state.
            Ephemeral = offscreen || langArg != null || pageArg != null;

            Loc.SetLanguage(langArg ?? settings.Language);

            var window = new MainWindow(settings);

            if (offscreen)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -32000;
                window.Top = -32000;
            }

            MainWindow = window;
            window.Show();
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
