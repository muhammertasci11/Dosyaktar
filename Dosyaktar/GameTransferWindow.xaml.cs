using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Dosyaktar.Helpers;
using Dosyaktar.Services;
using Microsoft.Win32;

namespace Dosyaktar
{
    public partial class GameTransferWindow : Window
    {
        private readonly FileTransferService _xfer;
        private readonly string _targetIP;
        private readonly int    _port;

        // Seçili gönderim yolu (herhangi bir modda)
        private string? _selectedFolderPath;
        private string? _manifestPath;        // Steam için ek manifest
        private bool    _isBusy;

        private static readonly SolidColorBrush GreenBrush  = new(Color.FromRgb(0x34, 0xD3, 0x99));
        private static readonly SolidColorBrush RedBrush    = new(Color.FromRgb(0xF8, 0x71, 0x71));
        private static readonly SolidColorBrush OrangeBrush = new(Color.FromRgb(0xFB, 0x92, 0x3C));

        public GameTransferWindow(FileTransferService xfer, string targetIP, int port)
        {
            _xfer     = xfer;
            _targetIP = targetIP;
            _port     = port;
            InitializeComponent();

            _xfer.ProgressChanged += OnProgress;
            _xfer.StatusChanged   += OnStatus;
            _xfer.Error           += OnError;

            // Başlangıçta Steam listesini yükle
            Loaded += async (_, _) => await LoadGamesAsync();
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Platform Sekmeleri
        // ═════════════════════════════════════════════════════════════════════

        private async void Platform_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            ResetSelection();
            PanelGameList.Visibility = GetGameListVisible();
            PanelFolder.Visibility   = TabFolder.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelExe.Visibility      = TabExe.IsChecked   == true ? Visibility.Visible : Visibility.Collapsed;

            if (TabSteam.IsChecked == true || TabEpic.IsChecked == true)
                await LoadGamesAsync();
        }

        private Visibility GetGameListVisible()
            => (TabSteam.IsChecked == true || TabEpic.IsChecked == true)
               ? Visibility.Visible : Visibility.Collapsed;

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
            => await LoadGamesAsync();

        // ═════════════════════════════════════════════════════════════════════
        //  Oyun Listesi Yükleme
        // ═════════════════════════════════════════════════════════════════════

        private async Task LoadGamesAsync()
        {
            TxtGameStatus.Text = "Taranıyor...";
            GameListView.ItemsSource = null;
            List<InstalledGame> games = new();

            bool isSteam = TabSteam.IsChecked == true;

            await Task.Run(() =>
            {
                games = isSteam
                    ? PlatformScanner.ScanSteam()
                    : PlatformScanner.ScanEpic();
            });

            GameListView.ItemsSource = games;
            string platform = isSteam ? "Steam" : "Epic";
            TxtGameStatus.Text = games.Count > 0
                ? $"{games.Count} {platform} oyunu bulundu"
                : $"Hiç {platform} oyunu bulunamadı";
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Oyun Seçimi
        // ═════════════════════════════════════════════════════════════════════

        private void GameListView_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (GameListView.SelectedItem is not InstalledGame game)
            {
                SelectedGameInfo.Visibility = Visibility.Collapsed;
                BtnSend.IsEnabled = false;
                return;
            }

            _selectedFolderPath = game.InstallPath;
            _manifestPath       = game.ManifestPath; // Steam için

            TxtSelectedGame.Text = game.Name;
            TxtSelectedInfo.Text = game.Platform == "Steam"
                ? $"Steam · {game.InstallPath}"
                : $"Epic Games · {game.InstallPath}";
            TxtSelectedSize.Text = game.SizeText;

            SelectedGameInfo.Visibility = Visibility.Visible;
            BtnSend.IsEnabled = !_isBusy;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Klasör Seçimi
        // ═════════════════════════════════════════════════════════════════════

        private void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Göndermek istediğin klasörü seç",
                Multiselect = false
            };
            
            if (dialog.ShowDialog(this) == true)
            {
                string picked = dialog.FolderName;
                _selectedFolderPath = picked;
                _manifestPath       = null;
                long size = PlatformScanner.DirectorySize(picked);
                TxtSelectedFolder.Text = $"✓ {System.IO.Path.GetFileName(picked)} — {FileTransferService.FormatSize(size)}";
                BtnSend.IsEnabled = !_isBusy;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Exe Seçimi
        // ═════════════════════════════════════════════════════════════════════

        private void BtnSelectExe_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Uygulamanın exe dosyasını seç",
                Filter = "Uygulamalar|*.exe"
            };
            if (dlg.ShowDialog() != true) return;

            string exePath = dlg.FileName;

            // Windows Store (UWP) klasörü kontrolü
            if (IsUwpApp(exePath))
            {
                UwpWarning.Visibility = Visibility.Visible;
                BtnSend.IsEnabled     = false;
                TxtSelectedExe.Text   = "⚠ Bu uygulama aktarılamaz (Windows Store)";
                return;
            }

            UwpWarning.Visibility   = Visibility.Collapsed;
            _selectedFolderPath     = Path.GetDirectoryName(exePath)!;
            _manifestPath           = null;
            long size = PlatformScanner.DirectorySize(_selectedFolderPath);
            TxtSelectedExe.Text = $"✓ {Path.GetFileName(exePath)} ({Path.GetDirectoryName(exePath)}) — {FileTransferService.FormatSize(size)}";
            BtnSend.IsEnabled   = !_isBusy;
        }

