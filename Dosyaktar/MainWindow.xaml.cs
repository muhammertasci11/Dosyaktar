using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Dosyaktar.Services;

namespace Dosyaktar
{
    // ─── Dosya listesi için model ──────────────────────────────────────────────
    public sealed class FileEntry
    {
        public string Path     { get; }
        public string Name     { get; }
        public long   Size     { get; }
        public string SizeText { get; }

        public FileEntry(string path)
        {
            Path     = path;
            var fi   = new FileInfo(path);
            Name     = fi.Name;
            Size     = fi.Length;
            SizeText = FileTransferService.FormatSize(fi.Length);
        }
    }

    public partial class MainWindow : Window
    {
        // ── Servisler ─────────────────────────────────────────────────────────
        private readonly NetworkManager      _net       = new();
        private readonly FileTransferService _xfer      = new();
        private readonly NetworkDiscovery    _discovery = new();

        // ── Durum ─────────────────────────────────────────────────────────────
        private readonly ObservableCollection<FileEntry>    _files = new();
        private readonly ObservableCollection<DiscoveredPeer> _peers = new();
        private AppSettings     _settings = SettingsService.Load();
        private DiscoveredPeer? _selectedPeer;
        private bool _isBusy;
        private bool _initialized;
        private bool _closing;

        // ── Renk sabitleri ────────────────────────────────────────────────────
        private static readonly SolidColorBrush GreenBrush  = new(Color.FromRgb(0x10, 0xB9, 0x81));
        private static readonly SolidColorBrush RedBrush    = new(Color.FromRgb(0xEF, 0x44, 0x44));
        private static readonly SolidColorBrush OrangeBrush = new(Color.FromRgb(0xF5, 0x9E, 0x0B));
        private static readonly SolidColorBrush YellowBrush = new(Color.FromRgb(0xEA, 0xB3, 0x08));
        private static readonly SolidColorBrush BlueBrush   = new(Color.FromRgb(0x37, 0x7E, 0xF7));
        private static readonly SolidColorBrush GrayBrush   = new(Color.FromRgb(0x6B, 0x72, 0x80));

        // ─────────────────────────────────────────────────────────────────────

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                _initialized = true;

                FileListControl.ItemsSource  = _files;
                PeerList.ItemsSource = _peers;

                WireEvents();
                UpdateRoleUI();

                // Kendi bilgilerini göster
                string myIP = NetworkManager.GetLocalIP();
                TxtMyIP.Text      = myIP;
                TxtHostname.Text  = Environment.MachineName;

                // Son hedef IP'yi yükle
                if (!string.IsNullOrEmpty(_settings.LastTargetIP))
                    TxtTargetIP.Text = _settings.LastTargetIP;

                // Cihaz keşfini başlat
                int port = _settings.Port > 0 ? _settings.Port : FileTransferService.DefaultPort;
                _discovery.Start(port, isReceiving: false);

                // Tarama animasyonu
                StartScanAnimation();

                Loaded += async (_, _) => await CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                File.WriteAllText(
                    System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        "dosyaktar_crash.txt"), ex.ToString());
                MessageBox.Show(ex.Message);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Olay bağlantıları
        // ═════════════════════════════════════════════════════════════════════

