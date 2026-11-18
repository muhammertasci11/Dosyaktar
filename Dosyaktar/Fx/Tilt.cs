using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dosyaktar.Fx
{
    /// <summary>
    /// Pointer-reactive depth for small surfaces. The element leans away from
    /// the cursor and lifts by a couple of pixels, then settles back on leave.
    /// Does nothing when the user has turned off client-area animation.
    /// </summary>
    public static class Tilt
    {
        private const double Smoothing = 0.18;

        private sealed class State
        {
            public readonly SkewTransform      Skew  = new();
            public readonly ScaleTransform     Scale = new(1, 1);
            public readonly TranslateTransform Shift = new();
            public readonly DispatcherTimer    Timer;

            public double TargetX, TargetY, CurrentX, CurrentY;
            public double TargetLift, CurrentLift;
            public bool   Hovering;

            public State(FrameworkElement element)
            {
                var group = new TransformGroup();
                group.Children.Add(Skew);
                group.Children.Add(Scale);
                group.Children.Add(Shift);

                element.RenderTransformOrigin = new Point(0.5, 0.5);
                element.RenderTransform       = group;

                Timer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                Timer.Tick += (_, _) => Step();
            }

            public void Step()
            {
                double dx = TargetX - CurrentX;
                double dy = TargetY - CurrentY;
                double dl = TargetLift - CurrentLift;

                bool settled = Math.Abs(dx) < 0.0015
                            && Math.Abs(dy) < 0.0015
                            && Math.Abs(dl) < 0.05
                            && !Hovering;

                if (settled)
                {
                    CurrentX = TargetX;
                    CurrentY = TargetY;
                    CurrentLift = TargetLift;
                    Apply();
                    Timer.Stop();
                    return;
                }

                CurrentX    += dx * Smoothing;
                CurrentY    += dy * Smoothing;
                CurrentLift += dl * Smoothing;
                Apply();
            }

            public void Apply()
            {
                double s = Math.Max(0, Strength);

                Skew.AngleX  = -CurrentY * s * 0.55;
                Skew.AngleY  =  CurrentX * s * 0.55;

                double scale = 1.0 + (Hovering ? 0.012 : 0.0);
                Scale.ScaleX = scale;
                Scale.ScaleY = scale;

                Shift.X = -CurrentX * s * 0.7;
                Shift.Y = -CurrentY * s * 0.7 + CurrentLift;
            }

            public double Strength { get; set; } = 5;
        }

        // ── attached properties ───────────────────────────────────────────

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled", typeof(bool), typeof(Tilt),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject element, bool value)
            => element.SetValue(IsEnabledProperty, value);

        public static bool GetIsEnabled(DependencyObject element)
            => (bool)element.GetValue(IsEnabledProperty);

        public static readonly DependencyProperty StrengthProperty =
            DependencyProperty.RegisterAttached(
                "Strength", typeof(double), typeof(Tilt),
                new PropertyMetadata(5.0, OnStrengthChanged));

        public static void SetStrength(DependencyObject element, double value)
            => element.SetValue(StrengthProperty, value);

        public static double GetStrength(DependencyObject element)
            => (double)element.GetValue(StrengthProperty);

        private static readonly DependencyProperty StateProperty =
            DependencyProperty.RegisterAttached(
                "State", typeof(State), typeof(Tilt), new PropertyMetadata(null));

        private static void OnStrengthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d.GetValue(StateProperty) is State state)
                state.Strength = (double)e.NewValue;
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element) return;

            if ((bool)e.NewValue) Attach(element);
            else Detach(element);
        }

        private static void Attach(FrameworkElement element)
        {
            if (element.GetValue(StateProperty) is State) return;

            var state = new State(element) { Strength = GetStrength(element) };
            element.SetValue(StateProperty, state);

            element.MouseEnter += OnMouseEnter;
            element.MouseMove  += OnMouseMove;
            element.MouseLeave += OnMouseLeave;
            element.Unloaded   += OnUnloaded;
        }

        private static void Detach(FrameworkElement element)
        {
            if (element.GetValue(StateProperty) is not State state) return;

            element.MouseEnter -= OnMouseEnter;
            element.MouseMove  -= OnMouseMove;
            element.MouseLeave -= OnMouseLeave;
            element.Unloaded   -= OnUnloaded;

            state.Timer.Stop();
            element.ClearValue(StateProperty);
            element.RenderTransform = Transform.Identity;
        }

        private static State? Of(object sender) =>
            sender is FrameworkElement element ? element.GetValue(StateProperty) as State : null;

        private static void OnMouseEnter(object sender, MouseEventArgs e)
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            var state = Of(sender);
            if (state == null) return;

            state.Hovering   = true;
            state.TargetLift = -3;
            state.Timer.Start();
        }

        private static void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            if (sender is not FrameworkElement element) return;
            var state = Of(sender);
            if (state == null || !state.Hovering) return;

            var p = e.GetPosition(element);
            if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return;

            state.TargetX = Math.Clamp((p.X / element.ActualWidth) * 2 - 1, -1, 1);
            state.TargetY = Math.Clamp((p.Y / element.ActualHeight) * 2 - 1, -1, 1);
        }

        private static void OnMouseLeave(object sender, MouseEventArgs e)
        {
            var state = Of(sender);
            if (state == null) return;

            state.Hovering   = false;
            state.TargetX    = 0;
            state.TargetY    = 0;
            state.TargetLift = 0;
            if (SystemParameters.ClientAreaAnimation) state.Timer.Start();
        }

        private static void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.GetValue(StateProperty) is State state)
                state.Timer.Stop();
        }
    }
}