        private static bool IsUwpApp(string exePath)
        {
            string? dir = Path.GetDirectoryName(exePath)?.ToLowerInvariant();
            return dir != null && (
                dir.Contains("windowsapps") ||
                dir.Contains("program files\\windowsapps"));
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Gönder
        // ═════════════════════════════════════════════════════════════════════

        private async void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFolderPath == null) return;

            // Epic için bilgilendirme
            bool isEpic = TabEpic.IsChecked == true;
            if (isEpic && GameListView.SelectedItem is InstalledGame epicGame)
            {
                var res = MessageBox.Show(
                    $"Epic oyunu gönderilecek: {epicGame.Name}\n\n" +
                    "Alıcı tarafta:\n" +
                    "1. Epic Games Launcher'ı kapat\n" +
                    "2. Dosyaktar ile oyun klasörünü al\n" +
                    "3. Launcher'ı aç → Library → Manage İnstallations\n" +
                    "4. Oyunu tıkla → 'Verify'\n\n" +
                    "Devam etmek istiyor musun?",
                    "Epic Games — Talimat", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (res == MessageBoxResult.No) return;
            }

            SetBusy(true);
            ProgressPanel.Visibility = Visibility.Visible;

            // Steam için manifest dosyasını da gönder
            if (_manifestPath != null && File.Exists(_manifestPath))
            {
                TxtStatus.Text = "Steam manifest + oyun klasörü gönderiliyor...";
                // Önce klasörü gönder
                var r1 = await _xfer.SendFolderAsync(_selectedFolderPath, _targetIP, _port);
                if (!r1.Success && !r1.Message.Contains("iptal")) { ShowError(r1.Message); SetBusy(false); return; }

                // Sonra manifest
                var r2 = await _xfer.SendFilesAsync(new[] { _manifestPath }, _targetIP, _port);
                if (!r2.Success && !r2.Message.Contains("iptal")) { ShowError(r2.Message); SetBusy(false); return; }

                if (r1.Success && r2.Success)
                    ShowSuccess("Oyun + Steam manifest gönderildi!\n\nAlıcı Steam kütüphanesini yenilediğinde oyun hazır görünecek.");
            }
            else
            {
                // Klasör veya exe modu
                var result = await _xfer.SendFolderAsync(_selectedFolderPath, _targetIP, _port);
                if (result.Success)
                    ShowSuccess($"Gönderim tamamlandı!\n\n{result.Message}");
                else if (!result.Message.Contains("iptal"))
                    ShowError(result.Message);
            }

            SetBusy(false);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  İlerleme
        // ═════════════════════════════════════════════════════════════════════

        private void OnProgress(object? sender, TransferProgress p)
        {
            Dispatcher.InvokeAsync(() =>
            {
                PbTransfer.Value  = p.Percentage;
                TxtPct.Text       = $"{p.Percentage:F1}%";
                TxtTransfer.Text  = p.TotalFiles > 1
                    ? $"[{p.CurrentFileIndex}/{p.TotalFiles}] {p.FileName}"
                    : p.FileName;
                TxtSpeed.Text     = p.SpeedMBps > 0 ? $"⚡ {p.SpeedMBps:F1} MB/s" : "Hesaplanıyor...";
                TxtRemaining.Text = p.RemainingTime == TimeSpan.MaxValue ? "—"
                    : p.RemainingTime.TotalSeconds < 60
                        ? $"⏱ {p.RemainingTime.TotalSeconds:F0} sn kaldı"
                        : $"⏱ {p.RemainingTime.Minutes}d {p.RemainingTime.Seconds}sn kaldı";
            });
        }

        private void OnStatus(object? sender, string msg)
            => Dispatcher.InvokeAsync(() => TxtStatus.Text = msg);

        private void OnError(object? sender, string msg)
            => Dispatcher.InvokeAsync(() => TxtStatus.Text = $"✗ {msg}");

        // ═════════════════════════════════════════════════════════════════════
        //  Yardımcılar
        // ═════════════════════════════════════════════════════════════════════

        private void ResetSelection()
        {
            _selectedFolderPath = null;
            _manifestPath       = null;
            BtnSend.IsEnabled   = false;
            SelectedGameInfo.Visibility = Visibility.Collapsed;
            TxtSelectedFolder.Text = "";
            TxtSelectedExe.Text    = "";
            UwpWarning.Visibility  = Visibility.Collapsed;
        }

        private void SetBusy(bool busy)
        {
            _isBusy           = busy;
            BtnSend.IsEnabled = !busy && _selectedFolderPath != null;
            BtnCancel.IsEnabled = busy;
        }

        private void ShowSuccess(string msg)
        {
            PbTransfer.Value = 100;
            TxtPct.Text = "100%";
            MessageBox.Show(msg, "✓ Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowError(string msg)
            => MessageBox.Show($"Hata:\n{msg}", "Transfer Hatası", MessageBoxButton.OK, MessageBoxImage.Error);

        private void BtnCancelTransfer_Click(object sender, RoutedEventArgs e) => _xfer.Cancel();

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _xfer.ProgressChanged -= OnProgress;
            _xfer.StatusChanged   -= OnStatus;
            _xfer.Error           -= OnError;
            Close();
        }
    }
}
