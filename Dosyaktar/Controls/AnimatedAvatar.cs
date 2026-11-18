using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Dosyaktar.Services;

namespace Dosyaktar.Controls
{
    /// <summary>
    /// Animasyonlu avatar yüzeyi. 32 tariften birini çizer; göz kırpma, süzülme,
    /// dönme gibi hareketler tek bir paylaşılan saatten beslenir. Sistemde
    /// "animasyonları göster" kapalıysa sabit kare çizilir.
    /// </summary>
    public sealed class AnimatedAvatar : FrameworkElement
    {
        public static readonly DependencyProperty AvatarIdProperty =
            DependencyProperty.Register(
                nameof(AvatarId),
                typeof(string),
                typeof(AnimatedAvatar),
                new FrameworkPropertyMetadata(
                    AvatarCatalog.DefaultId,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    OnAvatarIdChanged));

        public string AvatarId
        {
            get => (string)GetValue(AvatarIdProperty);
            set => SetValue(AvatarIdProperty, value);
        }

        // ── paylaşılan animasyon saati ────────────────────────────────────────
        private static readonly List<AnimatedAvatar> Live = new();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static double _lastFrameMs = -1000;
        private static bool _hooked;

        private static bool Motion => SystemParameters.ClientAreaAnimation;

        static AnimatedAvatar()
        {
            SystemParameters.StaticPropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;
                foreach (var avatar in Live.ToArray())
                    avatar.InvalidateVisual();
            };
        }

        public AnimatedAvatar()
        {
            SnapsToDevicePixels = true;
            IsHitTestVisible = false;
            Loaded   += (_, _) => Register(this);
            Unloaded += (_, _) => Unregister(this);
        }

        private static void OnAvatarIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AnimatedAvatar avatar && avatar.IsLoaded)
                avatar.InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double w = double.IsNaN(Width) ? 64 : Width;
            double h = double.IsNaN(Height) ? 64 : Height;
            return new Size(w, h);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 1) return;

            var spec = AvatarCatalog.Find(AvatarId);
            double t = Motion ? Clock.Elapsed.TotalSeconds : 0;

            dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
            dc.PushTransform(new ScaleTransform(size / 100.0, size / 100.0));
            dc.PushClip(new EllipseGeometry(new Point(50, 50), 50, 50));
            AvatarArt.Draw(dc, spec, t, still: !Motion);
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        private static void Register(AnimatedAvatar avatar)
        {
            if (!Live.Contains(avatar)) Live.Add(avatar);

            if (_hooked) return;
            CompositionTarget.Rendering += OnFrame;
            _hooked = true;
        }

        private static void Unregister(AnimatedAvatar avatar)
        {
            Live.Remove(avatar);

            if (Live.Count > 0 || !_hooked) return;
            CompositionTarget.Rendering -= OnFrame;
            _hooked = false;
        }

        private static void OnFrame(object? sender, EventArgs e)
        {
            // ~30 kare/sn: 32 avatar aynı anda görünse bile çizim yükü sınırlı kalır.
            double now = Clock.Elapsed.TotalMilliseconds;
            if (now - _lastFrameMs < 33) return;
            _lastFrameMs = now;

            if (!Motion) return;

            for (int i = Live.Count - 1; i >= 0; i--)
            {
                var avatar = Live[i];
                if (avatar.IsVisible)
                    avatar.InvalidateVisual();
            }
        }
    }
}