        private void WireEvents()
        {
            _net.LogMessage += (_, msg) => AppendLog($"[Ağ] {msg}");
            _net.Error      += (_, msg) => AppendLog($"[Hata] {msg}", isError: true);

            _xfer.StatusChanged   += (_, msg) => AppendLog(msg);
            _xfer.Error           += (_, msg) => AppendLog(msg, isError: true);
            _xfer.ProgressChanged += (_, p)   => UpdateProgress(p);

            _discovery.PeersChanged += (_, peers) => Dispatcher.InvokeAsync(() => UpdatePeerList(peers));
            _discovery.LogMessage   += (_, msg)   => AppendLog($"[Keşif] {msg}");
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Cihaz Listesi Güncelleme
        // ═════════════════════════════════════════════════════════════════════

        private void UpdatePeerList(IReadOnlyList<DiscoveredPeer> peers)
        {
            _peers.Clear();
            foreach (var p in peers.OrderBy(p => p.Hostname))
                _peers.Add(p);

            bool hasPeers = _peers.Count > 0;
            NoPeersHint.Visibility = hasPeers ? Visibility.Collapsed : Visibility.Visible;

            // Badge
            PeerCountBadge.Visibility = hasPeers ? Visibility.Visible : Visibility.Collapsed;
            TxtPeerCount.Text = _peers.Count.ToString();

            // Seçili peer hala listede mi?
            if (_selectedPeer != null && !peers.Any(p => p.IP == _selectedPeer.IP))
                ClearSelectedPeer();
        }

        private void PeerCard_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is DiscoveredPeer peer)
                SelectPeer(peer);
        }

        private void SelectPeer(DiscoveredPeer peer)
        {
            _selectedPeer = peer;
            TxtTargetIP.Text = peer.IP;

            // Seçili kart göster, manuel giriş gizle
            SelectedPeerCard.Visibility = Visibility.Visible;
            ManualIPPanel.Visibility    = Visibility.Collapsed;
            TxtSelectedPeerName.Text    = peer.Hostname;
            TxtSelectedPeerIP.Text      = peer.IP;
            TxtConnStatus.Text          = peer.IsReceiving ? "✓ Almaya hazır" : "🟢 Çevrimiçi";
            TxtConnStatus.Foreground    = peer.IsReceiving ? GreenBrush : GrayBrush;

            BtnSend.IsEnabled = RbSender.IsChecked == true && _files.Count > 0 && !_isBusy;
            AppendLog($"Hedef seçildi: {peer.Hostname} ({peer.IP})");
        }

        private void BtnClearPeer_Click(object sender, RoutedEventArgs e) => ClearSelectedPeer();

        private void ClearSelectedPeer()
        {
            _selectedPeer = null;
            SelectedPeerCard.Visibility = Visibility.Collapsed;
            ManualIPPanel.Visibility    = Visibility.Visible;
            TxtConnStatus.Text          = "";
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Tarama Animasyonu (📡 sallanması)
        // ═════════════════════════════════════════════════════════════════════

        private void StartScanAnimation()
        {
            var anim = new DoubleAnimation
            {
                From           = 0.4,
                To             = 1.0,
                Duration       = TimeSpan.FromSeconds(1.2),
                AutoReverse    = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            TxtScanning.BeginAnimation(OpacityProperty, anim);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  IP Kopyala
        // ═════════════════════════════════════════════════════════════════════

        private void CopyIP_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Clipboard.SetText(TxtMyIP.Text);
                TxtNetStatus.Text       = "✓ IP panoya kopyalandı";
                TxtNetStatus.Foreground = GreenBrush;
            }
            catch { }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Güncelleme kontrolü
        // ═════════════════════════════════════════════════════════════════════

        private async Task CheckForUpdatesAsync()
        {
            if (!_settings.CheckUpdates) return;
            await Task.Delay(2000); // Pencere açıldıktan 2 sn sonra kontrol et

            var svc = new UpdateService();
            // Status mesajlarını log + durum barına aktar
            svc.StatusChanged += (_, msg) => Dispatcher.InvokeAsync(() =>
            {
                AppendLog($"[Güncelleme] {msg}");
                TxtNetStatus.Text       = msg;
                if (msg.StartsWith("⚠"))
                    TxtNetStatus.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "WarningBrush");
                else if (msg.StartsWith("✓"))
                    TxtNetStatus.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "SuccessBrush");
                else if (msg.StartsWith("🎉"))
                    TxtNetStatus.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "AccentBrush");
                else
                    TxtNetStatus.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextSecBrush");
            });

            try
            {
                // AutoUpdate=true → sessizce indir+uygula
                // AutoUpdate=false → pencere aç
                var info = await svc.CheckAndMaybeAutoUpdateAsync(silent: _settings.AutoUpdate);

                if (info != null) // Manuel mod: kullanıcıya sor
                {
                    await Dispatcher.InvokeAsync(() =>
                        new UpdateWindow(info) { Owner = this }.ShowDialog());
                }
            }
            catch { }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Rol seçimi
        // ═════════════════════════════════════════════════════════════════════

        private void Role_Checked(object sender, RoutedEventArgs e) => UpdateRoleUI();

        private void UpdateRoleUI()
        {
            if (!_initialized) return;
            bool isSender = RbSender.IsChecked == true;

            if (DropZoneBorder != null)
                DropZoneBorder.Visibility = isSender ? Visibility.Visible : Visibility.Collapsed;
            
            if (ReceiveModeUI != null)
                ReceiveModeUI.Visibility = isSender ? Visibility.Collapsed : Visibility.Visible;

            if (TxtTargetLabel != null)
                TxtTargetLabel.Text = isSender ? "Hedef Cihaz" : "Gönderici Cihaz (opsiyonel)";

            if (BtnSend != null)
            {
                BtnSend.Visibility = isSender ? Visibility.Visible : Visibility.Collapsed;
                BtnSend.IsEnabled = isSender && _files.Count > 0 && !_isBusy;
            }

            // Discovery'ye modunu bildir
            _discovery.SetReceivingMode(!isSender);
            
            if (TxtReceiveSavePath != null)
            {
                if (string.IsNullOrEmpty(_settings.SaveDirectory))
                {
                    _settings.SaveDirectory = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        "Dosyaktar Alınanlar");
                    SettingsService.Save(_settings);
                }
                TxtReceiveSavePath.Text = _settings.SaveDirectory;
            }

            if (TxtTransferFile != null)
                TxtTransferFile.Text = isSender ? "Bekleniyor..." : "Bağlantı bekleniyor...";
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Bağlantı Testi
        // ═════════════════════════════════════════════════════════════════════

        private async void BtnTestConn_Click(object sender, RoutedEventArgs e)
        {
            string ip = TxtTargetIP.Text.Trim();
            if (!NetworkManager.IsValidIP(ip))
            {
                TxtConnStatus.Text       = "⚠ Geçersiz IP";
                TxtConnStatus.Foreground = OrangeBrush;
                return;
            }

            BtnTestConn.IsEnabled    = false;
            TxtConnStatus.Text       = "Test ediliyor...";
            TxtConnStatus.Foreground = GrayBrush;

            var (pingOk, ms) = await _net.PingAsync(ip);
            if (pingOk)
            {
                TxtConnStatus.Text       = $"✓ Erişilebilir ({ms} ms)";
                TxtConnStatus.Foreground = GreenBrush;
            }
            else
            {
                bool tcpOk = await _net.TestTcpPortAsync(ip,
                    _settings.Port > 0 ? _settings.Port : FileTransferService.DefaultPort);
                if (tcpOk)
                {
                    TxtConnStatus.Text       = "✓ Port açık";
                    TxtConnStatus.Foreground = GreenBrush;
                }
                else
                {
                    TxtConnStatus.Text       = "✗ Erişilemiyor";
                    TxtConnStatus.Foreground = RedBrush;
                    MessageBox.Show(
                        $"'{ip}' adresine ulaşılamıyor.\n\n" +
                        "Kontrol listesi:\n" +
                        "• İki PC aynı Wi-Fi veya kablo ağında mı?\n" +
                        "• Alıcı PC'de Dosyaktar açık ve 'Almaya Başla' tıklandı mı?\n" +
                        "• Windows Güvenlik Duvarı 5001 ve 5002 portlarına izin veriyor mu?\n\n" +
                        "Güvenlik Duvarı için Hızlı Çözüm:\n" +
                        "Windows Güvenlik Duvarı → Gelişmiş Ayarlar\n" +
                        "Gelen Kurallar → Yeni Kural → Port → TCP → 5001, 5002",
                        "Bağlantı Testi", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            BtnTestConn.IsEnabled = true;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Dosya seçimi
        // ═════════════════════════════════════════════════════════════════════

        private void DropZone_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isBusy)
                BtnBrowse_Click(sender, e);
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Gönderilecek Dosyaları Seç", Multiselect = true };
            if (dlg.ShowDialog() == true) AddFiles(dlg.FileNames);
        }

        public void AddFiles(IEnumerable<string> paths)
        {
            foreach (var p in paths)
            {
                if (!File.Exists(p)) continue;
                if (_files.Any(f => f.Path.Equals(p, StringComparison.OrdinalIgnoreCase))) continue;
                _files.Add(new FileEntry(p));
            }
            RefreshFileUI();
        }

        private void BtnClearFiles_Click(object sender, RoutedEventArgs e)
        {
            _files.Clear();
            RefreshFileUI();
        }

        private void BtnRemoveFile_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is FileEntry entry)
            {
                _files.Remove(entry);
                RefreshFileUI();
            }
        }

        private void RefreshFileUI()
        {
            bool hasFiles = _files.Count > 0;
            DropHint.Visibility      = hasFiles ? Visibility.Collapsed : Visibility.Visible;
            FileListPanel.Visibility = hasFiles ? Visibility.Visible   : Visibility.Collapsed;

            if (hasFiles)
            {
                long total = _files.Sum(f => f.Size);
                TxtFileCount.Text = $"{_files.Count} dosya — {FileTransferService.FormatSize(total)}";
            }

            BtnSend.IsEnabled = RbSender.IsChecked == true && hasFiles && !_isBusy;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Sürükle & Bırak
        // ═════════════════════════════════════════════════════════════════════

        private void DropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                DropZone.BorderBrush = BlueBrush;
                DropZone.Background  = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xFF));
                e.Effects            = DragDropEffects.Copy;
            }
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            DropZone.BorderBrush = (SolidColorBrush)FindResource("BorderBrush");
            DropZone.Background  = (SolidColorBrush)FindResource("SurfaceBrush");
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            DropZone.BorderBrush = (SolidColorBrush)FindResource("BorderBrush");
            DropZone.Background  = (SolidColorBrush)FindResource("SurfaceBrush");
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                AddFiles(files);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Transfer — GÖNDER
        // ═════════════════════════════════════════════════════════════════════

        private async void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            if (_files.Count == 0)
            {
                MessageBox.Show("Önce gönderilecek dosyaları seçin.", "Dosya Seçilmedi",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string targetIP = _selectedPeer?.IP ?? TxtTargetIP.Text.Trim();
            if (!NetworkManager.IsValidIP(targetIP))
            {
                MessageBox.Show(
                    "Hedef cihaz seçilmedi.\n\n" +
                    "Sol listeden bir cihaz seçin veya manuel IP girin.\n" +
                    "Alıcı PC'de Dosyaktar'ın açık olduğundan emin olun.",
                    "Hedef Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _settings.LastTargetIP = targetIP;
            SettingsService.Save(_settings);

            SetBusy(true);
            int port = _settings.Port > 0 ? _settings.Port : FileTransferService.DefaultPort;
            AppendLog($"Bağlanılıyor: {targetIP}:{port}");

            var paths  = _files.Select(f => f.Path).ToArray();
            var result = await _xfer.SendFilesAsync(paths, targetIP, port);
            SetBusy(false);

            if (result.Success)
            {
                SetProgressDone();
                MessageBox.Show($"✓ Transfer tamamlandı!\n\n{result.Message}",
                    "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (!result.Message.Contains("iptal", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    $"Transfer başarısız:\n{result.Message}\n\n" +
                    "İpuçları:\n" +
                    "• Alıcı PC'de 'Almaya Başla' tıklandı mı?\n" +
                    "• Aynı ağda mısınız?\n" +
                    "• Güvenlik duvarı 5001 portunu engelliyor olabilir",
                    "Transfer Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Transfer — AL
        // ═════════════════════════════════════════════════════════════════════

        private void BtnChangeReceivePath_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Alınan Dosyalar Klasörü" };
            if (dlg.ShowDialog() == true)
            {
                _settings.SaveDirectory = dlg.FolderName;
                SettingsService.Save(_settings);
                if (TxtReceiveSavePath != null)
                    TxtReceiveSavePath.Text = dlg.FolderName;
                AppendLog($"Kayıt dizini değiştirildi: {dlg.FolderName}");
            }
        }

        private async void BtnReceive_Click(object sender, RoutedEventArgs e)
        {
            SetBusy(true);

            if (BtnCancelReceive != null) BtnCancelReceive.Visibility = Visibility.Visible;
            if (BtnReceive != null) BtnReceive.Visibility = Visibility.Collapsed;
            if (RbSender != null) RbSender.IsEnabled = false;

            string saveDir = _settings.SaveDirectory;
            if (string.IsNullOrEmpty(saveDir) || !Directory.Exists(saveDir))
            {
                saveDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "Dosyaktar Alınanlar");
                _settings.SaveDirectory = saveDir;
                SettingsService.Save(_settings);
            }

            int port = _settings.Port > 0 ? _settings.Port : FileTransferService.DefaultPort;

            SetStatus($"Bekleniyor... ({port})", GreenBrush);
            TxtTransferFile.Text = $"Bağlantı bekleniyor — Port {port}";
            AppendLog($"Alınan dosyalar → {saveDir}");

            _discovery.SetReceivingMode(true);
            var result = await _xfer.StartReceivingAsync(saveDir, port);
            _discovery.SetReceivingMode(false);

            SetBusy(false);

            if (BtnCancelReceive != null) BtnCancelReceive.Visibility = Visibility.Collapsed;
            if (BtnReceive != null) BtnReceive.Visibility = Visibility.Visible;
            if (RbSender != null) RbSender.IsEnabled = true;

            if (result.Success)
            {
                SetProgressDone();
                MessageBox.Show($"✓ Dosya(lar) alındı!\n\nKaydedildi: {saveDir}",
                    "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (!result.Message.Contains("İptal", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show($"Alma başarısız:\n{result.Message}",
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else 
            {
                SetStatus("İptal Edildi", YellowBrush);
                TxtTransferFile.Text = "Bekleme iptal edildi.";
            }
        }

        private void BtnCancelReceive_Click(object sender, RoutedEventArgs e)
        {
            _xfer.Cancel();
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Ayarlar
        // ═════════════════════════════════════════════════════════════════════

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsWindow(_settings) { Owner = this };
            dlg.ShowDialog();

            _settings = SettingsService.Load();
            AppendLog("Ayarlar güncellendi.");

            // Discovery'yi yeni portla yeniden başlat
            int port = _settings.Port > 0 ? _settings.Port : FileTransferService.DefaultPort;
            _discovery.Start(port, isReceiving: RbReceiver.IsChecked == true);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Oyun Taşıma
        // ═════════════════════════════════════════════════════════════════════

        private void BtnGameTransfer_Click(object sender, RoutedEventArgs e)
        {
            string targetIP = _selectedPeer?.IP ?? TxtTargetIP.Text.Trim();
            if (!NetworkManager.IsValidIP(targetIP))
            {
                MessageBox.Show(
                    "Oyun taşıma başlatmadan önce sol listeden bir cihaz seçin.",
                    "Hedef Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int port = _settings.Port > 0 ? _settings.Port : FileTransferService.DefaultPort;
            var dlg  = new GameTransferWindow(_xfer, targetIP, port) { Owner = this };
            dlg.ShowDialog();
        }

        // ═════════════════════════════════════════════════════════════════════
        //  İptal
        // ═════════════════════════════════════════════════════════════════════

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _xfer.Cancel();
            AppendLog("Transfer iptal isteği gönderildi.");
        }

        // ═════════════════════════════════════════════════════════════════════
        //  İlerleme
        // ═════════════════════════════════════════════════════════════════════

        private void UpdateProgress(TransferProgress p)
        {
            Dispatcher.InvokeAsync(() =>
            {
                PbTransfer.Value     = p.Percentage;
                TxtPercent.Text      = $"{p.Percentage:F1}%";
                TxtTransferFile.Text = p.TotalFiles > 1
                    ? $"[{p.CurrentFileIndex}/{p.TotalFiles}] {p.FileName}"
                    : p.FileName;
                TxtSpeed.Text        = p.SpeedMBps > 0 ? $"{p.SpeedMBps:F1} MB/s" : "Hesaplanıyor...";
                TxtRemaining.Text    = p.RemainingTime == TimeSpan.MaxValue
                    ? "—"
                    : p.RemainingTime.TotalSeconds < 60
                        ? $"{p.RemainingTime.TotalSeconds:F0} sn"
                        : $"{p.RemainingTime.Minutes}d {p.RemainingTime.Seconds}sn";
            });
        }

        private void SetProgressDone()
        {
            PbTransfer.Value  = 100;
            TxtPercent.Text   = "100%";
            TxtSpeed.Text     = "Tamamlandı";
            TxtRemaining.Text = "—";
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Yardımcı UI
        // ═════════════════════════════════════════════════════════════════════

        private void SetBusy(bool busy)
        {
            _isBusy              = busy;
            bool isSender        = RbSender.IsChecked == true;
            BtnSend.IsEnabled    = !busy && _files.Count > 0 && isSender;
            BtnReceive.IsEnabled = !busy && !isSender;
            BtnCancel.IsEnabled  = busy;
            RbSender.IsEnabled   = !busy;
            RbReceiver.IsEnabled = !busy;

            SetStatus(busy ? "Transfer devam ediyor..." : "Hazır",
                      busy ? OrangeBrush : GreenBrush);
        }

        private void SetStatus(string text, SolidColorBrush color)
        {
            Dispatcher.InvokeAsync(() =>
            {
                TxtStatus.Text       = text;
                TxtStatus.Foreground = color;
                StatusDot.Fill       = color;
                StatusPill.Background = new SolidColorBrush(
                    Color.FromArgb(30, color.Color.R, color.Color.G, color.Color.B));
            });
        }

        private void AppendLog(string message, bool isError = false)
        {
            Dispatcher.InvokeAsync(() =>
            {
                string time = DateTime.Now.ToString("HH:mm:ss");
                TxtLog.Text += $"[{time}] {(isError ? "✗" : "▸")} {message}\n";
                LogScroll.ScrollToEnd();
            });
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Pencere kapanışı
        // ═════════════════════════════════════════════════════════════════════

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_closing) return;
            _closing = true;
            _discovery.Dispose();
            _xfer.Cancel();
            _net.Dispose();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) 
        { 
            _peers.Clear(); 
            _discovery.Stop(); 
            int port = _settings.Port > 0 ? _settings.Port : FileTransferService.DefaultPort; 
            _discovery.Start(port, isReceiving: false); 
        }

        private void BtnSendToPeer_Click(object sender, RoutedEventArgs e) 
        { 
            if ((sender as System.Windows.Controls.Button)?.Tag is DiscoveredPeer peer) 
            { 
                _selectedPeer = peer; 
                BtnSend_Click(null, null); 
            } 
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
                this.WindowState = WindowState.Normal;
            else
                this.WindowState = WindowState.Maximized;
        }

        private void BtnCloseWindow_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void BtnNav_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelDiscovery == null) return; // Henüz initialize olmadıysa

            PanelDiscovery.Visibility = Visibility.Collapsed;
            PanelActive.Visibility    = Visibility.Collapsed;
            PanelHistory.Visibility   = Visibility.Collapsed;

            System.Windows.Controls.Grid activePanel = null;

            if (BtnNavDiscovery.IsChecked == true)
                activePanel = PanelDiscovery;
            else if (BtnNavActive.IsChecked == true)
                activePanel = PanelActive;
            else if (BtnNavHistory.IsChecked == true)
                activePanel = PanelHistory;

            if (activePanel != null)
            {
                activePanel.Visibility = Visibility.Visible;
                
                // Fade-in animasyonu ekle
                System.Windows.Media.Animation.DoubleAnimation fadeIn = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                activePanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }
        }
    }
}