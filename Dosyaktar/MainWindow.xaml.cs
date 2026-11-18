using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Dosyaktar.Services;
using Microsoft.Win32;
using Path = System.IO.Path;
using QRCoder;

namespace Dosyaktar
{
    // ─── Kuyruktaki dosya ─────────────────────────────────────────────────────
    public sealed class FileEntry
    {
        public string Path     { get; }
        public string Name     { get; }
        public long   Size     { get; }
        public string SizeText { get; }

        public FileEntry(string path)
        {
            Path = path;
            var fi = new FileInfo(path);
            Name = fi.Name;
            Size = fi.Length;
            SizeText = FileTransferService.FormatSize(fi.Length);
        }
    }

    // ─── Etkinlik günlüğü satırı ──────────────────────────────────────────────
    public sealed class LogEntry
    {
        public string Time { get; init; } = "";
        public string Text { get; init; } = "";
        public string Kind { get; init; } = "info";
    }

    public partial class MainWindow : Window
    {
        // ── servisler ─────────────────────────────────────────────────────────
        private readonly NetworkManager       _net      = new();
        private readonly FileTransferService  _sender   = new();   // giden
        private readonly FileTransferService  _receiver = new();   // gelen
        private readonly NetworkDiscovery     _discovery = new();
        private readonly PairingService       _pairing  = new();
        private readonly MobileServer         _mobile   = new();

        // ── veri ──────────────────────────────────────────────────────────────
        private readonly ObservableCollection<FileEntry>       _files    = new();
        private readonly ObservableCollection<DiscoveredPeer>  _peers    = new();
        private readonly ObservableCollection<LogEntry>        _logs     = new();
        private readonly ObservableCollection<HistoryEntry>    _history  = new();
        private readonly ObservableCollection<SharedFile>      _shared   = new();
        private readonly ObservableCollection<MobileSession>   _sessions = new();

        // ── durum ─────────────────────────────────────────────────────────────
        private AppSettings    _settings;
        private DiscoveredPeer? _selectedPeer;
        private bool _initialized;
        private bool _suppressSettings;
        private bool _closing;
        private bool _senderBusy;

        // oturum
        private bool     _isConnectedSession;
        private bool     _receiving;
        private string   _connectedTargetIP   = "";
        private string   _connectedTargetName = "";
        private DateTime _sessionStarted      = DateTime.Now;

        // eşleşme
        private string _pendingPairIp   = "";
        private string _pendingPairName = "";
        private string _pendingPairPin  = "";
        private bool   _pairOutgoing;

        // mobil köprü
        
        private bool     _gamesLoaded;
        private DateTime _lastMobileUiTick = DateTime.MinValue;

        // transfer kartı
        private int _cardToken;

        // ─────────────────────────────────────────────────────────────────────

        public MainWindow(AppSettings settings)
        {
            _settings = settings ?? SettingsService.Load();

            InitializeComponent();

            PeerList.ItemsSource         = _peers;
            QueueList.ItemsSource        = _files;
            LogList.ItemsSource          = _logs;
            HistoryList.ItemsSource      = _history;
            SharedFilesList.ItemsSource  = _shared;
            MobileSessionsList.ItemsSource = _sessions;

            _initialized = true;

            ApplySettingsToServices();
            BuildThemeGallery();
            WireEvents();
            LoadSettingsToUI();
            UpdateIdentity();
            UpdateNetworkInfo();
            UpdateQueueUi();
            UpdateSharedUi();
            UpdateSessionUi();
            UpdateMobileUi();
            LoadHistoryIntoView();

            StateChanged  += OnWindowStateChanged;
            Loaded        += OnLoaded;
            PreviewKeyDown += OnPreviewKeyDown;

            AttachWindowDragHandlers();

            StartDiscovery();
            try { _pairing.StartListening(); }
            catch (Exception ex) { AppendLog("Eşleşme servisi başlatılamadı: " + ex.Message, "err"); }

            RbSend.IsChecked = true;
            RestoreLastPage();
        }

        // ═════════════════════════════════════════════════════════════════════
        //  BAŞLANGIÇ
        // ═════════════════════════════════════════════════════════════════════

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            StartScanPulse();
            _ = CheckUpdatesOnStartAsync();
        }

        private void ApplySettingsToServices()
        {
            string name = string.IsNullOrWhiteSpace(_settings.Hostname)
                ? Environment.MachineName
                : _settings.Hostname.Trim();

            _settings.Hostname   = name;
            _discovery.DeviceName = name;
            _mobile.DeviceName    = name;
            _mobile.SaveFolder    = EffectiveDownloadDir;

            if (string.IsNullOrWhiteSpace(_settings.SaveDirectory))
                _settings.SaveDirectory = AppSettings.DefaultSaveDir;
        }

        private void WireEvents()
        {
            // ağ
            _net.LogMessage += (_, m) => AppendLog("[Ağ] " + m);
            _net.Error      += (_, m) => AppendLog("[Ağ] " + m, "err");

            // gönderim
            _sender.StatusChanged += (_, m) => AppendLog(m);
            _sender.Error         += (_, m) => AppendLog(m, "err");
            _sender.ProgressChanged += (_, p) => Dispatcher.InvokeAsync(() => ShowProgress(p, true));
            _sender.FlashProgressChanged += (_, p) => Dispatcher.InvokeAsync(() => ShowFlash(p));

            // alım
            _receiver.StatusChanged += (_, m) => AppendLog("[Alım] " + m);
            _receiver.Error         += (_, m) => AppendLog("[Alım] " + m, "err");
            _receiver.ProgressChanged += (_, p) => Dispatcher.InvokeAsync(() => ShowProgress(p, false));
            _receiver.FlashProgressChanged += (_, p) => Dispatcher.InvokeAsync(() => ShowFlash(p));

            // keşif
            _discovery.PeersChanged += (_, list) => Dispatcher.InvokeAsync(() => UpdatePeerList(list));
            _discovery.LogMessage   += (_, m) => AppendLog("[Keşif] " + m);

            // eşleşme
            _pairing.PairingRequested        += Pairing_Requested;
            _pairing.PairingResponseReceived += Pairing_ResponseReceived;
            _pairing.LogMessage              += (_, m) => AppendLog("[Eşleşme] " + m);

            // mobil köprü
            _mobile.Log += m => AppendLog("[Mobil] " + m);
            _mobile.SessionsChanged += () => Dispatcher.InvokeAsync(() => { RefreshSessions(); RefreshMobileCode(); });
            _mobile.FileReceived += f => Dispatcher.InvokeAsync(() => OnMobileFileReceived(f));
            _mobile.TransferChanged += t => Dispatcher.InvokeAsync(() => OnMobileTransfer(t));

            // oyun kütüphanesi
            GamesView.SendRequested += async (_, game) => await SendGameAsync(game);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  NAVİGASYON
        // ═════════════════════════════════════════════════════════════════════

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (!_initialized) return;

            if      (ReferenceEquals(sender, RbSession)) ShowPage("session");
            else if (ReferenceEquals(sender, RbGames))   ShowPage("games");
            else if (ReferenceEquals(sender, RbMobile))  ShowPage("mobile");
            else if (ReferenceEquals(sender, RbHistory)) ShowPage("history");
            else if (ReferenceEquals(sender, RbSettings))ShowPage("settings");
            else                                          ShowPage("send");
        }

