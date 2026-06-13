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
using System.Windows.Controls;
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
        private readonly PairingService      _pairing   = new();

        // ── Durum ─────────────────────────────────────────────────────────────
        private readonly ObservableCollection<FileEntry>    _files = new();
        private readonly ObservableCollection<DiscoveredPeer> _peers = new();
        private AppSettings     _settings = SettingsService.Load();
        private DiscoveredPeer? _selectedPeer;
        private bool _isBusy;
        private bool _initialized;
        private bool _closing;
        
        // ── Oturum (Pairing) Durumu ───────────────────────────────────────────
        private bool _isConnectedSession;
        private string _connectedTargetIP = "";
        private string _connectedTargetName = "";
        private string _currentPairingPin = "";

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

                // Eşleştirme servisini başlat
                _pairing.PairingRequested += Pairing_Requested;
                _pairing.PairingResponseReceived += Pairing_ResponseReceived;
                _pairing.StartListening();

                // Tarama animasyonu
                StartScanAnimation();
                
                // Ayarları UI'a yükle ve ağ bilgisini güncelle
                LoadSettingsToUI();
                UpdateNetworkInfo();
                ApplyTheme(_settings.Theme);
                
                // Dinamik Sürüm Bilgisi
                var ver = UpdateService.GetCurrentVersion();
                TxtAppVersion.Text = $"Dosyaktar v{ver.Major}.{ver.Minor}.{ver.Build}";

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
            if (!_settings.AutoUpdate) return;
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
                // Kullanıcı her zaman güncelleme panelini görsün
                var info = await svc.CheckAndMaybeAutoUpdateAsync(silent: false);

                if (info != null) // Manuel mod: kullanıcıya sor
                {
                    await Dispatcher.InvokeAsync(() =>
                        new UpdateWindow(info) { Owner = this }.ShowDialog());
                }
            }
            catch { }
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn) btn.IsEnabled = false;
            try
            {
                var svc = new UpdateService();
                svc.StatusChanged += (_, msg) => Dispatcher.InvokeAsync(() => AppendLog($"[Manuel Güncelleme] {msg}"));
                
                var info = await svc.CheckAndMaybeAutoUpdateAsync(silent: false);
                if (info != null)
                {
                    await Dispatcher.InvokeAsync(() => new UpdateWindow(info) { Owner = this }.ShowDialog());
                }
                else
                {
                    MessageBox.Show("Sisteminiz güncel veya yeni bir sürüm bulunamadı (GitHub önbelleği nedeniyle 5 dakika sürebilir).", "Güncelleme Kontrolü", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (sender is Button b) b.IsEnabled = true;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Rol seçimi
        // ═════════════════════════════════════════════════════════════════════

        private void Role_Checked(object sender, RoutedEventArgs e) => UpdateRoleUI();

        private void UpdateRoleUI()
        {
            // Eski Gönder/Al mantığı ShareIt Session moduyla iptal edildi.
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
            if (dlg.ShowDialog() == true) AddFilesAndSend(dlg.FileNames);
        }

        public async void AddFilesAndSend(IEnumerable<string> paths)
        {
            var newFiles = new List<string>();
            foreach (var p in paths)
            {
                if (File.Exists(p)) newFiles.Add(p);
            }
            if (newFiles.Count == 0) return;

            if (!_isConnectedSession || string.IsNullOrEmpty(_connectedTargetIP))
            {
                MessageBox.Show("Lütfen önce bir cihaza bağlanın.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetBusy(true);
            int port = _settings.Port > 0 ? _settings.Port : FileTransferService.DefaultPort;
            AppendLog($"Dosyalar gönderiliyor: {_connectedTargetIP}:{port}");

            // UI'da aktarım kartını göster
            if (FindName("TransferCard") is Border card) card.Visibility = Visibility.Visible;
            if (FindName("TxtTransferFile") is TextBlock tf) tf.Text = System.IO.Path.GetFileName(newFiles[0]) + (newFiles.Count > 1 ? $" ve {newFiles.Count - 1} dosya daha" : "");
            if (FindName("TxtTargetDevice") is TextBlock td) td.Text = _connectedTargetName;
            if (FindName("TxtActiveCount") is TextBlock ac) ac.Text = "1 Aktarım";

            var result = await _xfer.SendFilesAsync(newFiles.ToArray(), _connectedTargetIP, port);
            SetBusy(false);

            if (FindName("TransferCard") is Border card2) card2.Visibility = Visibility.Collapsed;
            if (FindName("TxtActiveCount") is TextBlock ac2) ac2.Text = "Aktif işlem yok";

            if (result.Success)
            {
                SetProgressDone();
                AppendLog("Transfer başarıyla tamamlandı.");
                AddToHistory($"Gönderildi: {newFiles.Count} dosya → {_connectedTargetName}");
            }
            else if (!result.Message.Contains("iptal", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show($"Transfer başarısız:\n{result.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                AddToHistory($"Hata: {newFiles.Count} dosya gönderilemedi → {_connectedTargetName}");
            }
        }

        private void AddToHistory(string info)
        {
            Dispatcher.InvokeAsync(() => {
                if (FindName("TxtHistoryEmpty") is TextBlock emptyTxt) emptyTxt.Visibility = Visibility.Collapsed;
                if (FindName("HistoryList") is StackPanel hl)
                {
                    var border = new Border {
                        Background = (SolidColorBrush)FindResource("Surface2Brush"),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(16, 12, 16, 12),
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    border.Child = new TextBlock {
                        Text = $"{DateTime.Now:HH:mm} - {info}",
                        Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush"),
                        FontSize = 13
                    };
                    hl.Children.Add(border);
                }
            });
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Sürükle & Bırak
        // ═════════════════════════════════════════════════════════════════════

        private void DropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (sender is Border dropZone)
                {
                    dropZone.BorderBrush = BlueBrush;
                    dropZone.Background  = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xFF));
                }
                e.Effects = DragDropEffects.Copy;
            }
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border dropZone)
            {
                dropZone.BorderBrush = (SolidColorBrush)FindResource("BorderBrush");
                dropZone.Background  = (SolidColorBrush)FindResource("BackgroundBrush");
            }
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            if (sender is Border dropZone)
            {
                dropZone.BorderBrush = (SolidColorBrush)FindResource("BorderBrush");
                dropZone.Background  = (SolidColorBrush)FindResource("BackgroundBrush");
            }
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                AddFilesAndSend(files);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Transfer — GÖNDER
        // ═════════════════════════════════════════════════════════════════════

        // ═════════════════════════════════════════════════════════════════════
        //  Eşleştirme ve Oturum (Session) Modu
        // ═════════════════════════════════════════════════════════════════════

        private void Pairing_Requested(object? sender, (string IP, string Hostname, string Pin) e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _connectedTargetIP = e.IP;
                _connectedTargetName = e.Hostname;
                
                PairingModal.Visibility = Visibility.Visible;
                if (FindName("TxtPairingTitle") is System.Windows.Controls.TextBlock title) title.Text = "Bağlantı İsteği";
                if (FindName("TxtPairingMessage") is System.Windows.Controls.TextBlock msg) msg.Text = $"{e.Hostname} cihazı size bağlanmak istiyor.\nKodları karşılaştırın:";
                if (FindName("TxtPairingPin") is System.Windows.Controls.TextBlock pin) pin.Text = e.Pin;
                
                if (FindName("PairingReceiveButtons") is Grid recvBtns) recvBtns.Visibility = Visibility.Visible;
                if (FindName("PairingSendButtons") is Grid sendBtns) sendBtns.Visibility = Visibility.Collapsed;
            });
        }

        private void Pairing_ResponseReceived(object? sender, (string IP, bool Accepted) e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                PairingModal.Visibility = Visibility.Collapsed;
                if (e.Accepted)
                {
                    AppendLog("Bağlantı isteği KABUL EDİLDİ.");
                    _connectedTargetIP = e.IP;
                    _connectedTargetName = _selectedPeer?.Hostname ?? e.IP;
                    StartSession();
                }
                else
                {
                    AppendLog("Bağlantı isteği REDDEDİLDİ.");
                    MessageBox.Show("Karşı cihaz bağlantı isteğini reddetti.", "Reddedildi", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            });
        }

        private async void BtnPairingAccept_Click(object sender, RoutedEventArgs e)
        {
            PairingModal.Visibility = Visibility.Collapsed;
            AppendLog($"{_connectedTargetName} eşleşmesi kabul edildi.");
            await _pairing.SendResponseAsync(_connectedTargetIP, true);
            StartSession();
        }

        private async void BtnPairingReject_Click(object sender, RoutedEventArgs e)
        {
            PairingModal.Visibility = Visibility.Collapsed;
            AppendLog($"{_connectedTargetName} eşleşmesi reddedildi.");
            await _pairing.SendResponseAsync(_connectedTargetIP, false);
            _connectedTargetIP = "";
            _connectedTargetName = "";
        }

        private void BtnPairingCancel_Click(object sender, RoutedEventArgs e)
        {
            PairingModal.Visibility = Visibility.Collapsed;
            AppendLog("Eşleşme isteği iptal edildi.");
        }

        private void StartSession()
        {
            _isConnectedSession = true;
            if (FindName("PanelDiscovery") is Grid pd) pd.Visibility = Visibility.Collapsed;
            if (FindName("PanelHistory") is Grid ph) ph.Visibility = Visibility.Collapsed;
            if (FindName("PanelActive") is Grid pa) pa.Visibility = Visibility.Visible;
            
            if (FindName("BtnNavActive") is System.Windows.Controls.RadioButton rba) rba.IsChecked = true;
            
            if (FindName("TxtActiveTargetName") is System.Windows.Controls.TextBlock tn) tn.Text = _connectedTargetName;
            if (FindName("TxtActiveTargetIP") is System.Windows.Controls.TextBlock tip) tip.Text = _connectedTargetIP;
            
            // Arka planda sürekli alıcı modunu dinle
            _ = Task.Run(ReceiveLoopAsync);
        }

        private async Task ReceiveLoopAsync()
        {
            while (_isConnectedSession)
            {
                string saveDir = _settings.SaveDirectory;
                if (string.IsNullOrEmpty(saveDir) || !Directory.Exists(saveDir))
                {
                    saveDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Dosyaktar Alınanlar");
                }
                
                int port = _settings.Port > 0 ? _settings.Port : FileTransferService.DefaultPort;
                
                try
                {
                    // StartReceivingAsync is blocking until a transfer completes
                    var result = await _xfer.StartReceivingAsync(saveDir, port);
                    if (result.Success)
                    {
                        Dispatcher.Invoke(() => SetProgressDone());
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal during disconnect
                }
                catch (Exception ex)
                {
                    AppendLog($"Oturum dinleme hatası: {ex.Message}");
                    await Task.Delay(1000); // Prevent tight loop crash
                }
            }
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _isConnectedSession = false;
            _xfer.Cancel(); // stop receiving loop
            
            _connectedTargetIP = "";
            _connectedTargetName = "";
            
            if (FindName("PanelActive") is Grid pa) pa.Visibility = Visibility.Collapsed;
            if (FindName("PanelDiscovery") is Grid pd) pd.Visibility = Visibility.Visible;
            if (FindName("BtnNavDiscovery") is System.Windows.Controls.RadioButton rbd) rbd.IsChecked = true;
            
            AppendLog("Oturum sonlandırıldı.");
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
            if (!_isConnectedSession || string.IsNullOrEmpty(_connectedTargetIP))
            {
                MessageBox.Show("Oyun taşıma başlatmadan önce sol listeden bir cihaza bağlanın.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int port = _settings.Port > 0 ? _settings.Port : FileTransferService.DefaultPort;
            var dlg  = new GameTransferWindow(_xfer, _connectedTargetIP, port) { Owner = this };
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
                if (TransferProgressBar != null) TransferProgressBar.Value = p.Percentage;
                if (TxtQueueProgress != null) TxtQueueProgress.Text = $"{p.Percentage:F1}%";
                if (TxtQueueFileName != null) TxtQueueFileName.Text = p.TotalFiles > 1
                    ? $"[{p.CurrentFileIndex}/{p.TotalFiles}] {p.FileName}"
                    : p.FileName;
                if (TxtQueueSpeed != null) TxtQueueSpeed.Text = p.SpeedMBps > 0 ? $"{p.SpeedMBps:F1} MB/s" : "Hesaplanıyor...";
                if (TxtQueueFileSize != null) TxtQueueFileSize.Text = p.RemainingTime == TimeSpan.MaxValue
                    ? "—"
                    : p.RemainingTime.TotalSeconds < 60
                        ? $"{p.RemainingTime.TotalSeconds:F0} sn"
                        : $"{p.RemainingTime.Minutes}d {p.RemainingTime.Seconds}sn";
            });
        }

        private void SetProgressDone()
        {
            if (TransferProgressBar != null) TransferProgressBar.Value = 100;
            if (TxtQueueProgress != null) TxtQueueProgress.Text = "100%";
            if (TxtQueueSpeed != null) TxtQueueSpeed.Text = "Tamamlandı";
            if (TxtQueueFileSize != null) TxtQueueFileSize.Text = "—";
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Yardımcı UI
        // ═════════════════════════════════════════════════════════════════════

        private void SetBusy(bool busy)
        {
            _isBusy              = busy;
            if (FindName("BtnCancel") is System.Windows.Controls.Button btnCancel) btnCancel.IsEnabled  = busy;

            SetStatus(busy ? "Transfer devam ediyor..." : "Hazır",
                      busy ? OrangeBrush : GreenBrush);
        }

        private void SetStatus(string text, SolidColorBrush color)
        {
            Dispatcher.InvokeAsync(() =>
            {
                // New UI handles status implicitly
            });
        }

        private void AppendLog(string message, bool isError = false)
        {
            Dispatcher.InvokeAsync(() =>
            {
                // New UI handles history implicitly
            });
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Pencere kapanışı
        // ═════════════════════════════════════════════════════════════════════

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_closing) return;
            _closing = true;
            _isConnectedSession = false;
            _pairing.Dispose();
            _discovery.Dispose();
            _xfer.Cancel();
            _net.Dispose();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) 
        { 
            // Cihaz listesi UI'dan anında silinmemeli, çünkü broadcast 2 saniyede bir geliyor.
            // Sadece loglara yazdırıp arka planda dinlemeye devam etmesini sağlıyoruz.
            AppendLog("Ağ taraması tetiklendi...");
            int port = _settings.Port > 0 ? _settings.Port : FileTransferService.DefaultPort; 
            _discovery.Start(port, isReceiving: false); 
        }

        private async void BtnSendToPeer_Click(object sender, RoutedEventArgs e) 
        { 
            if ((sender as System.Windows.Controls.Button)?.Tag is DiscoveredPeer peer) 
            { 
                _selectedPeer = peer; 
                _currentPairingPin = new Random().Next(100000, 999999).ToString();
                
                if (FindName("PairingModal") is Grid pm) pm.Visibility = Visibility.Visible;
                if (FindName("TxtPairingTitle") is System.Windows.Controls.TextBlock title) title.Text = "Bağlanılıyor...";
                if (FindName("TxtPairingMessage") is System.Windows.Controls.TextBlock msg) msg.Text = $"{peer.Hostname} cihazının onayı bekleniyor.";
                if (FindName("TxtPairingPin") is System.Windows.Controls.TextBlock pin) pin.Text = _currentPairingPin;
                
                if (FindName("PairingReceiveButtons") is Grid recvBtns) recvBtns.Visibility = Visibility.Collapsed;
                if (FindName("PairingSendButtons") is Grid sendBtns) sendBtns.Visibility = Visibility.Visible;

                AppendLog($"{peer.Hostname} cihazına eşleşme isteği gönderiliyor. Kod: {_currentPairingPin}");
                
                bool sent = await _pairing.SendRequestAsync(peer.IP, _currentPairingPin, Environment.MachineName);
                if (!sent)
                {
                    if (FindName("PairingModal") is Grid pm2) pm2.Visibility = Visibility.Collapsed;
                    
                    var result = MessageBox.Show(
                        "İstek gönderilemedi.\n\nEğer cihazları doğrudan Ethernet kablosu ile bağladıysanız, Windows Güvenlik Duvarı bağlantıyı engelliyor olabilir.\n\nOtomatik olarak Güvenlik Duvarı izni vermek ister misiniz? (Yönetici izni gerektirir)", 
                        "Bağlantı Engellendi", 
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        
                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            NetworkManager.AddFirewallRule();
                            MessageBox.Show("Güvenlik Duvarı izni eklendi. Lütfen tekrar bağlanmayı deneyin.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"İzin eklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
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

        private void SwitchPanel(System.Windows.Controls.Grid panelToShow, string title)
        {
            if (PanelDiscovery == null) return;
            PanelDiscovery.Visibility = Visibility.Collapsed;
            PanelActive.Visibility = Visibility.Collapsed;
            PanelHistory.Visibility = Visibility.Collapsed;
            PanelSettings.Visibility = Visibility.Collapsed;
            
            if (TxtHeaderTitle != null) TxtHeaderTitle.Text = title;
            if (BtnPairDevice != null) BtnPairDevice.Visibility = (panelToShow == PanelDiscovery) ? Visibility.Visible : Visibility.Collapsed;
            
            panelToShow.Visibility = Visibility.Visible;
            System.Windows.Media.Animation.DoubleAnimation fadeIn = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.0, To = 1.0, Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            panelToShow.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        private void MenuDiscovery_Click(object sender, RoutedEventArgs e) => SwitchPanel(PanelDiscovery, "Keşif Modu");
        private void MenuActiveSession_Click(object sender, RoutedEventArgs e) => SwitchPanel(PanelActive, "Aktif Oturum");
        private void MenuHistory_Click(object sender, RoutedEventArgs e) => SwitchPanel(PanelHistory, "Transfer Geçmişi");
        private void MenuSettings_Click(object sender, RoutedEventArgs e) => SwitchPanel(PanelSettings, "Ayarlar");
        private void MenuPair_Click(object sender, RoutedEventArgs e)
        {
            var w = new Window { Title = "Manuel Cihaz Ekle", Width = 300, Height = 180, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize, Background = (SolidColorBrush)FindResource("SurfaceBrush"), Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush") };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = "Hedef IP Adresi:", FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,8) });
            var txt = new TextBox { Style = (Style)FindResource("ModernTextBox"), Margin = new Thickness(0, 0, 0, 16) };
            sp.Children.Add(txt);
            var btn = new Button { Content = "Bağlan", Style = (Style)FindResource("GradientButton"), Padding = new Thickness(0, 8, 0, 8) };
            btn.Click += async (s, args) => {
                string ip = txt.Text.Trim();
                if (NetworkManager.IsValidIP(ip)) {
                    w.DialogResult = true;
                    _selectedPeer = new DiscoveredPeer { IP = ip, Hostname = ip };
                    _currentPairingPin = new Random().Next(100000, 999999).ToString();
                    
                    if (FindName("PairingModal") is Grid pm) pm.Visibility = Visibility.Visible;
                    if (FindName("TxtPairingTitle") is TextBlock title) title.Text = "Bağlanılıyor...";
                    if (FindName("TxtPairingMessage") is TextBlock msg) msg.Text = $"{ip} cihazının onayı bekleniyor.";
                    if (FindName("TxtPairingPin") is TextBlock pin) pin.Text = _currentPairingPin;
                    if (FindName("PairingReceiveButtons") is Grid recvBtns) recvBtns.Visibility = Visibility.Collapsed;
                    if (FindName("PairingSendButtons") is Grid sendBtns) sendBtns.Visibility = Visibility.Visible;
                    
                    bool sent = await _pairing.SendRequestAsync(ip, _currentPairingPin, Environment.MachineName);
                    if (!sent)
                    {
                        if (FindName("PairingModal") is Grid pm2) pm2.Visibility = Visibility.Collapsed;
                        var res = MessageBox.Show("İstek gönderilemedi.\n\nGüvenlik duvarı izni vermek ister misiniz?", "Hata", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (res == MessageBoxResult.Yes) {
                            try { NetworkManager.AddFirewallRule(); MessageBox.Show("İzin eklendi. Tekrar deneyin."); }
                            catch (Exception ex) { MessageBox.Show("Hata: " + ex.Message); }
                        }
                    }
                } else MessageBox.Show("Geçersiz IP adresi.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            sp.Children.Add(btn);
            w.Content = sp;
            w.ShowDialog();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Environment.Exit(0);
        }



        private void Border_Drop(object sender, DragEventArgs e)
        {
            DropZoneBorder.Background = Brushes.Transparent;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && _isConnectedSession)
                {
                    Task.Run(() => AddFilesAndSend(files));
                }
            }
        }
        private void Border_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Copy;
            else e.Effects = DragDropEffects.None;
            DropZoneBorder.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            e.Handled = true;
        }
        private void Border_DragLeave(object sender, DragEventArgs e)
        {
            DropZoneBorder.Background = Brushes.Transparent;
        }

        private void BtnCancelTransfer_Click(object sender, RoutedEventArgs e)
        {
            // _xfer.CancelTransfer();
        }

        private void ThemeCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string theme)
            {
                ApplyTheme(theme);
                AppendLog($"Tema uygulandı: {theme}");
                
                // Remove highlight from all theme cards
                if (ThemeWrapPanel != null)
                {
                    foreach (var child in ThemeWrapPanel.Children)
                    {
                        if (child is Border b)
                        {
                            b.BorderBrush = Application.Current.Resources["BorderBrush"] as SolidColorBrush;
                            b.BorderThickness = new Thickness(1);
                        }
                    }
                }
                
                // Highlight selected
                border.BorderBrush = Application.Current.Resources["AccentBrush"] as SolidColorBrush;
                border.BorderThickness = new Thickness(2);

                // Save to settings
                _settings.Theme = theme;
                SettingsService.Save(_settings);
            }
        }

        private void ApplyTheme(string theme)
        {
            var res = Application.Current.Resources;
            Color bg, surface, surface2, border, accent, text, textSec, textMuted;

            switch (theme)
            {
                case "nordic":
                    bg = (Color)ColorConverter.ConvertFromString("#E6F0FA");
                    surface = (Color)ColorConverter.ConvertFromString("#FFFFFF");
                    surface2 = (Color)ColorConverter.ConvertFromString("#F8FAFC");
                    border = (Color)ColorConverter.ConvertFromString("#E2E8F0");
                    accent = (Color)ColorConverter.ConvertFromString("#3B82F6");
                    text = (Color)ColorConverter.ConvertFromString("#0F172A");
                    textSec = (Color)ColorConverter.ConvertFromString("#475569");
                    textMuted = (Color)ColorConverter.ConvertFromString("#94A3B8");
                    break;
                case "cyberpunk":
                    bg = (Color)ColorConverter.ConvertFromString("#0A0A0B");
                    surface = (Color)ColorConverter.ConvertFromString("#170F24");
                    surface2 = (Color)ColorConverter.ConvertFromString("#1F1332");
                    border = (Color)ColorConverter.ConvertFromString("#35245A");
                    accent = (Color)ColorConverter.ConvertFromString("#E879F9");
                    text = (Color)ColorConverter.ConvertFromString("#F8FAFC");
                    textSec = (Color)ColorConverter.ConvertFromString("#C084FC");
                    textMuted = (Color)ColorConverter.ConvertFromString("#A855F7");
                    break;
                case "forest":
                    bg = (Color)ColorConverter.ConvertFromString("#052E16");
                    surface = (Color)ColorConverter.ConvertFromString("#14532D");
                    surface2 = (Color)ColorConverter.ConvertFromString("#166534");
                    border = (Color)ColorConverter.ConvertFromString("#22C55E");
                    accent = (Color)ColorConverter.ConvertFromString("#4ADE80");
                    text = (Color)ColorConverter.ConvertFromString("#F0FDF4");
                    textSec = (Color)ColorConverter.ConvertFromString("#86EFAC");
                    textMuted = (Color)ColorConverter.ConvertFromString("#BBF7D0");
                    break;
                case "sunset":
                    bg = (Color)ColorConverter.ConvertFromString("#431407");
                    surface = (Color)ColorConverter.ConvertFromString("#7C2D12");
                    surface2 = (Color)ColorConverter.ConvertFromString("#9A3412");
                    border = (Color)ColorConverter.ConvertFromString("#C2410C");
                    accent = (Color)ColorConverter.ConvertFromString("#FB923C");
                    text = (Color)ColorConverter.ConvertFromString("#FFF7ED");
                    textSec = (Color)ColorConverter.ConvertFromString("#FFEDD5");
                    textMuted = (Color)ColorConverter.ConvertFromString("#FED7AA");
                    break;
                case "crimson":
                default:
                    bg = (Color)ColorConverter.ConvertFromString("#0b1326");
                    surface = (Color)ColorConverter.ConvertFromString("#131b2e");
                    surface2 = (Color)ColorConverter.ConvertFromString("#171f33");
                    border = (Color)ColorConverter.ConvertFromString("#2d3449");
                    accent = (Color)ColorConverter.ConvertFromString("#4d8eff");
                    text = (Color)ColorConverter.ConvertFromString("#dae2fd");
                    textSec = (Color)ColorConverter.ConvertFromString("#c2c6d6");
                    textMuted = (Color)ColorConverter.ConvertFromString("#8c909f");
                    break;
            }

            res["BackgroundBrush"] = new SolidColorBrush(bg);
            res["SurfaceBrush"] = new SolidColorBrush(surface);
            res["Surface2Brush"] = new SolidColorBrush(surface2);
            res["BorderBrush"] = new SolidColorBrush(border);
            res["AccentBrush"] = new SolidColorBrush(accent);
            res["TextPrimaryBrush"] = new SolidColorBrush(text);
            res["TextSecBrush"] = new SolidColorBrush(textSec);
            res["TextMutedBrush"] = new SolidColorBrush(textMuted);
            
            var primaryGradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            primaryGradient.GradientStops.Add(new GradientStop(accent, 0.0));
            // Just a slightly darker version for gradient
            var darkAccent = Color.FromArgb(255, (byte)(accent.R * 0.8), (byte)(accent.G * 0.8), (byte)(accent.B * 0.8));
            primaryGradient.GradientStops.Add(new GradientStop(darkAccent, 1.0));
            res["PrimaryGradientBrush"] = primaryGradient;
        }

        private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Title = "İndirme klasörü seçin (herhangi bir dosya seçin)";
            dlg.CheckFileExists = false;
            dlg.FileName = "Klasor_Sec";
            if (!string.IsNullOrEmpty(TxtDownloadFolder.Text) && Directory.Exists(TxtDownloadFolder.Text))
                dlg.InitialDirectory = TxtDownloadFolder.Text;
            if (dlg.ShowDialog() == true)
            {
                string dir = System.IO.Path.GetDirectoryName(dlg.FileName) ?? "";
                if (!string.IsNullOrEmpty(dir))
                    TxtDownloadFolder.Text = dir;
            }
        }

        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (!_initialized) return;
            
            _settings.SaveDirectory = TxtDownloadFolder.Text;
            _settings.AutoUpdate = ChkAutoUpdate.IsChecked == true;
            _settings.AutoReceive = ChkAutoReceive.IsChecked == true;
            if (int.TryParse(TxtPort.Text, out int port) && port > 0 && port < 65536)
                _settings.Port = port;
                
            SettingsService.Save(_settings);
        }

        private void UpdateNetworkInfo()
        {
            try
            {
                string localIp = Dosyaktar.Services.NetworkManager.GetLocalIP();
                if (localIp == "127.0.0.1")
                {
                    if (TxtNetworkType != null) TxtNetworkType.Text = "Seçili ağ yok";
                    if (NetSpeedBar != null) NetSpeedBar.Width = 0;
                    if (TxtFooterStatus != null) TxtFooterStatus.Text = "Bağlantı yok";
                    return;
                }

                System.Net.NetworkInformation.NetworkInterface activeNic = null;
                foreach (var n in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (n.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    foreach (var addr in n.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.ToString() == localIp)
                        {
                            activeNic = n;
                            break;
                        }
                    }
                    if (activeNic != null) break;
                }

                if (activeNic == null)
                {
                    if (TxtNetworkType != null) TxtNetworkType.Text = "Seçili ağ yok";
                    if (NetSpeedBar != null) NetSpeedBar.Width = 0;
                    if (TxtFooterStatus != null) TxtFooterStatus.Text = "Bağlantı yok";
                    return;
                }

                bool isWifi = activeNic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211;
                long speedMbps = activeNic.Speed / 1_000_000;
                string typeName = isWifi ? $"WiFi ({speedMbps} Mbps)" : $"Ethernet ({speedMbps} Mbps)";
                if (TxtNetworkType != null) TxtNetworkType.Text = typeName;
                if (TxtFooterStatus != null) TxtFooterStatus.Text = isWifi ? "WiFi Bağlı" : "Kablolu Bağlı";
                double barWidth = Math.Min(speedMbps / 10.0, 260);
                if (NetSpeedBar != null) NetSpeedBar.Width = barWidth;
            }
            catch
            {
                if (TxtNetworkType != null) TxtNetworkType.Text = "Tespit edilemedi";
            }
        }

        private void LoadSettingsToUI()
        {
            TxtHostname.Text = Environment.MachineName;
            TxtDownloadFolder.Text = _settings.SaveDirectory;
            TxtPort.Text = (_settings.Port > 0 ? _settings.Port : FileTransferService.DefaultPort).ToString();
            ChkAutoUpdate.IsChecked = _settings.AutoUpdate;
            ChkAutoReceive.IsChecked = _settings.AutoReceive;
        }
    }
}