using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Dosyaktar.Controls
{
    /// <summary>
    /// Kenar çubuğu sekmesi: vektör simgeyi taşır, üzerine gelince yaylanır,
    /// seçiliyken hafifçe nefes alır. Tüm hareketler sistemin "animasyonları
    /// göster" tercihine bağlıdır; kapalıysa yalnızca durum renkleri değişir.
    /// </summary>
    public class NavTabButton : RadioButton
    {
        public static readonly DependencyProperty IconDataProperty =
            DependencyProperty.Register(
                nameof(IconData),
                typeof(Geometry),
                typeof(NavTabButton),
                new PropertyMetadata(null));

        /// <summary>24×24 kutuya çizilmiş vektör simge geometrisi.</summary>
        public Geometry? IconData
        {
            get => (Geometry?)GetValue(IconDataProperty);
            set => SetValue(IconDataProperty, value);
        }

        private FrameworkElement? _iconHost;
        private Border? _glow;
        private ScaleTransform? _scale;
        private TranslateTransform? _shift;

        private const double RestScale = 1.0;
        private const double HoverScale = 1.16;
        private const double ActiveScale = 1.07;
        private const double HoverLift = -1.6;

        private static bool Motion => SystemParameters.ClientAreaAnimation;

        public override void OnApplyTemplate()
        {
            // Şablon yeniden uygulanırsa eski animasyonlar serbest kalsın.
            StopPulse();

            base.OnApplyTemplate();

            _iconHost = GetTemplateChild("IconHost") as FrameworkElement;
            _glow = GetTemplateChild("IconGlow") as Border;

            if (_iconHost != null)
            {
                _scale = new ScaleTransform(1, 1);
                _shift = new TranslateTransform(0, 0);

                var group = new TransformGroup();
                group.Children.Add(_scale);
                group.Children.Add(_shift);

                _iconHost.RenderTransform = group;
                _iconHost.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            ApplyRest(instant: true);
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);

            if (!Motion || _iconHost == null) return;

            AnimateScale(HoverScale, 150);
            AnimateLift(IsChecked == true ? 0 : HoverLift, 170);

            if (IsChecked == true)
            {
                if (_glow != null)
                    _glow.BeginAnimation(UIElement.OpacityProperty,
                        Fade(0.82, 180));
            }
            else
            {
                if (_glow != null)
                    _glow.BeginAnimation(UIElement.OpacityProperty,
                        Fade(0.18, 180));
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            ApplyRest(instant: false);
        }

        protected override void OnChecked(RoutedEventArgs e)
        {
            base.OnChecked(e);
            ApplyRest(instant: false);
        }

        protected override void OnUnchecked(RoutedEventArgs e)
        {
            base.OnUnchecked(e);
            ApplyRest(instant: false);
        }

        private void ApplyRest(bool instant)
        {
            if (_iconHost == null) return;

            bool active = IsChecked == true;

            if (!Motion)
            {
                StopPulse();
                if (_scale != null)
                {
                    _scale.ScaleX = active ? ActiveScale : RestScale;
                    _scale.ScaleY = active ? ActiveScale : RestScale;
                }
                if (_shift != null) _shift.Y = 0;
                if (_glow != null) _glow.Opacity = active ? 0.55 : 0;
                return;
            }

            if (active)
            {
                AnimateScale(ActiveScale, instant ? 0 : 200);
                StartPulse();
            }
            else
            {
                StopPulse();
                AnimateScale(RestScale, instant ? 0 : 200);
                AnimateLift(0, instant ? 0 : 200);
                if (_glow != null)
                    _glow.BeginAnimation(UIElement.OpacityProperty, Fade(0, instant ? 0 : 180));
            }
        }

        /// <summary>Seçili sekmede yumuşak parlama + hafif süzülme.</summary>
        private void StartPulse()
        {
            if (_glow == null || _shift == null) return;

            var pulse = new DoubleAnimation(0.42, 0.8, TimeSpan.FromSeconds(2.4))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            _glow.BeginAnimation(UIElement.OpacityProperty, pulse);

            var floatAnim = new DoubleAnimation(0, -1.7, TimeSpan.FromSeconds(2.9))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            _shift.BeginAnimation(TranslateTransform.YProperty, floatAnim);
        }

        private void StopPulse()
        {
            _glow?.BeginAnimation(UIElement.OpacityProperty, null);
            _shift?.BeginAnimation(TranslateTransform.YProperty, null);
        }

        private void AnimateScale(double value, int ms)
        {
            if (_scale == null) return;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var anim = new DoubleAnimation(value, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = ease,
                FillBehavior = FillBehavior.HoldEnd
            };

            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        private void AnimateLift(double value, int ms)
        {
            if (_shift == null) return;

            _shift.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(value, TimeSpan.FromMilliseconds(ms))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.HoldEnd
                });
        }

        private static DoubleAnimation Fade(double to, int ms)
            => new(to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
    }
}