        private void RestoreLastPage()
        {
            string page = _settings.LastPage is "session" or "games" or "mobile" or "history" or "settings"
                ? _settings.LastPage
                : "send";

            var tab = page switch
            {
                "session" => RbSession,
                "games"   => RbGames,
                "mobile"  => RbMobile,
                "history" => RbHistory,
                "settings"=> RbSettings,
                _         => RbSend
            };

            if (tab.IsChecked == true) ShowPage(page);
            else tab.IsChecked = true;   // Checked → Nav_Checked → ShowPage
        }

        private void ShowPage(string key)
        {
            if (PageSend == null) return;

            if (_settings.LastPage != key)
            {
                _settings.LastPage = key;
                SettingsService.Save(_settings);
            }

            PageSend.Visibility     = key == "send"     ? Visibility.Visible : Visibility.Collapsed;
            PageSession.Visibility  = key == "session"  ? Visibility.Visible : Visibility.Collapsed;
            PageGames.Visibility    = key == "games"    ? Visibility.Visible : Visibility.Collapsed;
            PageMobile.Visibility   = key == "mobile"   ? Visibility.Visible : Visibility.Collapsed;
            PageHistory.Visibility  = key == "history"  ? Visibility.Visible : Visibility.Collapsed;
            PageSettings.Visibility = key == "settings" ? Visibility.Visible : Visibility.Collapsed;

            // Oyunlar sayfası tam genişlik kullanır; sağ aktarım paneli gizlenir.
            RailRight.Visibility = key == "games" ? Visibility.Collapsed : Visibility.Visible;

            TxtPageTitle.Text = key switch
            {
                "session"  => "Oturum",
                "games"    => "Oyunlar",
                "mobile"   => "Mobil köprü",
                "history"  => "Geçmiş",
                "settings" => "Ayarlar",
                _          => "Keşif"
            };

            var page = key switch
            {
                "session"  => PageSession,
                "games"    => (FrameworkElement)PageGames,
                "mobile"   => PageMobile,
                "history"  => PageHistory,
                "settings" => PageSettings,
                _          => (FrameworkElement)PageSend
            };

            page.BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                From = 0.0,
                To   = 1.0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });

            if (key == "mobile")
            {
                if (!_mobile.IsRunning) StartBridge();
                UpdateMobileUi();
            }
            else if (key == "history")
            {
                LoadHistoryIntoView();
            }
            else if (key == "games")
            {
                GamesView.SetSendAvailable(_isConnectedSession);

                if (!_gamesLoaded)
                {
                    _gamesLoaded = true;
                    _ = GamesView.RefreshAsync();
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  PENCERE
        // ═════════════════════════════════════════════════════════════════════

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            // WindowChrome + WindowStyle=None: maximize taşmasını içeriden kapat.
            Padding = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

        private void BtnCloseWindow_Click(object sender, RoutedEventArgs e) => Close();

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (PairingOverlay.Visibility == Visibility.Visible)
                {
                    BtnPairingCancel_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control
                     && PageGames.Visibility == Visibility.Visible)
            {
                GamesView.FocusSearch();
                e.Handled = true;
            }
            else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            {
                BtnPickFiles_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control
                     && e.Key >= Key.D1 && e.Key <= Key.D6)
            {
                var tab = (e.Key - Key.D1) switch
                {
                    0 => RbSend,
                    1 => RbSession,
                    2 => RbGames,
                    3 => RbMobile,
                    4 => RbHistory,
                    _ => RbSettings
                };

                tab.IsChecked = true;
                e.Handled = true;
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_closing) return;
            _closing = true;

            _isConnectedSession = false;
            _receiving = false;

            try { _settings.LastTargetIP = _connectedTargetIP; SettingsService.Save(_settings); } catch { }
            try { _sender.Cancel(); }   catch { }
            try { _receiver.Cancel(); } catch { }
            try { _pairing.Dispose(); } catch { }
            try { _discovery.Dispose(); } catch { }
            try { _mobile.Dispose(); } catch { }
            try { _net.Dispose(); } catch { }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  CİHAZ KEŞFİ
        // ═════════════════════════════════════════════════════════════════════

        private int EffectivePort =>
            _settings.Port is > 0 and < 65536 ? _settings.Port : FileTransferService.DefaultPort;

        private string EffectiveDownloadDir =>
            string.IsNullOrWhiteSpace(_settings.SaveDirectory)
                ? AppSettings.DefaultSaveDir
                : _settings.SaveDirectory;

        private void StartDiscovery()
        {
            _discovery.DeviceName = _settings.Hostname;
            _discovery.Start(EffectivePort, _isConnectedSession);
            AppendLog($"Keşif başladı · port {EffectivePort}");
        }

        private void UpdatePeerList(IReadOnlyList<DiscoveredPeer> peers)
        {
            _peers.Clear();
            foreach (var p in peers.OrderBy(p => p.Hostname, StringComparer.CurrentCultureIgnoreCase))
                _peers.Add(p);

            bool has = _peers.Count > 0;
            EmptyPeers.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            TxtPeerSubtitle.Text  = has
                ? $"{_peers.Count} cihaz aynı ağda"
                : "Aynı ağdaki cihazlar aranıyor…";

            if (_selectedPeer != null && !_peers.Any(p => p.IP == _selectedPeer.IP))
                ClearSelectedPeer();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("Ağ taraması yenilendi.");
            SetStatus("Cihazlar taranıyor…", "info");
            StartDiscovery();
        }

        private void BtnSendToPeer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement el || el.Tag is not DiscoveredPeer peer) return;

            SelectPeer(peer);

            if (_isConnectedSession && _connectedTargetIP == peer.IP)
            {
                ShowPage("session");
                RbSession.IsChecked = true;
                return;
            }

            if (_isConnectedSession)
            {
                SetStatus("Önce mevcut oturumu kapat", "warn");
                return;
            }

            _ = BeginPairingAsync(peer);
        }

        private void SelectPeer(DiscoveredPeer peer)
        {
            _selectedPeer = peer;
            TxtTargetName.Text = string.IsNullOrEmpty(peer.Hostname) ? peer.IP : peer.Hostname;
            TxtTargetIp.Text   = peer.IP;
            SelectedTargetBar.Visibility = Visibility.Visible;
            TxtDropHint.Text = $"{TxtTargetName.Text} cihazına gönderilecek — bağlanmak için dosya seç.";
        }

        private void ClearSelectedPeer()
        {
            _selectedPeer = null;
            SelectedTargetBar.Visibility = Visibility.Collapsed;
            TxtDropHint.Text = "Klasör, oyun veya 50 GB'lık tek dosya — hepsi olur.";
        }

        private void BtnClearTarget_Click(object sender, RoutedEventArgs e) => ClearSelectedPeer();

        // ═════════════════════════════════════════════════════════════════════
        //  EŞLEŞME
        // ═════════════════════════════════════════════════════════════════════

        private async Task BeginPairingAsync(DiscoveredPeer peer)
        {
            _pendingPairIp   = peer.IP;
            _pendingPairName = string.IsNullOrEmpty(peer.Hostname) ? peer.IP : peer.Hostname;
            _pendingPairPin  = Random.Shared.Next(100000, 1000000).ToString();
            _pairOutgoing    = true;

            ShowPairingOverlay();
            AppendLog($"{_pendingPairName} için eşleşme isteği gönderiliyor.");

            bool sent = await _pairing.SendRequestAsync(_pendingPairIp, _pendingPairPin, _settings.Hostname);

            if (!sent)
            {
                HidePairingOverlay();
                SetStatus("İstek gönderilemedi — güvenlik duvarı engelliyor olabilir", "err");

                var answer = MessageBox.Show(
                    "İstek gönderilemedi.\n\nWindows Güvenlik Duvarı bağlantıyı engelliyor olabilir. " +
                    "Otomatik izin kuralı eklemek ister misiniz? (Yönetici izni gerekir)",
                    "Bağlantı engellendi",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (answer == MessageBoxResult.Yes)
                {
                    try
                    {
                        NetworkManager.AddFirewallRule();
                        SetStatus("Güvenlik duvarı kuralı eklendi — yeniden deneyin", "ok");
                    }
                    catch (Exception ex)
                    {
                        AppendLog("Kural eklenemedi: " + ex.Message, "err");
                    }
                }
            }
        }

        private void ShowPairingOverlay()
        {
            PairingOverlay.Visibility = Visibility.Visible;

            TxtPairingHost.Text = _pairOutgoing
                ? $"{_pendingPairName} onayı bekleniyor"
                : $"{_pendingPairName} bağlanmak istiyor";
            TxtPairingIp.Text   = _pendingPairIp;
            TxtPairingPin.Text  = FormatPin(_pendingPairPin);

            PairingActions.Visibility  = _pairOutgoing ? Visibility.Collapsed : Visibility.Visible;
            BtnPairingCancel.Visibility = _pairOutgoing ? Visibility.Visible : Visibility.Collapsed;

            TxtPairingHint.Text = _pairOutgoing
                ? "Karşı cihazda aynı kod görünüyorsa kabul et."
                : "Kod iki cihazda da aynıysa kabul edin.";
        }

        private void HidePairingOverlay()
        {
            PairingOverlay.Visibility = Visibility.Collapsed;
            _pendingPairIp = _pendingPairName = _pendingPairPin = "";
        }

        private void Pairing_Requested(object? sender, (string IP, string Hostname, string Pin) e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_isConnectedSession)
                {
                    _ = _pairing.SendResponseAsync(e.IP, false);
                    AppendLog($"{e.Hostname} isteği reddedildi — oturum meşgul.", "warn");
                    return;
                }

                _pendingPairIp   = e.IP;
                _pendingPairName = string.IsNullOrEmpty(e.Hostname) ? e.IP : e.Hostname;
                _pendingPairPin  = e.Pin;
                _pairOutgoing    = false;

                ShowPairingOverlay();
                SetStatus($"{_pendingPairName} eşleşme isteği gönderdi", "accent");
            });
        }

        private void Pairing_ResponseReceived(object? sender, (string IP, bool Accepted) e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                HidePairingOverlay();

                if (e.Accepted)
                {
                    AppendLog("Eşleşme kabul edildi.");
                    StartSession(e.IP, string.IsNullOrEmpty(_pendingPairName) ? e.IP : _pendingPairName);
                }
                else
                {
                    AppendLog("Eşleşme reddedildi.", "warn");
                    SetStatus("Karşı cihaz isteği reddetti", "warn");
                }
            });
        }

        private async void BtnPairingAccept_Click(object sender, RoutedEventArgs e)
        {
            string ip = _pendingPairIp;
            string name = _pendingPairName;
            HidePairingOverlay();

            await _pairing.SendResponseAsync(ip, true);
            StartSession(ip, name);
        }

        private async void BtnPairingReject_Click(object sender, RoutedEventArgs e)
        {
            string ip = _pendingPairIp;
            HidePairingOverlay();

            await _pairing.SendResponseAsync(ip, false);
            AppendLog("Eşleşme isteği reddedildi.", "warn");
            SetStatus("İstek reddedildi", "warn");
        }

        private void BtnPairingCancel_Click(object sender, RoutedEventArgs e)
        {
            HidePairingOverlay();
            SetStatus("Eşleşme iptal edildi", "info");
            AppendLog("Eşleşme isteği iptal edildi.");
        }

        // ═════════════════════════════════════════════════════════════════════
        //  OTURUM
        // ═════════════════════════════════════════════════════════════════════

        private void StartSession(string ip, string name)
        {
            _isConnectedSession  = true;
            _connectedTargetIP   = ip;
            _connectedTargetName = string.IsNullOrEmpty(name) ? ip : name;
            _sessionStarted      = DateTime.Now;

            _settings.LastTargetIP = ip;
            SettingsService.Save(_settings);

            _discovery.SetReceivingMode(true);

            TxtSessName.Text  = _connectedTargetName;
            TxtSessIp.Text    = ip;
            TxtSessSince.Text = _sessionStarted.ToString("HH:mm");
            TxtSessPort.Text  = EffectivePort.ToString();
            TxtSessMode.Text  = _settings.UseFlashMode
                ? $"Flash · {_settings.FlashConnections} kanal"
                : "Standart";

            UpdateSessionUi();
            ShowPage("session");
            RbSession.IsChecked = true;

            SetStatus($"{_connectedTargetName} ile bağlantı kuruldu", "ok");
            GamesView.SetSendAvailable(true);
            AppendLog($"Oturum açıldı: {_connectedTargetName} ({ip})");

            if (_settings.AutoReceive) StartReceiving();
            else TxtSessionPercent.Text = "Alma kapalı — 'Almaya başla'ya bas";

            if (_files.Count > 0) _ = SendQueueAsync();
        }

        private void StartReceiving()
        {
            if (!_isConnectedSession || _receiving) return;

            _receiving = true;
            UpdateSessionUi();
            AppendLog($"Dinleme başladı · port {EffectivePort}");
            _ = Task.Run(ReceiveLoopAsync);
        }

        private void StopReceiving()
        {
            if (!_receiving) return;

            _receiving = false;
            try { _receiver.Cancel(); } catch { }
            UpdateSessionUi();
            AppendLog("Dinleme durduruldu.");
        }

        private async Task ReceiveLoopAsync()
        {
            while (_receiving && _isConnectedSession && !_closing)
            {
                string dir = EffectiveDownloadDir;

                try
                {
                    var result = await _receiver.StartReceivingAsync(dir, EffectivePort);

                    if (result.Success)
                    {
                        AppendedReceived(dir);
                    }
                    else if (!_closing && _receiving)
                    {
                        AppendLog("[Alım] " + result.Message, "warn");
                    }
                }
                catch (OperationCanceledException)
                {
                    // bağlantı kesildi
                }
                catch (Exception ex)
                {
                    if (!_receiving || _closing) break;
                    AppendLog("[Alım] " + ex.Message, "err");
                    await Task.Delay(1200);
                }
            }
        }

        private void AppendedReceived(string dir)
        {
            _ = Task.Run(() =>
            {
                GameInstallResult? install = null;
                try { install = GameTransferService.TryFinalize(dir); }
                catch (Exception ex) { AppendLog("[Oyun] " + ex.Message, "err"); }

                Dispatcher.InvokeAsync(() =>
                {
                    if (install != null)
                    {
                        CompleteTransferCard(install.Registered, install.Message);
                        AddHistory(new HistoryEntry
                        {
                            Title     = install.Registered ? "Oyun alındı" : "Gelen oyun",
                            Detail    = install.TargetPath ?? dir,
                            Direction = HistoryDirection.Received,
                            Success   = install.Registered,
                            When      = DateTime.Now
                        });
                        SetStatus(install.Registered ? "Oyun kütüphaneye eklendi" : "Oyun alındı",
                                  install.Registered ? "ok" : "warn");
                        AppendLog("[Oyun] " + install.Message, install.Registered ? "ok" : "warn");
                        return;
                    }

                    CompleteTransferCard(true, "Kaydedildi: " + dir);
                    AddHistory(new HistoryEntry
                    {
                        Title     = "Gelen dosyalar",
                        Detail    = dir,
                        Direction = HistoryDirection.Received,
                        Success   = true,
                        When      = DateTime.Now
                    });
                    SetStatus("Dosyalar alındı", "ok");
                    AppendLog("[Alındı] " + dir, "ok");
                });
            });
        }

        private void UpdateSessionUi()
        {
            if (SessionActive == null) return;

            SessionActive.Visibility = _isConnectedSession ? Visibility.Visible : Visibility.Collapsed;
            SessionEmpty.Visibility  = _isConnectedSession ? Visibility.Collapsed : Visibility.Visible;

            BtnReceiveToggle.Content = _receiving ? "Almayı durdur" : "Almaya başla";

            if (_isConnectedSession && !_receiving)
                TxtSessionPercent.Text = "Alma kapalı — dinleme başlatılmadı";
        }

        private void BtnReceiveToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_receiving) StopReceiving();
            else            StartReceiving();
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnectedSession) return;

            StopReceiving();
            try { _sender.Cancel(); } catch { }

            _isConnectedSession  = false;
            _connectedTargetIP   = "";
            _connectedTargetName = "";
            _senderBusy          = false;

            _discovery.SetReceivingMode(false);
            ActiveTransferCard.Visibility = Visibility.Collapsed;
            PbSession.Value = 0;
            TxtSessionSpeed.Text = "";
            TxtSessionEta.Text = "";
            TxtSessionPercent.Text = "Hazır";

            UpdateSessionUi();
            ShowPage("send");
            RbSend.IsChecked = true;

            GamesView.SetSendAvailable(false);

            SetStatus("Oturum sonlandırıldı", "info");
            AppendLog("Oturum kapatıldı.");
        }

        private async void BtnPairWithPin_Click(object sender, RoutedEventArgs e)
        {
            string pin = new string((TxtPairPin.Text ?? "").Where(char.IsDigit).ToArray());

            if (pin.Length != 6)
            {
                SetStatus("PIN 6 haneli olmalı", "warn");
                TxtPairPin.Focus();
                return;
            }

            // PIN karşıdan gelen isteklerde kullanılır; hedef IP yoksa istek gönderilemez.
            SetStatus("PIN, karşı cihazın isteğini onaylamak için kullanılır", "info");
            AppendLog("PIN doğrulama: karşı cihazdan istek bekleniyor.", "warn");
            await Task.CompletedTask;
        }

        private async void BtnManualConnect_Click(object sender, RoutedEventArgs e)
        {
            string ip = (TxtManualIp.Text ?? "").Trim();

            if (!NetworkManager.IsValidIP(ip))
            {
                SetStatus("Geçersiz IP adresi", "warn");
                TxtManualIp.Focus();
                return;
            }

            if (_isConnectedSession)
            {
                SetStatus("Önce mevcut oturumu kapat", "warn");
                return;
            }

            await BeginPairingAsync(new DiscoveredPeer { IP = ip, Hostname = ip });
        }

        // ═════════════════════════════════════════════════════════════════════
        //  DOSYA SEÇİMİ / SÜRÜKLE-BIRAK
        // ═════════════════════════════════════════════════════════════════════

        private void DropZone_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && !_senderBusy)
                BtnPickFiles_Click(sender, e);
        }

        private void BtnPickFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Gönderilecek dosyaları seç",
                Multiselect = true
            };

            if (dlg.ShowDialog() == true)
                _ = HandlePathsAsync(dlg.FileNames);
        }

        private void BtnPickFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Gönderilecek klasörü seç" };
            if (dlg.ShowDialog() == true)
                _ = SendFolderAsync(dlg.FolderName);
        }

        private void AttachWindowDragHandlers()
        {
            AllowDrop = true;
            AddHandler(DragDrop.DragOverEvent,  new DragEventHandler(Window_DragOver));
            AddHandler(DragDrop.DragLeaveEvent, new DragEventHandler(Window_DragLeave));
            AddHandler(DragDrop.DropEvent,      new DragEventHandler(Window_Drop));
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                DropOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            DropOverlay.Visibility = Visibility.Visible;
        }

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            var p = e.GetPosition(this);
            if (p.X < 0 || p.Y < 0 || p.X > ActualWidth || p.Y > ActualHeight)
                DropOverlay.Visibility = Visibility.Collapsed;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;

            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
                _ = HandleDropAsync(paths);
        }

        private void DropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            DropOverlay.Visibility = Visibility.Collapsed;

            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
                _ = HandleDropAsync(paths);
        }

        private async Task HandleDropAsync(string[] paths)
        {
            var files = paths.Where(File.Exists).ToList();
            var dirs  = paths.Where(Directory.Exists).ToList();

            if (files.Count == 0 && dirs.Count == 1)
            {
                await SendFolderAsync(dirs[0]);
                return;
            }

            if (dirs.Count > 0 && files.Count == 0)
            {
                await SendFolderAsync(dirs[0]);
                return;
            }

            if (dirs.Count > 0)
                AppendLog("Klasörler tek tek gönderilir; bu bırakmada yalnızca dosyalar alındı.", "warn");

            await HandlePathsAsync(files);
        }

        private async Task HandlePathsAsync(IEnumerable<string> paths)
        {
            var list = paths.Where(File.Exists).ToList();
            if (list.Count == 0) return;

            foreach (var p in list) AddToQueue(p);

            if (_isConnectedSession)
            {
                await SendQueueAsync();
                return;
            }

            var peer = _selectedPeer ?? (_peers.Count == 1 ? _peers[0] : null);

            if (peer == null)
            {
                SetStatus("Hedef seçilmedi — Keşif'te bir cihaza 'Gönder' de", "warn");
                ShowPage("send");
                RbSend.IsChecked = true;
                OpenPairingPromptForSelection();
                return;
            }

            await BeginPairingAsync(peer);
        }

        private void OpenPairingPromptForSelection()
        {
            if (_peers.Count == 0)
                AppendLog("Ağda cihaz yok. Karşı tarafta Dosyaktar açık ve aynı ağda olmalı.", "warn");
            else
                AppendLog($"{_files.Count} dosya kuyrukta — bir cihaza 'Gönder' de.", "warn");
        }

        private void AddToQueue(string path)
        {
            if (_files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
                return;

            try { _files.Add(new FileEntry(path)); }
            catch (Exception ex) { AppendLog("Dosya eklenemedi: " + ex.Message, "err"); }

            UpdateQueueUi();
        }

        private void BtnRemoveQueued_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is FileEntry fe)
            {
                _files.Remove(fe);
                UpdateQueueUi();
            }
        }

        private void UpdateQueueUi()
        {
            TxtQueueCount.Text = _files.Count.ToString();
            EmptyQueue.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  GÖNDERİM
        // ═════════════════════════════════════════════════════════════════════

        private void BtnSessionSendFiles_Click(object sender, RoutedEventArgs e) => BtnPickFiles_Click(sender, e);

        private void BtnSessionSendFolder_Click(object sender, RoutedEventArgs e) => BtnPickFolder_Click(sender, e);

        private async Task SendQueueAsync()
        {
            if (!_isConnectedSession)
            {
                SetStatus("Önce bir cihazla eşleş", "warn");
                return;
            }

            if (_senderBusy)
            {
                AppendLog("Zaten bir aktarım sürüyor.", "warn");
                return;
            }

            var snapshot = _files.Select(f => f.Path).Where(File.Exists).ToList();
            if (snapshot.Count == 0)
            {
                SetStatus("Gönderilecek dosya yok", "warn");
                return;
            }

            _senderBusy = true;
            BtnCancelTransfer.IsEnabled = true;

            BeginTransferCard(
                snapshot.Count == 1 ? Path.GetFileName(snapshot[0]) : $"{snapshot.Count} dosya",
                _connectedTargetName);

            SetStatus("Gönderiliyor…", "accent");

            TransferResult result;
            try
            {
                result = await _sender.SendFilesAsync(snapshot, _connectedTargetIP, EffectivePort);
            }
            catch (Exception ex)
            {
                result = new TransferResult(false, ex.Message);
            }

            _senderBusy = false;
            BtnCancelTransfer.IsEnabled = false;

            bool cancelled = result.Message.Contains("iptal", StringComparison.OrdinalIgnoreCase);

            if (result.Success)
            {
                long total = _files.Where(f => File.Exists(f.Path)).Sum(f => f.Size);
                CompleteTransferCard(true, "Tamamlandı · " + FileTransferService.FormatSize(total));
                AddHistory(new HistoryEntry
                {
                    Title     = snapshot.Count == 1 ? Path.GetFileName(snapshot[0]) : $"{snapshot.Count} dosya",
                    Detail    = $"{_connectedTargetName} · {FileTransferService.FormatSize(total)}",
                    Direction = HistoryDirection.Sent,
                    Success   = true,
                    When      = DateTime.Now
                });

                _files.Clear();
                UpdateQueueUi();
                SetStatus("Transfer tamamlandı", "ok");
            }
            else if (cancelled)
            {
                CompleteTransferCard(false, "İptal edildi");
                SetStatus("Transfer iptal edildi", "warn");
                AppendLog("Transfer iptal edildi.", "warn");
            }
            else
            {
                CompleteTransferCard(false, "Başarısız");
                AddHistory(new HistoryEntry
                {
                    Title     = snapshot.Count == 1 ? Path.GetFileName(snapshot[0]) : $"{snapshot.Count} dosya",
                    Detail    = result.Message,
                    Direction = HistoryDirection.Sent,
                    Success   = false,
                    When      = DateTime.Now
                });
                SetStatus("Transfer başarısız: " + result.Message, "err");
            }
        }

        private async Task SendFolderAsync(string folder)
        {
            if (!Directory.Exists(folder))
            {
                SetStatus("Klasör bulunamadı", "err");
                return;
            }

            if (!_isConnectedSession)
            {
                SetStatus("Klasör göndermek için önce bir cihaza bağlan", "warn");
                return;
            }

            if (_senderBusy)
            {
                AppendLog("Zaten bir aktarım sürüyor.", "warn");
                return;
            }

            _senderBusy = true;
            BtnCancelTransfer.IsEnabled = true;

            string name = new DirectoryInfo(folder).Name;
            BeginTransferCard(name, _connectedTargetName + " · klasör");
            SetStatus("Klasör taranıyor…", "accent");

            TransferResult result;
            try
            {
                result = await _sender.SendFolderAsync(folder, _connectedTargetIP, EffectivePort);
            }
            catch (Exception ex)
            {
                result = new TransferResult(false, ex.Message);
            }

            _senderBusy = false;
            BtnCancelTransfer.IsEnabled = false;

            if (result.Success)
            {
                CompleteTransferCard(true, "Klasör gönderildi");
                AddHistory(new HistoryEntry
                {
                    Title     = name,
                    Detail    = $"{_connectedTargetName} · klasör",
                    Direction = HistoryDirection.Sent,
                    Success   = true,
                    When      = DateTime.Now
                });
                SetStatus("Klasör gönderildi", "ok");
            }
            else if (result.Message.Contains("iptal", StringComparison.OrdinalIgnoreCase))
            {
                CompleteTransferCard(false, "İptal edildi");
                SetStatus("Klasör gönderimi iptal edildi", "warn");
            }
            else
            {
                CompleteTransferCard(false, "Başarısız");
                SetStatus("Klasör gönderilemedi: " + result.Message, "err");
            }
        }

        private void BeginTransferCard(string title, string detail)
        {
            _cardToken++;
            ActiveTransferCard.Visibility = Visibility.Visible;
            TxtActiveName.Text  = title;
            TxtActiveDetail.Text = detail;
            PbActive.Value = 0;
            TxtActivePercent.Text = "0%";
            TxtActiveSpeed.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
            TxtActiveSpeed.Text = "—";
        }

        private void ShowProgress(TransferProgress p, bool outgoing)
        {
            _cardToken++;
            ActiveTransferCard.Visibility = Visibility.Visible;

            TxtActiveName.Text = string.IsNullOrWhiteSpace(p.FileName)
                ? (outgoing ? "Gönderiliyor" : "Alınıyor")
                : p.FileName;

            TxtActiveDetail.Text = p.TotalFiles > 1
                ? $"{p.CurrentFileIndex}/{p.TotalFiles} · {FileTransferService.FormatSize(p.BytesSent)} / {FileTransferService.FormatSize(p.TotalBytes)}"
                : FileTransferService.FormatSize(p.TotalBytes);

            PbActive.Value = Math.Max(0, Math.Min(100, p.Percentage));
            TxtActivePercent.Text = $"{p.Percentage:0}%";
            TxtActiveSpeed.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
            TxtActiveSpeed.Text = p.SpeedMBps > 0.01 ? $"{p.SpeedMBps:0.0} MB/s" : "—";
        }

        private void ShowFlash(FlashProgressEventArgs p)
        {
            if (ActiveTransferCard.Visibility != Visibility.Visible) return;
            TxtActiveDetail.Text = $"Flash · {p.ActiveConnections} kanal";
        }

        private void CompleteTransferCard(bool ok, string message)
        {
            int token = ++_cardToken;

            TxtActivePercent.Text = ok ? "100%" : "—";
            if (ok) PbActive.Value = 100;

            TxtActiveSpeed.SetResourceReference(TextBlock.ForegroundProperty, ok ? "SuccessBrush" : "DangerBrush");
            TxtActiveSpeed.Text = ok ? "Tamamlandı" : message;
            TxtActiveDetail.Text = message;

            _ = HideCardLaterAsync(token);
        }

        private async Task HideCardLaterAsync(int token)
        {
            await Task.Delay(3200);
            if (token != _cardToken || _closing) return;

            ActiveTransferCard.Visibility = Visibility.Collapsed;
            PbActive.Value = 0;
        }

        private void BtnCancelTransfer_Click(object sender, RoutedEventArgs e)
        {
            if (!_senderBusy)
            {
                ActiveTransferCard.Visibility = Visibility.Collapsed;
                PbActive.Value = 0;
                return;
            }

            try { _sender.Cancel(); } catch { }
            SetStatus("İptal isteği gönderildi…", "warn");
        }

        // ═════════════════════════════════════════════════════════════════════
        //  OYUN TAŞIMA
        // ═════════════════════════════════════════════════════════════════════

        private void BtnGameTransfer_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("games");
            RbGames.IsChecked = true;
        }

        private async Task SendGameAsync(GameEntry game)
        {
            if (!_isConnectedSession)
            {
                SetStatus("Oyun göndermek için önce bir cihaza bağlan", "warn");
                return;
            }

            if (_senderBusy)
            {
                AppendLog("Zaten bir aktarım sürüyor.", "warn");
                return;
            }

            _senderBusy = true;
            BtnCancelTransfer.IsEnabled = true;

            BeginTransferCard(game.Name, _connectedTargetName + " · oyun");
            SetStatus("Oyun hazırlanıyor…", "accent");
            AppendLog($"{game.Name} gönderiliyor ({game.ProviderName})…");

            TransferResult result;
            try
            {
                result = await GameTransferService.SendAsync(
                    _sender, game, _connectedTargetIP, EffectivePort, game.SizeBytes);
            }
            catch (Exception ex)
            {
                result = new TransferResult(false, ex.Message);
            }

            _senderBusy = false;
            BtnCancelTransfer.IsEnabled = false;

            if (result.Success)
            {
                CompleteTransferCard(true, "Oyun gönderildi");
                AddHistory(new HistoryEntry
                {
                    Title     = game.Name,
                    Detail    = $"{_connectedTargetName} · {game.ProviderName}",
                    Direction = HistoryDirection.Sent,
                    Success   = true,
                    When      = DateTime.Now
                });
                SetStatus("Oyun gönderildi", "ok");
            }
            else if (result.Message.Contains("iptal", StringComparison.OrdinalIgnoreCase))
            {
                CompleteTransferCard(false, "İptal edildi");
                SetStatus("Oyun gönderimi iptal edildi", "warn");
            }
            else
            {
                CompleteTransferCard(false, "Başarısız");
                SetStatus("Oyun gönderilemedi", "err");
                AppendLog(result.Message, "err");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  MOBİL KÖPRÜ
        // ═════════════════════════════════════════════════════════════════════

        private void StartBridge()
        {
            try
            {
                _mobile.DeviceName = _settings.Hostname;
                _mobile.SaveFolder = EffectiveDownloadDir;
                _mobile.Start(MobileServer.DefaultPort);


                UpdateMobileUi();
                SetStatus("Mobil köprü açık", "ok");
                AppendLog($"Mobil köprü: {_mobile.BaseUrl}");
            }
            catch (Exception ex)
            {

                UpdateMobileUi();
                SetStatus("Mobil köprü açılamadı", "err");
                AppendLog("Mobil köprü hatası: " + ex.Message, "err");
            }
        }

        private void BtnToggleBridge_Click(object sender, RoutedEventArgs e)
        {
            if (_mobile.IsRunning)
            {
                _mobile.Stop();
                SetStatus("Mobil köprü kapatıldı", "info");
                AppendLog("Mobil köprü kapatıldı.");
            }
            else
            {
                StartBridge();
            }

            UpdateMobileUi();
            RefreshSessions();
        }

        private void UpdateMobileUi()
        {
            if (TxtBridgeState == null) return;

            bool on = _mobile.IsRunning;

            TxtStatusMobile.Text = on ? "Mobil köprü: açık" : "Mobil köprü: kapalı";

            TxtBridgeState.Text = on ? "Yayında" : "Kapalı";
            TxtBridgeState.SetResourceReference(TextBlock.ForegroundProperty, on ? "SuccessBrush" : "TextMutedBrush");
            BridgeDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, on ? "SuccessBrush" : "TextMutedBrush");

            BtnToggleBridge.Content = on ? "Köprüyü kapat" : "Köprüyü aç";
            BtnToggleBridge.Style = (Style)FindResource(on ? "SecondaryButton" : "PrimaryButton");

            TxtMobilePin.Text = on ? FormatPin(_mobile.Pin) : "— — —";

            if (on)
            {
                TxtMobileUrl.Text = _mobile.BaseUrl;
                TxtMobileIp.Text  = $"{_mobile.IpAddress} · port {_mobile.Port}";
                ImgQr.Source      = MakeQr(_mobile.BaseUrl);
            }
            else
            {
                TxtMobileUrl.Text = "http://—";
                TxtMobileIp.Text  = "Köprü kapalı — açınca adres oluşur";
                ImgQr.Source      = null;
            }
        }

        private void RefreshMobileCode()
        {
            if (TxtMobilePin == null) return;
            TxtMobilePin.Text = _mobile.IsRunning ? FormatPin(_mobile.Pin) : "— — —";
        }

        private void BtnNewPin_Click(object sender, RoutedEventArgs e)
        {
            if (!_mobile.IsRunning) StartBridge();

            if (_mobile.IsRunning)
            {
                _mobile.RefreshPin();
                UpdateMobileUi();
                SetStatus("Yeni bağlantı kodu üretildi", "ok");
            }
        }

        private void BtnCopyUrl_Click(object sender, RoutedEventArgs e)
        {
            if (!_mobile.IsRunning)
            {
                SetStatus("Köprü kapalı — adres yok", "warn");
                return;
            }

            CopyToClipboard(_mobile.BaseUrl, "Adres kopyalandı");
        }

        private void BtnCopyIp_Click(object sender, RoutedEventArgs e)
        {
            string ip = _mobile.IsRunning ? _mobile.IpAddress : NetworkManager.GetLocalIP();
            CopyToClipboard(ip, "IP kopyalandı");
        }

        private void BtnDisconnectMobile_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is MobileSession session)
            {
                _mobile.Disconnect(session);
                RefreshSessions();
                AppendLog($"Telefon bağlantısı kesildi: {session.Name}");
            }
        }

        private void RefreshSessions()
        {
            if (MobileSessionsList == null) return;

            var list = _mobile.Sessions;

            _sessions.Clear();
            foreach (var s in list) _sessions.Add(s);

            TxtMobileCount.Text = _sessions.Count.ToString();
            EmptyMobileSessions.Visibility = _sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnMobileFileReceived(SharedFile file)
        {
            AddHistory(new HistoryEntry
            {
                Title     = file.Name,
                Detail    = "Telefondan alındı · " + file.SizeText,
                Direction = HistoryDirection.Received,
                Success   = true,
                When      = DateTime.Now
            });

            AppendLog($"Telefondan dosya alındı: {file.Name} ({file.SizeText})", "ok");
            SetStatus($"Telefondan alındı: {file.Name}", "ok");
        }

        private void OnMobileTransfer(MobileTransfer t)
        {
            // Köprü her parçada haber verir — arayüzü saniyede bir tazele.
            var now = DateTime.UtcNow;
            if (now - _lastMobileUiTick < TimeSpan.FromMilliseconds(600)) return;
            _lastMobileUiTick = now;

            if (t.Active)
            {
                string dir = t.Direction == "down" ? "Telefona gönderiliyor" : "Telefondan alınıyor";
                SetStatus($"{dir}: {t.Name} · {t.Percent}%", "accent");
            }
        }

        private void BtnAddShared_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Telefona açılacak dosyalar",
                Multiselect = true
            };

            if (dlg.ShowDialog() != true) return;

            foreach (var path in dlg.FileNames)
            {
                var file = _mobile.AddSharedFile(path);
                if (file != null) _shared.Add(file);
            }

            UpdateSharedUi();
            SetStatus($"{_shared.Count} dosya telefona açık", "ok");
        }

        private void BtnRemoveShared_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is SharedFile file)
            {
                _mobile.RemoveSharedFile(file.Id);
                _shared.Remove(file);
                UpdateSharedUi();
            }
        }

        private void UpdateSharedUi()
        {
            if (SharedFilesList == null) return;

            TxtSharedCount.Text = _shared.Count == 0 ? "0 dosya" : $"{_shared.Count} dosya";
            EmptyShared.Visibility = _shared.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  GEÇMİŞ
        // ═════════════════════════════════════════════════════════════════════

        private void LoadHistoryIntoView()
        {
            if (HistoryList == null) return;

            _history.Clear();
            foreach (var entry in HistoryService.Load())
                _history.Add(entry);

            UpdateHistoryUi();
        }

        private void AddHistory(HistoryEntry entry)
        {
            HistoryService.Add(entry);

            _history.Insert(0, entry);
            if (_history.Count > 250) _history.RemoveAt(_history.Count - 1);

            UpdateHistoryUi();
        }

        private void UpdateHistoryUi()
        {
            if (EmptyHistory == null) return;
            EmptyHistory.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (BtnClearHistory != null) BtnClearHistory.IsEnabled = _history.Count > 0;
        }

        private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
        {
            if (_history.Count == 0) return;

            if (MessageBox.Show("Transfer geçmişi silinsin mi?", "Geçmişi temizle",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            HistoryService.Clear();
            _history.Clear();
            UpdateHistoryUi();
            SetStatus("Geçmiş temizlendi", "info");
        }

        // ═════════════════════════════════════════════════════════════════════
        //  TEMA
        // ═════════════════════════════════════════════════════════════════════

        private void BuildThemeGallery()
        {
            if (ThemeGallery == null) return;

            ThemeGallery.Children.Clear();

            var current = ThemeManager.Normalize(_settings.Theme);
            var tileStyle = (Style)FindResource("ThemeTile");

            foreach (var palette in ThemeManager.Palettes)
            {
                var tile = new RadioButton
                {
                    GroupName = "theme",
                    Style     = tileStyle,
                    Tag       = palette.Name,
                    ToolTip   = palette.Description,
                    Content   = BuildThemeSwatch(palette),
                    IsChecked = palette.Id == current
                };

                System.Windows.Automation.AutomationProperties.SetName(tile, palette.Name);
                System.Windows.Automation.AutomationProperties.SetHelpText(tile, palette.Description);
                System.Windows.Automation.AutomationProperties.SetAutomationId(tile, "RbTheme_" + palette.Id);

                var captured = palette;
                tile.Checked += (_, _) => ApplyTheme(captured);
                ThemeGallery.Children.Add(tile);
            }

            TxtActiveTheme.Text = ThemeManager.Find(current).Name;
        }

        private static FrameworkElement BuildThemeSwatch(ThemePalette p)
        {
            var surface = new LinearGradientBrush(p.Background, p.Surface, new Point(0, 0), new Point(1, 1));
            var accent  = new LinearGradientBrush(p.Accent, p.Accent2, new Point(0, 0), new Point(1, 1));

            var host = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background   = surface,
                BorderBrush  = new SolidColorBrush(p.Border),
                BorderThickness = new Thickness(1)
            };

            var chip = new Border
            {
                Width  = 40,
                Height = 22,
                CornerRadius = new CornerRadius(6),
                Background = accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };

            host.Child = chip;
            return host;
        }

        private void ApplyTheme(ThemePalette palette)
        {
            if (palette == null) return;

            ThemeManager.Apply(palette);

            _settings.Theme = palette.Id;
            SettingsService.Save(_settings);

            if (TxtActiveTheme != null) TxtActiveTheme.Text = palette.Name;
            AppendLog("Tema: " + palette.Name);
        }

        private void BtnThemeCycle_Click(object sender, RoutedEventArgs e)
        {
            var next = ThemeManager.Next(ThemeManager.Current);
            if (next == null) return;

            ApplyTheme(next);

            // Galerideki seçimi de kaydır
            foreach (var child in ThemeGallery.Children)
            {
                if (child is RadioButton rb && string.Equals(rb.Tag as string, next.Name, StringComparison.Ordinal))
                {
                    rb.IsChecked = true;
                    break;
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  AYARLAR
        // ═════════════════════════════════════════════════════════════════════

        private void LoadSettingsToUI()
        {
            _suppressSettings = true;

            TxtHostname.Text      = _settings.Hostname;
            TxtPort.Text          = EffectivePort.ToString();
            TxtDownloadFolder.Text = EffectiveDownloadDir;

            ChkAutoReceive.IsChecked = _settings.AutoReceive;
            ChkFlashMode.IsChecked   = _settings.UseFlashMode;
            ChkCheckUpdates.IsChecked = _settings.CheckUpdates;
            ChkAutoUpdate.IsChecked   = _settings.AutoUpdate;

            SliFlashConns.Value   = Math.Max(2, Math.Min(32, _settings.FlashConnections));
            TxtFlashConns.Text    = ((int)SliFlashConns.Value).ToString();

            TxtActiveTheme.Text = ThemeManager.Find(_settings.Theme).Name;

            var version = UpdateService.GetCurrentVersion();
            string shortVersion = $"v{version.Major}.{version.Minor}";

            TxtAppVersion.Text    = $"Dosyaktar v{version.Major}.{version.Minor}.{version.Build}";
            TxtStatusVersion.Text = shortVersion;
            TxtTitleVersion.Text  = shortVersion;

            _suppressSettings = false;
        }

        private void UpdateIdentity()
        {
            string name = _settings.Hostname;
            TxtAvatar.Text = string.IsNullOrEmpty(name) ? "D" : name.Substring(0, 1).ToUpperInvariant();
        }

        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (!_initialized || _suppressSettings) return;
            PersistSettingsFromUi();
        }

        private void PersistSettingsFromUi()
        {
            string host = (TxtHostname.Text ?? "").Trim();
            if (host.Length == 0) host = Environment.MachineName;
            if (host.Length > 32) host = host.Substring(0, 32);

            bool hostChanged = !string.Equals(host, _settings.Hostname, StringComparison.Ordinal);
            _settings.Hostname = host;
            _discovery.DeviceName = host;
            _mobile.DeviceName    = host;

            int port = EffectivePort;
            if (int.TryParse((TxtPort.Text ?? "").Trim(), out int parsed) && parsed is > 0 and < 65536)
                port = parsed;
            else
                TxtPort.Text = EffectivePort.ToString();

            bool portChanged = port != _settings.Port;
            _settings.Port = port;

            string folder = (TxtDownloadFolder.Text ?? "").Trim();
            if (Directory.Exists(folder)) _settings.SaveDirectory = folder;
            else TxtDownloadFolder.Text = EffectiveDownloadDir;

            _settings.AutoReceive = ChkAutoReceive.IsChecked == true;
            _settings.UseFlashMode = ChkFlashMode.IsChecked == true;
            _settings.CheckUpdates = ChkCheckUpdates.IsChecked == true;
            _settings.AutoUpdate  = ChkAutoUpdate.IsChecked == true;

            _settings.FlashConnections = (int)SliFlashConns.Value;

            _mobile.SaveFolder = EffectiveDownloadDir;

            SettingsService.Save(_settings);

            UpdateIdentity();
            UpdateMobileUi();

            if (hostChanged) StartDiscovery();
            if (portChanged) { StartDiscovery(); AppendLog($"Port {port} olarak güncellendi."); }

            if (_isConnectedSession)
            {
                if (_settings.AutoReceive && !_receiving) StartReceiving();
                if (!_settings.AutoReceive && _receiving) StopReceiving();

                TxtSessPort.Text = EffectivePort.ToString();
                TxtSessMode.Text = _settings.UseFlashMode
                    ? $"Flash · {_settings.FlashConnections} kanal"
                    : "Standart";
            }

            SetStatus("Ayarlar kaydedildi", "ok");
        }

        private void SliFlashConns_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtFlashConns != null)
                TxtFlashConns.Text = ((int)e.NewValue).ToString();

            if (!_initialized || _suppressSettings) return;

            _settings.FlashConnections = (int)e.NewValue;
            SettingsService.Save(_settings);
        }

        private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Kayıt klasörünü seç",
                InitialDirectory = Directory.Exists(TxtDownloadFolder.Text)
                    ? TxtDownloadFolder.Text
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            if (dlg.ShowDialog() == true && Directory.Exists(dlg.FolderName))
            {
                TxtDownloadFolder.Text = dlg.FolderName;
                PersistSettingsFromUi();
            }
        }

        private void BtnOpenDownloadFolder_Click(object sender, RoutedEventArgs e)
        {
            string dir = EffectiveDownloadDir;

            try
            {
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetStatus("Klasör açılamadı: " + ex.Message, "err");
            }
        }

        private void BtnResetApp_Click(object sender, RoutedEventArgs e)
        {
            var answer = MessageBox.Show(
                "Tüm ayarlar, geçmiş ve kayıtlı hedefler silinecek. Uygulama yeniden başlatılacak.\n\nOnaylıyor musunuz?",
                "Sıfırla",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes) return;

            try
            {
                SettingsService.Save(new AppSettings());
                HistoryService.Clear();
            }
            catch { }

            try
            {
                string? exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            }
            catch { }

            Close();
        }

        // ═════════════════════════════════════════════════════════════════════
        //  GÜNCELLEME
        // ═════════════════════════════════════════════════════════════════════

        private async Task CheckUpdatesOnStartAsync()
        {
            if (!_settings.CheckUpdates) return;

            await Task.Delay(2200);
            if (_closing) return;

            await RunUpdateCheckAsync(silentAuto: _settings.AutoUpdate);
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn) btn.IsEnabled = false;
            try { await RunUpdateCheckAsync(silentAuto: false); }
            finally { if (sender is Button b) b.IsEnabled = true; }
        }

        private async Task RunUpdateCheckAsync(bool silentAuto)
        {
            var svc = new UpdateService();

            svc.StatusChanged += (_, msg) => Dispatcher.InvokeAsync(() => AppendLog("[Güncelleme] " + msg));

            try
            {
                var info = await svc.CheckAndMaybeAutoUpdateAsync(silent: silentAuto);

                if (info != null)
                {
                    await Dispatcher.InvokeAsync(() =>
                        new UpdateWindow(info) { Owner = this }.ShowDialog());
                }
                else if (!silentAuto)
                {
                    SetStatus("Sürüm güncel", "ok");
                }
            }
            catch (Exception ex)
            {
                AppendLog("[Güncelleme] " + ex.Message, "err");
                if (!silentAuto) SetStatus("Güncelleme kontrolü başarısız", "err");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  YARDIMCILAR
        // ═════════════════════════════════════════════════════════════════════

        private void UpdateNetworkInfo()
        {
            try
            {
                string ip = NetworkManager.GetLocalIP();

                if (string.IsNullOrEmpty(ip) || ip == "127.0.0.1")
                {
                    TxtTitleNet.Text  = "Ağ yok";
                    TxtStatusNet.Text = "Bağlantı yok";
                    TxtTitleNet.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
                    return;
                }

                string kind = "Ethernet";
                var nic = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                                         n.GetIPProperties().UnicastAddresses
                                             .Any(a => a.Address.ToString() == ip));

                if (nic != null && nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    kind = "Wi-Fi";

                TxtTitleNet.Text  = kind;
                TxtStatusNet.Text = $"{kind} · {ip}";
                TxtTitleNet.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
            }
            catch
            {
                TxtTitleNet.Text  = "Ağ";
                TxtStatusNet.Text = "Tespit edilemedi";
            }
        }

        private void SetStatus(string text, string tone = "info")
        {
            if (TxtStatus == null) return;

            TxtStatus.Text = text;

            string brush = tone switch
            {
                "ok"     => "SuccessBrush",
                "warn"   => "WarningBrush",
                "err"    => "DangerBrush",
                "accent" => "AccentBrush",
                _        => "TextMutedBrush"
            };

            StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
        }

        private void AppendLog(string message, string kind = "info")
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(() => AppendLog(message, kind));
                return;
            }

            if (_logs.Count > 140) _logs.RemoveAt(_logs.Count - 1);

            _logs.Insert(0, new LogEntry
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Text = message,
                Kind = kind
            });

            LogScroll?.ScrollToTop();
        }

        private void StartScanPulse()
        {
            if (ScanRing == null) return;

            var anim = new DoubleAnimation
            {
                From = 0.18,
                To   = 0.75,
                Duration = TimeSpan.FromMilliseconds(1500),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            ScanRing.BeginAnimation(OpacityProperty, anim);
        }

        private static BitmapImage? MakeQr(string text)
        {
            try
            {
                using var generator = new QRCodeGenerator();
                using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
                byte[] png = new PngByteQRCode(data).GetGraphic(10);

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption  = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(png);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }

        private static string FormatPin(string pin) =>
            pin.Length == 6 ? pin.Substring(0, 3) + " " + pin.Substring(3) : pin;

        private void CopyToClipboard(string text, string okMessage)
        {
            try
            {
                Clipboard.SetText(text);
                SetStatus(okMessage, "ok");
            }
            catch (Exception ex)
            {
                SetStatus("Panoya kopyalanamadı: " + ex.Message, "err");
            }
        }
    }
}
