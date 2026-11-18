using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dosyaktar.Helpers;
using Dosyaktar.Services;

namespace Dosyaktar.Views
{
    /// <summary>
    /// Oyun kütüphanesi sayfası: sağlayıcıları tarar, filtreler, seçilen oyunu
    /// gönderilmek üzere MainWindow'a bildirir.
    /// </summary>
    public partial class GameLibraryView : UserControl
    {
        private readonly GameLibraryService _library = new();
        private readonly List<GameEntry> _all = new();

        private CancellationTokenSource? _scanCts;
        private bool _ready;
        private bool _canSend;
        private int _filteredCount;

        /// <summary>Gönder butonuna basıldığında seçili oyunu taşır.</summary>
        public event EventHandler<GameEntry>? SendRequested;

        public GameLibraryView()
        {
            InitializeComponent();

            _library.Status += OnLibraryStatus;

            TxtSummary.Text = "—";
            TxtWarn.Visibility = Visibility.Collapsed;
            SizeProgress.Visibility = Visibility.Collapsed;
            TxtSizeProgress.Visibility = Visibility.Collapsed;

            _ready = true;
            ShowDetail(null);
        }

        // ═════════════════════════════════════════════════════════════════
        //  Dışarıya açık yüzey
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Bağlantı durumuna göre "Gönder" butonunu açar/kapatır.</summary>
        public void SetSendAvailable(bool canSend)
        {
            _canSend = canSend;
            UpdateSendEnabled();
        }

        /// <summary>Arama kutusuna odaklanır (Ctrl+F).</summary>
        public void FocusSearch()
        {
            TxtSearch.Focus();
            TxtSearch.SelectAll();
        }

        /// <summary>Kütüphaneleri yeniden tarar.</summary>
        public async Task RefreshAsync()
        {
            if (!_ready || _library.IsScanning) return;

            _scanCts?.Dispose();
            _scanCts = new CancellationTokenSource();
            var ct = _scanCts.Token;

            TxtWarn.Visibility = Visibility.Collapsed;
            BtnRefresh.IsEnabled = false;
            TxtScanStatus.Text = "Oyun kütüphaneleri taranıyor…";
            TxtSummary.Text = "Taranıyor…";
            UpdateViewState();

            try
            {
                var games = await _library.ScanAsync(ct);
                if (ct.IsCancellationRequested) return;

                _all.Clear();
                _all.AddRange(games);

                // Kapaklar: yerel dosyalar hemen, eksikler ağdan kademeli.
                var snapshot = _all.ToList();
                _ = HydrateCoversAsync(snapshot, ct);

                ApplyFilter(keepSelection: false);
                _ = MeasureMissingAsync(ct);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ShowWarn($"Tarama tamamlanamadı: {ex.Message}");
            }
            finally
            {
                BtnRefresh.IsEnabled = true;
                UpdateViewState();
            }
        }

        // ═════════════════════════════════════════════════════════════════
        //  Kapaklar — sağlayıcıdan bağımsız, önbellekli
        // ═════════════════════════════════════════════════════════════════

        private async Task HydrateCoversAsync(IReadOnlyList<GameEntry> games, CancellationToken ct)
        {
            // 1) Yerelde hazır kapaklar (Steam librarycache, klasör ikonu).
            await Task.Run(() =>
            {
                foreach (var game in games)
                {
                    if (ct.IsCancellationRequested) return;
                    game.LoadCover();
                }
            }, ct).ConfigureAwait(true);

            // 2) Eksikler için herkese açık vitrin görseli; kart başına akar.
            foreach (var game in games)
            {
                if (ct.IsCancellationRequested) return;
                if (game.Cover != null) continue;

                try
                {
                    string? path = await GameCoverService.EnsureCoverAsync(game, ct);
                    if (path == null) continue;

                    var image = await Task.Run(() => GameEntry.DecodeCover(path), ct);
                    if (image == null) continue;

                    game.CoverPath = path;
                    game.Cover = image;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // Kapak süsüdür; eşleşmeyen oyun rozetle kalır.
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════
        //  Yaşam döngüsü
        // ═════════════════════════════════════════════════════════════════

        private void OnLibraryStatus(object? sender, string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnLibraryStatus(sender, message)));
                return;
            }

            TxtScanStatus.Text = message;
        }

