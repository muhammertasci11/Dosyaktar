using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Dosyaktar.Services;

namespace Dosyaktar
{
    public partial class UpdateWindow : Window
    {
        private readonly UpdateInfo               _info;
        private readonly UpdateService            _svc = new();
        private          CancellationTokenSource? _cts;
        private          bool                     _downloading;

        public UpdateWindow(UpdateInfo info)
        {
            InitializeComponent();
            _info = info;

            string current = UpdateService.GetCurrentVersion().ToString();
            TxtVersionInfo.Text = $"Mevcut: v{current}   →   Yeni: {info.TagName}";

            string notes = string.IsNullOrWhiteSpace(info.ReleaseNotes)
                ? "Bu sürümde çeşitli iyileştirmeler ve hata düzeltmeleri yapıldı."
                : info.ReleaseNotes;
            TxtReleaseNotes.Text = notes;
        }

        private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_downloading) return;
            _downloading = true;

            BtnUpdate.IsEnabled      = false;
            BtnSkip.IsEnabled        = false;
            BtnUpdate.Content        = "İndiriliyor...";
            ProgressPanel.Visibility = Visibility.Visible;

            _cts = new CancellationTokenSource();
            var progress = new Progress<int>(p =>
            {
                PbDownload.Value  = p;
                TxtDlPercent.Text = $"{p}%";
            });

            try
            {
                string newExe = await _svc.DownloadUpdateAsync(_info, progress, _cts.Token);
                BtnUpdate.Content = "Yükleniyor...";
                await Task.Delay(500);
                UpdateService.ApplyUpdate(newExe);
            }
            catch (OperationCanceledException)
            {
                ResetButtons("İptal edildi.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"İndirme başarısız:\n{ex.Message}",
                    "Güncelleme Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                ResetButtons("Güncelle");
            }
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            Close();
        }

        private void ResetButtons(string btnText)
        {
            _downloading        = false;
            BtnUpdate.IsEnabled = true;
            BtnSkip.IsEnabled   = true;
            BtnUpdate.Content   = btnText;
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            base.OnClosed(e);
        }
    }
}