        // ═════════════════════════════════════════════════════════════════
        //  Tarama / boyut ölçümü
        // ═════════════════════════════════════════════════════════════════

        private async Task MeasureMissingAsync(CancellationToken ct)
        {
            int missing = _all.Count(g => g.SizeBytes <= 0 && Directory.Exists(g.InstallPath));
            if (missing == 0) return;

            SizeProgress.Visibility = Visibility.Visible;
            TxtSizeProgress.Visibility = Visibility.Visible;
            SizeProgress.Minimum = 0;
            SizeProgress.Maximum = missing;
            SizeProgress.Value = 0;

            int done = 0;
            var progress = new Progress<GameEntry>(_ =>
            {
                done++;
                SizeProgress.Value = Math.Min(done, missing);
                TxtSizeProgress.Text = $"Boyutlar hesaplanıyor… {done}/{missing}";
                UpdateSummary();
            });

            try
            {
                await _library.SizeMissingAsync(_all, progress, ct);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                    SizeProgress.Visibility = Visibility.Collapsed;
                    TxtSizeProgress.Visibility = Visibility.Collapsed;
                    UpdateSummary();
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════
        //  Filtre / görünüm durumu
        // ═════════════════════════════════════════════════════════════════

        private void ApplyFilter(bool keepSelection = true)
        {
            if (!_ready) return;

            string query = TxtSearch.Text.Trim();
            string provider = SelectedProviderKey();

            IEnumerable<GameEntry> queryable = _all;
            if (provider != "all")
                queryable = queryable.Where(g => g.ProviderId == provider);
            if (query.Length > 0)
                queryable = queryable.Where(g =>
                    g.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    g.FolderName.Contains(query, StringComparison.CurrentCultureIgnoreCase));

            var filtered = queryable.ToList();
            _filteredCount = filtered.Count;

            var previous = keepSelection ? GameList.SelectedItem as GameEntry : null;
            GameList.ItemsSource = filtered;

            if (previous != null && filtered.Contains(previous))
                GameList.SelectedItem = previous;
            else
                ShowDetail(null);

            UpdateSummary();
            UpdateViewState();
        }

        private void UpdateViewState()
        {
            if (!_ready) return;

            if (_library.IsScanning)
            {
                ScanState.Visibility = Visibility.Visible;
                GameList.Visibility = Visibility.Collapsed;
                EmptyState.Visibility = Visibility.Collapsed;
                NoResults.Visibility = Visibility.Collapsed;
                return;
            }

            ScanState.Visibility = Visibility.Collapsed;

            if (_all.Count == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
                NoResults.Visibility = Visibility.Collapsed;
                GameList.Visibility = Visibility.Collapsed;
            }
            else if (_filteredCount == 0)
            {
                EmptyState.Visibility = Visibility.Collapsed;
                NoResults.Visibility = Visibility.Visible;
                GameList.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyState.Visibility = Visibility.Collapsed;
                NoResults.Visibility = Visibility.Collapsed;
                GameList.Visibility = Visibility.Visible;
            }
        }

        private void UpdateSummary()
        {
            if (_all.Count == 0)
            {
                TxtSummary.Text = "—";
                return;
            }

            int providers = _all.Select(g => g.ProviderId).Distinct().Count();
            long total = _all.Sum(g => g.SizeBytes);

            TxtSummary.Text = total > 0
                ? $"{_all.Count} oyun · {providers} sağlayıcı · {FileTransferService.FormatSize(total)}"
                : $"{_all.Count} oyun · {providers} sağlayıcı";
        }

        private string SelectedProviderKey()
        {
            var chips = new[]
            {
                ChipAll, ChipSteam, ChipEpic, ChipGog, ChipBattleNet,
                ChipUbisoft, ChipEa, ChipRiot, ChipFolder
            };

            foreach (var chip in chips)
            {
                if (chip != null && chip.IsChecked == true)
                    return chip.Tag as string ?? "all";
            }

            return "all";
        }

        // ═════════════════════════════════════════════════════════════════
        //  Detay paneli
        // ═════════════════════════════════════════════════════════════════

        private void ShowDetail(GameEntry? game)
        {
            if (!_ready) return;

            if (game == null)
            {
                DetailEmpty.Visibility = Visibility.Visible;
                DetailFilled.Visibility = Visibility.Collapsed;
                DetailFilled.DataContext = null;
                DetailProviderDot.Visibility = Visibility.Collapsed;
                UpdateSendEnabled();
                return;
            }

            game.LoadCover();

            DetailFilled.DataContext = game;
            DetailEmpty.Visibility = Visibility.Collapsed;
            DetailFilled.Visibility = Visibility.Visible;

            DetailProviderDot.Visibility = Visibility.Visible;
            DetailProviderDot.SetResourceReference(Border.BackgroundProperty, game.ProviderDotKey);

            DetailCoverFallback.SetResourceReference(Border.BackgroundProperty, game.ProviderTintKey);
            TxtDetailInitial.Text = game.Initial;

            TxtDetailProvider.Text = $"{game.ProviderName} · {game.FolderName}";

            TxtDetailNote.Text = game.Provider switch
            {
                GameProvider.Steam => "Gönderilen oyun, alıcıda steamapps/common içine yerleştirilir ve manifesti Steam kütüphanesine yazılır.",
                GameProvider.Epic => "Oyun klasörü kopyalanır; Epic Launcher'da 'Doğrula' ile kayıt tamamlanabilir.",
                GameProvider.Folder => "Klasör olduğu gibi gönderilir; alıcı taraf onu indirme klasöründe bulur.",
                _ => "Oyun klasörü olduğu gibi gönderilir; alıcıda başlatıcıya manuel eklenmesi gerekebilir."
            };
            TxtDetailNote.Visibility = Visibility.Visible;

            BtnLaunchGame.IsEnabled = true;
            BtnOpenGameFolder.IsEnabled = Directory.Exists(game.InstallPath);

            UpdateSendEnabled();
        }

        private void UpdateSendEnabled()
        {
            BtnSendGame.IsEnabled = _ready && _canSend && GameList.SelectedItem is GameEntry;
        }

        private void ShowWarn(string message)
        {
            if (TxtWarn.Child is TextBlock text) text.Text = message;
            TxtWarn.Visibility = Visibility.Visible;
        }

        // ═════════════════════════════════════════════════════════════════
        //  Olay işleyicileri (XAML)
        // ═════════════════════════════════════════════════════════════════

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            TxtWarn.Visibility = Visibility.Collapsed;
            await RefreshAsync();
        }

        private async void BtnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var owner = Window.GetWindow(this);
                string? path = FolderPicker.ShowDialog(owner!, "Oyun klasörü seç");
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

                var rows = CustomGameStore.Load();
                if (!rows.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    string name = Path.GetFileName(path.TrimEnd('\\', '/'));
                    rows.Add((string.IsNullOrWhiteSpace(name) ? path : name, path));
                    CustomGameStore.Save(rows);
                }

                await RefreshAsync();
            }
            catch (Exception ex)
            {
                ShowWarn($"Klasör eklenemedi: {ex.Message}");
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
            => ApplyFilter();

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            if (GameList.Items.Count > 0)
            {
                GameList.SelectedIndex = 0;
                GameList.ScrollIntoView(GameList.SelectedItem);
                GameList.Focus();
            }

            e.Handled = true;
        }

        private void Provider_Checked(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            ApplyFilter(keepSelection: false);
        }

        private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            TxtSearch.Text = string.Empty;
            ChipAll.IsChecked = true;
            ApplyFilter(keepSelection: false);
            TxtSearch.Focus();
        }

        private void GameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => ShowDetail(GameList.SelectedItem as GameEntry);

        private void BtnSendGame_Click(object sender, RoutedEventArgs e)
        {
            if (GameList.SelectedItem is not GameEntry game) return;

            if (!_canSend)
            {
                ShowWarn("Göndermek için önce bir cihaza bağlan.");
                return;
            }

            TxtWarn.Visibility = Visibility.Collapsed;
            SendRequested?.Invoke(this, game);
        }

        private void BtnLaunchGame_Click(object sender, RoutedEventArgs e)
        {
            if (GameList.SelectedItem is not GameEntry game) return;

            if (!GameLauncher.Launch(game, out string? error))
                ShowWarn(error ?? "Oyun başlatılamadı.");
        }

        private void BtnOpenGameFolder_Click(object sender, RoutedEventArgs e)
        {
            if (GameList.SelectedItem is GameEntry game)
                GameLauncher.OpenFolder(game);
        }
    }
}
