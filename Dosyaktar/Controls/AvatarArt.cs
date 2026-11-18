using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Dosyaktar.Services;

namespace Dosyaktar.Controls
{
    /// <summary>
    /// Avatarların vektör çizimi. Her tarif 100×100 kutuda çizilir; kontrol
    /// bunu ölçeğe göre büyütür. Hareketler zamana (saniye) bağlıdır ve
    /// devre dışıyken t = 0 ile sabit kare çizilir.
    /// </summary>
    internal static class AvatarArt
    {
        // ── paylaşılan fırçalar ──────────────────────────────────────────────
        private static readonly Brush White      = Solid(255, 255, 255, 255);
        private static readonly Brush VisorLight = Solid(255, 216, 228, 255);
        private static readonly Brush WhiteFade  = Solid(46, 255, 255, 255);
        private static readonly Brush BlushBrush = Solid(92, 255, 138, 168);
        private static readonly Brush SoftWhite  = Solid(210, 255, 255, 255);
        private static readonly Brush Transparent = Solid(0, 255, 255, 255);

        private static readonly Dictionary<(Brush, int, PenLineCap), Pen> Pens = new();
        private static readonly Dictionary<(Color, Color, bool), Brush> Grads = new();

        public static void Draw(DrawingContext dc, AvatarSpec spec, double t, bool still)
        {
            if (still) t = 0;

            // arka plan
            dc.DrawEllipse(spec.BgBrush, null, new Point(50, 50), 50, 50);
            dc.DrawEllipse(spec.GlossBrush, null, new Point(38, 30), 36, 26);

            switch (spec.Recipe)
            {
                case 0:  Cat(dc, spec, t); break;
                case 1:  Fox(dc, spec, t); break;
                case 2:  Owl(dc, spec, t); break;
                case 3:  Panda(dc, spec, t); break;
                case 4:  Bear(dc, spec, t); break;
                case 5:  Rabbit(dc, spec, t); break;
                case 6:  Frog(dc, spec, t); break;
                case 7:  Penguin(dc, spec, t); break;
                case 8:  Koala(dc, spec, t); break;
                case 9:  Lion(dc, spec, t); break;
                case 10: Tiger(dc, spec, t); break;
                case 11: Octopus(dc, spec, t); break;
                case 12: Astronaut(dc, spec, t); break;
                case 13: Robot(dc, spec, t); break;
                case 14: Alien(dc, spec, t); break;
                case 15: Planet(dc, spec, t); break;
                case 16: Moon(dc, spec, t); break;
                case 17: StarAvatar(dc, spec, t); break;
                case 18: Comet(dc, spec, t); break;
                case 19: Ufo(dc, spec, t); break;
                case 20: Sun(dc, spec, t); break;
                case 21: Cloud(dc, spec, t); break;
                case 22: Leaf(dc, spec, t); break;
                case 23: Cactus(dc, spec, t); break;
                case 24: Mushroom(dc, spec, t); break;
                case 25: Flame(dc, spec, t); break;
                case 26: Wave(dc, spec, t); break;
                case 27: Crystal(dc, spec, t); break;
                case 28: Ghost(dc, spec, t); break;
                case 29: Ninja(dc, spec, t); break;
                case 30: Wizard(dc, spec, t); break;
                default: Knight(dc, spec, t); break;
            }

            // ince iç halka — cam yüzey hissi
            dc.DrawEllipse(null, P(SoftWhite, 1.2), new Point(50, 50), 48.6, 48.6);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  HAYVANLAR
        // ═════════════════════════════════════════════════════════════════════

        private static void Cat(DrawingContext dc, AvatarSpec s, double t)
        {
            double twitch = Math.Sin(t * 2.3 + s.Seed * 6.28) * 4.5;

            // kuyruk
            dc.PushTransform(new RotateTransform(8 + Math.Sin(t * 1.5 + s.Seed * 4) * 10, 70, 78));
            dc.DrawGeometry(null, P(s.BodyBrush, 7), Geo(g =>
            {
                g.BeginFigure(new Point(70, 78), false, false);
                g.BezierTo(new Point(88, 82), new Point(92, 64), new Point(82, 55), true, false);
            }));
            dc.Pop();

            // kulaklar
            dc.PushTransform(new RotateTransform(twitch, 34, 30));
            Poly(dc, s.BodyBrush, null, new Point(24, 33), new Point(20, 8), new Point(46, 22));
            Poly(dc, s.AccentBrush, null, new Point(27.5, 28), new Point(25, 15), new Point(39, 23));
            dc.Pop();
            dc.PushTransform(new RotateTransform(-twitch, 66, 30));
            Poly(dc, s.BodyBrush, null, new Point(76, 33), new Point(80, 8), new Point(54, 22));
            Poly(dc, s.AccentBrush, null, new Point(72.5, 28), new Point(75, 15), new Point(61, 23));
            dc.Pop();

            Circle(dc, s.BodyBrush, 50, 54, 27);
            dc.DrawEllipse(s.LightBrush, null, new Point(50, 63), 15, 10.5);

            BeadEyes(dc, s, 50, 10, 4.2, Blink(t, 4.1, s.Seed));
            Poly(dc, s.AccentBrush, null, new Point(46.5, 59), new Point(53.5, 59), new Point(50, 63));
            Smile(dc, P(s.InkBrush, 1.6), 50, 64.5, 8, 3.2);
            Blush(dc, 60, 16.5);

            var wp = P(s.SoftInkBrush, 1.3);
            dc.DrawLine(wp, new Point(34, 60), new Point(21, 57));
            dc.DrawLine(wp, new Point(34, 64), new Point(21, 65));
            dc.DrawLine(wp, new Point(66, 60), new Point(79, 57));
            dc.DrawLine(wp, new Point(66, 64), new Point(79, 65));
        }

        private static void Fox(DrawingContext dc, AvatarSpec s, double t)
        {
            double sway = Math.Sin(t * 1.7 + s.Seed * 5) * 9;

            dc.PushTransform(new RotateTransform(sway, 70, 80));
            dc.DrawGeometry(null, P(s.BodyBrush, 9), Geo(g =>
            {
                g.BeginFigure(new Point(70, 80), false, false);
                g.BezierTo(new Point(90, 76), new Point(89, 56), new Point(80, 51), true, false);
            }));
            Circle(dc, White, 81, 53, 5);
            dc.Pop();

            Poly(dc, s.BodyBrush, null, new Point(24, 34), new Point(17, 6), new Point(46, 21));
            Poly(dc, s.DarkBrush, null, new Point(22, 18), new Point(19, 7), new Point(31, 13));
            Poly(dc, s.BodyBrush, null, new Point(76, 34), new Point(83, 6), new Point(54, 21));
            Poly(dc, s.DarkBrush, null, new Point(78, 18), new Point(81, 7), new Point(69, 13));

            dc.DrawEllipse(s.BodyBrush, null, new Point(50, 56), 28, 24);
            dc.DrawEllipse(White, null, new Point(50, 68), 17, 11.5);

            BeadEyes(dc, s, 51, 12, 4, Blink(t, 4.5, s.Seed + 0.2));
            Circle(dc, s.InkBrush, 50, 63, 2.6);
            Smile(dc, P(s.InkBrush, 1.6), 50, 65.5, 9, 3.4);
            Blush(dc, 64, 19);
        }

        private static void Owl(DrawingContext dc, AvatarSpec s, double t)
        {
            double tilt = Math.Sin(t * 0.8 + s.Seed * 6.28) * 4;

            dc.PushTransform(new RotateTransform(tilt, 50, 54));

            Poly(dc, s.BodyBrush, null, new Point(31, 32), new Point(26, 15), new Point(43, 25));
            Poly(dc, s.BodyBrush, null, new Point(69, 32), new Point(74, 15), new Point(57, 25));

            dc.DrawEllipse(s.BodyBrush, null, new Point(50, 58), 27, 28);
            dc.DrawEllipse(s.LightBrush, null, new Point(50, 66), 17.5, 18);

            var ring = P(s.DarkBrush, 1.8);
            Circle(dc, White, 37, 46, 11.5, ring);
            Circle(dc, White, 63, 46, 11.5, ring);

            double open = Blink(t, 4.6, s.Seed);
            foreach (double cx in new[] { 37.0, 63.0 })
            {
                dc.DrawEllipse(s.InkBrush, null, new Point(cx, 47), 5.1, Math.Max(0.7, 5.1 * open));
                if (open > 0.6)
                    Circle(dc, White, cx + 1.7, 44.7, 1.6);
            }

            Poly(dc, s.AccentBrush, null, new Point(45.5, 57), new Point(54.5, 57), new Point(50, 62.5));
            dc.DrawLine(P(s.DarkBrush, 3.2), new Point(44, 84), new Point(44, 89));
            dc.DrawLine(P(s.DarkBrush, 3.2), new Point(56, 84), new Point(56, 89));

            dc.Pop();
        }

        private static void Panda(DrawingContext dc, AvatarSpec s, double t)
        {
            double bob = Math.Sin(t * 1.4 + s.Seed * 6) * 1.2;

            Circle(dc, s.DarkBrush, 27, 27 + bob * 0.4, 10);
            Circle(dc, s.DarkBrush, 73, 27 - bob * 0.4, 10);

            Circle(dc, s.BodyBrush, 50, 54, 27);
            dc.DrawEllipse(null, P(s.DarkBrush, 1.4), new Point(50, 54), 27, 27);

            dc.PushTransform(new RotateTransform(-14, 37, 50));
            dc.DrawEllipse(s.DarkBrush, null, new Point(37, 50), 8, 10.5);
            dc.Pop();
            dc.PushTransform(new RotateTransform(14, 63, 50));
            dc.DrawEllipse(s.DarkBrush, null, new Point(63, 50), 8, 10.5);
            dc.Pop();

            ScleraEyes(dc, s, 50, 12.5, 3.8, 4.4, 2.3, Blink(t, 4.2, s.Seed));

            Poly(dc, s.InkBrush, null, new Point(46, 59.5), new Point(54, 59.5), new Point(50, 63));
            Smile(dc, P(s.InkBrush, 1.5), 50, 64.5, 7, 3);
            Blush(dc, 63, 18);
        }

        private static void Bear(DrawingContext dc, AvatarSpec s, double t)
        {
            double breathe = 1 + 0.018 * Math.Sin(t * 1.6 + s.Seed * 6);

            dc.PushTransform(new ScaleTransform(breathe, breathe, 50, 60));

            foreach (double cx in new[] { 27.0, 73.0 })
            {
                Circle(dc, s.BodyBrush, cx, 29, 11);
                Circle(dc, s.LightBrush, cx, 29, 6);
            }

            Circle(dc, s.BodyBrush, 50, 56, 27);
            dc.DrawEllipse(s.AccentBrush, null, new Point(50, 65), 16, 11.5);
            BeadEyes(dc, s, 51, 11, 3.8, Blink(t, 4.7, s.Seed));
            Circle(dc, s.InkBrush, 50, 61, 3.2);
            Smile(dc, P(s.InkBrush, 1.6), 50, 66, 9, 3.4);
            dc.Pop();

            Blush(dc, 63, 19);
        }

        private static void Rabbit(DrawingContext dc, AvatarSpec s, double t)
        {
            double wig = Math.Sin(t * 1.9 + s.Seed * 5) * 5;

            Ear(dc, s, 38, 40, 5 + wig);
            Ear(dc, s, 62, 40, -5 - wig);

            Circle(dc, s.BodyBrush, 50, 57, 25);
            dc.DrawEllipse(null, P(s.DarkBrush, 1.2), new Point(50, 57), 25, 25);

            BeadEyes(dc, s, 54, 10, 3.9, Blink(t, 4.3, s.Seed));
            Poly(dc, s.AccentBrush, null, new Point(47, 61.5), new Point(53, 61.5), new Point(50, 65));
            Smile(dc, P(s.InkBrush, 1.5), 50, 66, 8, 3);
            Blush(dc, 64, 17);
        }

        private static void Ear(DrawingContext dc, AvatarSpec s, double cx, double cy, double angle)
        {
            dc.PushTransform(new RotateTransform(angle, cx, cy));
            RoundRect(dc, s.BodyBrush, null, new Rect(cx - 6, cy - 32, 12, 34), 6, 6);
            RoundRect(dc, s.AccentBrush, null, new Rect(cx - 2.6, cy - 27, 5.2, 24), 2.6, 2.6);
            dc.Pop();
        }

        private static void Frog(DrawingContext dc, AvatarSpec s, double t)
        {
            double throat = 1 + 0.09 * Math.Sin(t * 2.6 + s.Seed * 6);

            Circle(dc, s.BodyBrush, 33, 37, 14);
            Circle(dc, s.BodyBrush, 67, 37, 14);
            Circle(dc, White, 33, 37, 9.5);
            Circle(dc, White, 67, 37, 9.5);

            double open = Blink(t, 4.9, s.Seed);
            foreach (double cx in new[] { 33.0, 67.0 })
            {
                dc.DrawEllipse(s.InkBrush, null, new Point(cx, 38), 4.2, Math.Max(0.7, 4.2 * open));
                if (open > 0.6) Circle(dc, White, cx + 1.4, 36, 1.3);
            }

            dc.DrawEllipse(s.BodyBrush, null, new Point(50, 60), 33, 23);
            dc.PushTransform(new ScaleTransform(1, throat, 50, 73));
            dc.DrawEllipse(s.LightBrush, null, new Point(50, 73), 12, 7);
            dc.Pop();

            Smile(dc, P(s.InkBrush, 2), 50, 64, 32, 8);
            Circle(dc, s.InkBrush, 45, 55, 1.1);
            Circle(dc, s.InkBrush, 55, 55, 1.1);
            Blush(dc, 64, 21);
        }

        private static void Penguin(DrawingContext dc, AvatarSpec s, double t)
        {
            double waddle = Math.Sin(t * 1.7 + s.Seed * 6) * 2.4;

            dc.PushTransform(new RotateTransform(waddle, 50, 84));

            Ellipse(dc, s.AccentBrush, 40, 86, 9, 4.2);
            Ellipse(dc, s.AccentBrush, 60, 86, 9, 4.2);

            dc.PushTransform(new RotateTransform(-18, 23, 60));
            dc.DrawEllipse(s.DarkBrush, null, new Point(23, 60), 6, 15);
            dc.Pop();
            dc.PushTransform(new RotateTransform(18, 77, 60));
            dc.DrawEllipse(s.DarkBrush, null, new Point(77, 60), 6, 15);
            dc.Pop();

            dc.DrawEllipse(s.BodyBrush, null, new Point(50, 56), 28, 30);
            dc.DrawEllipse(White, null, new Point(50, 62), 19, 23);

            BeadEyes(dc, s, 50, 8.5, 3.3, Blink(t, 4.4, s.Seed));
            Poly(dc, s.AccentBrush, null, new Point(45.5, 57), new Point(54.5, 57), new Point(50, 62));

            dc.Pop();
        }

        private static void Koala(DrawingContext dc, AvatarSpec s, double t)
        {
            double wig = Math.Sin(t * 2 + s.Seed * 6) * 1.8;

            foreach (var (cx, sway) in new[] { (25.0, wig), (75.0, -wig) })
            {
                Circle(dc, s.BodyBrush, cx, 38 + sway, 15);
                Circle(dc, s.LightBrush, cx, 38 + sway, 9.5);
            }

            Circle(dc, s.BodyBrush, 50, 56, 26);
            Ellipse(dc, s.InkBrush, 50, 59.5, 8, 10);

            BeadEyes(dc, s, 48, 13.5, 3.2, Blink(t, 4.8, s.Seed));
            Smile(dc, P(s.InkBrush, 1.5), 50, 68, 7, 2.6);
            Blush(dc, 62, 21);
        }

        private static void Lion(DrawingContext dc, AvatarSpec s, double t)
        {
            dc.PushTransform(new RotateTransform(Math.Sin(t * 0.7 + s.Seed * 6) * 4, 50, 52));
            for (int i = 0; i < 12; i++)
            {
                double a = i * Math.PI / 6;
                Circle(dc, s.DarkBrush, 50 + Math.Cos(a) * 29, 52 + Math.Sin(a) * 29, 11);
            }
            dc.Pop();

            Circle(dc, s.BodyBrush, 32, 36, 8);
            Circle(dc, s.AccentBrush, 32, 36, 4.5);
            Circle(dc, s.BodyBrush, 68, 36, 8);
            Circle(dc, s.AccentBrush, 68, 36, 4.5);

            Circle(dc, s.BodyBrush, 50, 53, 23);
            Ellipse(dc, s.AccentBrush, 43, 62, 8, 6);
            Ellipse(dc, s.AccentBrush, 57, 62, 8, 6);
            Poly(dc, s.InkBrush, null, new Point(46, 57.5), new Point(54, 57.5), new Point(50, 61));

            BeadEyes(dc, s, 48, 10, 3.6, Blink(t, 4.2, s.Seed + 0.1));
            Smile(dc, P(s.InkBrush, 1.6), 50, 64, 9, 3.4);
        }

        private static void Tiger(DrawingContext dc, AvatarSpec s, double t)
        {
            Circle(dc, s.BodyBrush, 28, 32, 9);
            Circle(dc, s.AccentBrush, 28, 32, 5);
            Circle(dc, s.BodyBrush, 72, 32, 9);
            Circle(dc, s.AccentBrush, 72, 32, 5);

            Circle(dc, s.BodyBrush, 50, 54, 27);

            var stripe = P(s.InkBrush, 2.4);
            dc.DrawLine(stripe, new Point(24, 52), new Point(33, 55));
            dc.DrawLine(stripe, new Point(24, 58), new Point(33, 59));
            dc.DrawLine(stripe, new Point(76, 52), new Point(67, 55));
            dc.DrawLine(stripe, new Point(76, 58), new Point(67, 59));

            Poly(dc, s.InkBrush, null, new Point(40, 27), new Point(44, 38), new Point(36, 37));
            Poly(dc, s.InkBrush, null, new Point(60, 27), new Point(64, 38), new Point(56, 37));
            Poly(dc, s.InkBrush, null, new Point(50, 26), new Point(53, 36), new Point(47, 36));

            Ellipse(dc, White, 43, 64, 9, 7);
            Ellipse(dc, White, 57, 64, 9, 7);
            Poly(dc, s.InkBrush, null, new Point(46, 58), new Point(54, 58), new Point(50, 62));

            BeadEyes(dc, s, 50, 11, 3.8, Blink(t, 4.6, s.Seed));
            Smile(dc, P(s.InkBrush, 1.6), 50, 64.5, 10, 3.4);
        }

        private static void Octopus(DrawingContext dc, AvatarSpec s, double t)
        {
            for (int i = 0; i < 6; i++)
            {
                double x = 30 + i * 8;
                double ph = t * 2 + i * 0.9 + s.Seed * 6;
                var geo = Geo(g =>
                {
                    g.BeginFigure(new Point(x, 60), false, false);
                    g.QuadraticBezierTo(new Point(x + 7 * Math.Sin(ph), 74),
                                        new Point(x + 4 * Math.Sin(ph + 1.3), 88), true, false);
                });
                dc.DrawGeometry(null, P(i % 2 == 0 ? s.BodyBrush : s.DarkBrush, 7), geo);
            }

            dc.DrawEllipse(s.BodyBrush, null, new Point(50, 46), 25, 23);
            Circle(dc, s.AccentBrush, 35, 36, 2.6);
            Circle(dc, s.AccentBrush, 65, 36, 2.6);

            BeadEyes(dc, s, 46, 9, 3.9, Blink(t, 4.4, s.Seed + 0.3));
            Smile(dc, P(s.InkBrush, 1.6), 50, 53, 9, 3);
            Blush(dc, 52, 16);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  UZAY
        // ═════════════════════════════════════════════════════════════════════

        private static void Astronaut(DrawingContext dc, AvatarSpec s, double t)
        {
            double bob = Math.Sin(t * 0.8 + s.Seed * 6) * 1.8;
            dc.PushTransform(new TranslateTransform(0, bob));

            RoundRect(dc, s.BodyBrush, null, new Rect(24, 68, 52, 26), 13, 13);
            RoundRect(dc, s.AccentBrush, null, new Rect(24, 78, 52, 5), 2.5, 2.5);

            Circle(dc, s.BodyBrush, 50, 46, 31);
            dc.DrawEllipse(null, P(s.DarkBrush, 2), new Point(50, 46), 31, 31);

            var visor = new RectangleGeometry(new Rect(32, 30, 36, 28), 14, 14);
            dc.DrawGeometry(s.InkBrush, null, visor);
            dc.PushClip(visor);

            double open = Blink(t, 4.4, s.Seed);
            foreach (double cx in new[] { 43.0, 57.0 })
            {
                dc.DrawEllipse(VisorLight, null, new Point(cx, 42), 2.7, Math.Max(0.6, 2.7 * open));
                if (open > 0.6) Circle(dc, White, cx + 0.9, 40.8, 0.8);
            }
            Smile(dc, P(VisorLight, 1.5), 50, 48, 9, 3);

            double sx = 26 + ((t * 15) % 50);
            dc.PushTransform(new RotateTransform(-22, 50, 44));
            dc.DrawRectangle(WhiteFade, null, new Rect(sx, 14, 9, 64));
            dc.Pop();
            dc.Pop();

            Circle(dc, s.AccentBrush, 50, 86, 2.4);
            dc.Pop();
        }

        private static void Robot(DrawingContext dc, AvatarSpec s, double t)
        {
            double pulse = 0.5 + 0.5 * Math.Sin(t * 3 + s.Seed * 6);

            dc.DrawLine(P(s.DarkBrush, 3), new Point(50, 29), new Point(50, 19));
            Circle(dc, s.AccentBrush, 50, 16, 2.6 + pulse * 2.2);

            RoundRect(dc, s.BodyBrush, P(s.DarkBrush, 1.4), new Rect(27, 29, 46, 40), 12, 12);
            RoundRect(dc, s.DarkBrush, null, new Rect(22, 42, 6, 12), 3, 3);
            RoundRect(dc, s.DarkBrush, null, new Rect(72, 42, 6, 12), 3, 3);
            RoundRect(dc, s.InkBrush, null, new Rect(33, 38, 34, 21), 8, 8);

            double scan = Math.Sin(t * 0.9 + s.Seed * 6) * 3;
            double open = Blink(t, 5.4, s.Seed + 0.2);
            foreach (double cx in new[] { 43.0, 57.0 })
            {
                dc.DrawEllipse(s.AccentBrush, null, new Point(cx + scan, 48), 3, Math.Max(0.6, 3 * open));
                if (open > 0.7) Circle(dc, White, cx + scan + 0.8, 46.8, 0.7);
            }

            dc.DrawLine(P(s.AccentBrush, 2), new Point(46, 55), new Point(54, 55));
            RoundRect(dc, s.DarkBrush, null, new Rect(38, 70, 24, 8), 4, 4);
        }

        private static void Alien(DrawingContext dc, AvatarSpec s, double t)
        {
            double bob = Math.Sin(t * 1.1 + s.Seed * 6) * 2;

            var antennaPen = P(s.BodyBrush, 2.4);
            dc.DrawGeometry(null, antennaPen, Geo(g =>
            {
                g.BeginFigure(new Point(35, 27), false, false);
                g.QuadraticBezierTo(new Point(29, 18), new Point(27, 11), true, false);
            }));
            dc.DrawGeometry(null, antennaPen, Geo(g =>
            {
                g.BeginFigure(new Point(65, 27), false, false);
                g.QuadraticBezierTo(new Point(71, 18), new Point(73, 11), true, false);
            }));

            double pulse = 0.6 + 0.4 * Math.Sin(t * 2.4 + s.Seed * 6);
            Circle(dc, s.AccentBrush, 27, 9, 2.4 + pulse * 1.8);
            Circle(dc, s.AccentBrush, 73, 9, 2.4 + pulse * 1.8);

            dc.PushTransform(new TranslateTransform(0, bob));
            dc.DrawEllipse(s.BodyBrush, null, new Point(50, 53), 29, 33);

            double open = Blink(t, 5, s.Seed);
            dc.PushTransform(new RotateTransform(16, 38, 48));
            dc.DrawEllipse(s.InkBrush, null, new Point(38, 48), 7.5, Math.Max(0.9, 10.5 * open));
            dc.Pop();
            dc.PushTransform(new RotateTransform(-16, 62, 48));
            dc.DrawEllipse(s.InkBrush, null, new Point(62, 48), 7.5, Math.Max(0.9, 10.5 * open));
            dc.Pop();

            if (open > 0.6)
            {
                Circle(dc, White, 35.5, 43.5, 2.1);
                Circle(dc, White, 64.5, 43.5, 2.1);
            }

            Smile(dc, P(s.InkBrush, 1.8), 50, 64, 14, 5);
            Blush(dc, 67, 20);
            dc.Pop();
        }

        private static void Planet(DrawingContext dc, AvatarSpec s, double t)
        {
            Sparkle(dc, 16, 18, 2.4, t * 1.3 + s.Seed * 6);
            Sparkle(dc, 84, 26, 1.8, t * 1.1 + s.Seed * 4);

            const double tilt = -16;

            dc.PushTransform(new RotateTransform(tilt, 50, 52));
            dc.DrawEllipse(null, P(s.AccentBrush, 4.5), new Point(50, 52), 42, 11);
            dc.Pop();

            Circle(dc, s.BodyBrush, 50, 52, 24);
            dc.PushClip(new EllipseGeometry(new Point(50, 52), 24, 24));
            dc.PushOpacity(0.35);
            Ellipse(dc, s.LightBrush, 48, 39, 20, 6);
            dc.Pop();
            dc.Pop();

            dc.PushClip(new RectangleGeometry(new Rect(0, 52, 100, 48)));
            dc.PushTransform(new RotateTransform(tilt, 50, 52));
            dc.DrawEllipse(null, P(s.AccentBrush, 5), new Point(50, 52), 42, 11);
            dc.Pop();
            dc.Pop();

            BeadEyes(dc, s, 50, 9, 3.2, Blink(t, 4.8, s.Seed));
            Smile(dc, P(s.InkBrush, 1.5), 50, 57, 8, 3);
            Blush(dc, 55, 15);

            double a = t * 0.9 + s.Seed * 6.28;
            double mx = 42 * Math.Cos(a), my = 11 * Math.Sin(a);
            double rad = tilt * Math.PI / 180;
            double ox = mx * Math.Cos(rad) - my * Math.Sin(rad);
            double oy = mx * Math.Sin(rad) + my * Math.Cos(rad);
            Circle(dc, s.LightBrush, 50 + ox, 52 + oy, 3.2);
        }

        private static void Moon(DrawingContext dc, AvatarSpec s, double t)
        {
            double glow = 0.5 + 0.5 * Math.Sin(t * 1.2 + s.Seed * 6);
            dc.PushOpacity(0.22 + glow * 0.18);
            Circle(dc, White, 50, 50, 35);
            dc.Pop();

            Circle(dc, s.BodyBrush, 50, 50, 27);

            dc.PushOpacity(0.35);
            Circle(dc, s.DarkBrush, 37, 40, 5.2);
            Circle(dc, s.DarkBrush, 61, 37, 4);
            Circle(dc, s.DarkBrush, 45, 65, 3.4);
            dc.Pop();

            BeadEyes(dc, s, 51, 10, 3.4, Blink(t, 4.5, s.Seed));
            Smile(dc, P(s.InkBrush, 1.6), 50, 58, 9, 3.2);
            Blush(dc, 57, 17);

            Sparkle(dc, 18, 22, 2.2, t * 1.4 + s.Seed * 6);
            Sparkle(dc, 82, 74, 1.8, t * 1.2 + s.Seed * 5);
        }

        private static void StarAvatar(DrawingContext dc, AvatarSpec s, double t)
        {
            double tw = 1 + 0.025 * Math.Sin(t * 2.2 + s.Seed * 6);
            dc.PushTransform(new ScaleTransform(tw, tw, 50, 50));
            dc.PushOpacity(0.28 + 0.12 * Math.Sin(t * 2.2 + s.Seed * 6));
            Circle(dc, s.AccentBrush, 50, 50, 37);
            dc.Pop();

            Poly(dc, s.BodyBrush, null, StarPoints(50, 50, 33, 14, 5, -90));
            BeadEyes(dc, s, 51, 8, 3.2, Blink(t, 4.6, s.Seed));
            Smile(dc, P(s.InkBrush, 1.5), 50, 58, 8, 3);
            Blush(dc, 55, 14);
            dc.Pop();

            double sp = Math.Abs(Math.Sin(t * 1.7 + s.Seed * 6));
            dc.PushOpacity(0.2 + sp * 0.5);
            var pen = P(White, 1.6);
            dc.DrawLine(pen, new Point(12, 16), new Point(19, 16));
            dc.DrawLine(pen, new Point(15.5, 12.5), new Point(15.5, 19.5));
            dc.DrawLine(pen, new Point(82, 84), new Point(89, 84));
            dc.DrawLine(pen, new Point(85.5, 80.5), new Point(85.5, 87.5));
            dc.Pop();
        }

        private static void Comet(DrawingContext dc, AvatarSpec s, double t)
        {
            var tailGrad = FadeGrad(A(s.Accent, 210), A(s.Accent, 0), false);
            Poly(dc, tailGrad, null,
                new Point(66, 42), new Point(6, 25), new Point(20, 49), new Point(6, 72), new Point(66, 56));

            dc.PushOpacity(0.6);
            var streak = P(s.AccentBrush, 2);
            for (int i = 0; i < 3; i++)
            {
                double y = 40 + i * 7;
                dc.DrawGeometry(null, streak, Geo(g =>
                {
                    g.BeginFigure(new Point(58, y), false, false);
                    g.QuadraticBezierTo(new Point(34, y - 3 + i * 2), new Point(12, y - 5 + i * 4), true, false);
                }));
            }
            dc.Pop();

            for (int i = 0; i < 6; i++)
            {
                double p = (t * 0.32 + i * 0.17 + s.Seed) % 1.0;
                double x = 58 - p * 48;
                double y = 40 + i * 4.4 + Math.Sin(t * 2 + i) * 2;
                dc.PushOpacity((1 - p) * 0.9);
                Circle(dc, i % 2 == 0 ? White : s.AccentBrush, x, y, 1.7);
                dc.Pop();
            }

            double bob = Math.Sin(t * 1.4 + s.Seed * 6) * 1.5;
            dc.PushTransform(new TranslateTransform(0, bob));
            Circle(dc, s.BodyBrush, 66, 49, 19);
            BeadEyes(dc, s, 49, 7, 3, Blink(t, 4.4, s.Seed), 66);
            Smile(dc, P(s.InkBrush, 1.4), 66, 56, 7.5, 2.8);
            Blush(dc, 54, 12, 66);
            dc.Pop();
        }

        private static void Ufo(DrawingContext dc, AvatarSpec s, double t)
        {
            double beam = 0.32 + 0.22 * Math.Sin(t * 1.6 + s.Seed * 6);
            dc.PushOpacity(beam);
            Poly(dc, FadeGrad(A(s.Accent, 150), A(s.Accent, 0), true), null,
                new Point(38, 62), new Point(62, 62), new Point(78, 96), new Point(22, 96));
            dc.Pop();

            double bob = Math.Sin(t * 0.9 + s.Seed * 6) * 2.2;
            dc.PushTransform(new TranslateTransform(0, bob));

            var domeGrad = new LinearGradientBrush(A(s.Accent, 235), A(s.Accent, 90), new Point(0, 0), new Point(0, 1));
            domeGrad.Freeze();
            dc.DrawEllipse(domeGrad, P(s.DarkBrush, 1.2), new Point(50, 47), 14, 14);

            dc.DrawEllipse(s.BodyBrush, P(s.DarkBrush, 1.5), new Point(50, 58), 34, 11);

            double pulse = 0.5 + 0.5 * Math.Sin(t * 3 + s.Seed * 6);
            dc.PushOpacity(0.55 + pulse * 0.45);
            Circle(dc, White, 50, 44, 3.4);
            dc.Pop();

            for (int i = 0; i < 5; i++)
            {
                double a = 0.35 + 0.65 * Math.Max(0, Math.Sin(t * 4 - i * 0.9));
                dc.PushOpacity(a);
                Circle(dc, s.AccentBrush, 22 + i * 14, 61, 1.9);
                dc.Pop();
            }

            dc.Pop();
        }

        // ═════════════════════════════════════════════════════════════════════
        //  DOĞA
        // ═════════════════════════════════════════════════════════════════════

        private static void Sun(DrawingContext dc, AvatarSpec s, double t)
        {
            double pulse = 0.5 + 0.5 * Math.Sin(t * 1.5 + s.Seed * 6);
            dc.PushOpacity(0.18 + pulse * 0.14);
            Circle(dc, s.AccentBrush, 50, 50, 41);
            dc.Pop();

            dc.PushTransform(new RotateTransform(t * 7 + s.Seed * 360, 50, 50));
            for (int i = 0; i < 12; i++)
            {
                double a = i * Math.PI / 6;
                double cos = Math.Cos(a), sin = Math.Sin(a);
                dc.DrawLine(P(s.AccentBrush, 4.5),
                    new Point(50 + cos * 29, 50 + sin * 29),
                    new Point(50 + cos * 40, 50 + sin * 40));
            }
            dc.Pop();

            Circle(dc, s.BodyBrush, 50, 50, 26);
            BeadEyes(dc, s, 50, 9, 3.4, Blink(t, 4.3, s.Seed));
            Smile(dc, P(s.InkBrush, 1.6), 50, 58, 9, 3.2);
            Blush(dc, 57, 16);
        }

        private static void Cloud(DrawingContext dc, AvatarSpec s, double t)
        {
            double drift = Math.Sin(t * 0.7 + s.Seed * 6) * 2.2;
            dc.PushTransform(new TranslateTransform(drift, 0));

            for (int i = 0; i < 3; i++)
            {
                double p = (t * 0.55 + i * 0.29 + s.Seed) % 1.0;
                double y = 70 + p * 17;
                dc.PushOpacity((1 - p) * 0.9);
                dc.DrawLine(P(s.AccentBrush, 2.6), new Point(37 + i * 13, y), new Point(35.4 + i * 13, y + 5));
                dc.Pop();
            }

            RoundRect(dc, s.BodyBrush, null, new Rect(22, 51, 56, 21), 10.5, 10.5);
            Circle(dc, s.BodyBrush, 35, 52, 13);
            Circle(dc, s.BodyBrush, 50, 45, 17);
            Circle(dc, s.BodyBrush, 65, 52, 13);

            BeadEyes(dc, s, 57, 8, 3.1, Blink(t, 4.7, s.Seed));
            Smile(dc, P(s.InkBrush, 1.5), 50, 62, 8, 3);
            Blush(dc, 60, 14);
            dc.Pop();
        }

        private static void Leaf(DrawingContext dc, AvatarSpec s, double t)
        {
            double sway = Math.Sin(t * 1.1 + s.Seed * 6) * 5;
            dc.PushTransform(new RotateTransform(sway, 50, 84));

            dc.DrawLine(P(s.DarkBrush, 3), new Point(50, 85), new Point(50, 72));
            var leaf = Geo(g =>
            {
                g.BeginFigure(new Point(50, 13), true, true);
                g.QuadraticBezierTo(new Point(85, 34), new Point(66, 66), true, false);
                g.QuadraticBezierTo(new Point(58, 77), new Point(50, 79), true, false);
                g.QuadraticBezierTo(new Point(42, 77), new Point(34, 66), true, false);
                g.QuadraticBezierTo(new Point(15, 34), new Point(50, 13), true, false);
            });
            dc.DrawGeometry(s.BodyBrush, null, leaf);

            var vein = P(s.LightBrush, 1.8);
            dc.DrawLine(vein, new Point(50, 72), new Point(50, 24));
            for (int i = 0; i < 3; i++)
            {
                double y = 34 + i * 12;
                dc.DrawLine(vein, new Point(50, y), new Point(36 + i * 2, y + 8));
                dc.DrawLine(vein, new Point(50, y), new Point(64 - i * 2, y + 8));
            }

            BeadEyes(dc, s, 48, 8, 3, Blink(t, 4.5, s.Seed));
            Smile(dc, P(s.InkBrush, 1.4), 50, 56, 7.5, 2.8);
            dc.Pop();
        }

        private static void Cactus(DrawingContext dc, AvatarSpec s, double t)
        {
            double bloom = 0.5 + 0.5 * Math.Sin(t * 1.8 + s.Seed * 6);

            dc.DrawLine(P(s.BodyBrush, 11), new Point(34, 66), new Point(34, 52));
            dc.DrawLine(P(s.BodyBrush, 11), new Point(66, 68), new Point(66, 56));

            RoundRect(dc, s.BodyBrush, null, new Rect(40, 25, 20, 56), 10, 10);

            var spike = P(s.DarkBrush, 1.2);
            for (int i = 0; i < 4; i++)
            {
                double y = 38 + i * 9;
                dc.DrawLine(spike, new Point(40, y), new Point(35.5, y - 2.5));
                dc.DrawLine(spike, new Point(60, y + 3), new Point(64.5, y + 0.5));
            }

            dc.PushTransform(new ScaleTransform(1 + 0.12 * bloom, 1 + 0.12 * bloom, 50, 21));
            for (int i = 0; i < 6; i++)
            {
                double a = i * Math.PI / 3;
                Circle(dc, s.AccentBrush, 50 + Math.Cos(a) * 7, 21 + Math.Sin(a) * 7, 4.6);
            }
            Circle(dc, s.LightBrush, 50, 21, 3.4);
            dc.Pop();

            BeadEyes(dc, s, 48, 7, 3.1, Blink(t, 4.6, s.Seed));
            Smile(dc, P(s.InkBrush, 1.5), 50, 55, 7.5, 2.8);
            Blush(dc, 59, 13);
        }

        private static void Mushroom(DrawingContext dc, AvatarSpec s, double t)
        {
            double bob = Math.Sin(t * 1.5 + s.Seed * 6) * 1.2;
            dc.PushTransform(new TranslateTransform(0, bob));

            RoundRect(dc, s.AccentBrush, null, new Rect(40, 43, 20, 35), 9, 9);

            var cap = Geo(g =>
            {
                g.BeginFigure(new Point(13, 47), true, true);
                g.QuadraticBezierTo(new Point(14, 20), new Point(50, 18), true, false);
                g.QuadraticBezierTo(new Point(86, 20), new Point(87, 47), true, false);
                g.QuadraticBezierTo(new Point(50, 54), new Point(13, 47), true, false);
            });
            dc.DrawGeometry(s.BodyBrush, null, cap);

            double spot = 0.72 + 0.28 * Math.Sin(t * 2.4 + s.Seed * 6);
            dc.PushOpacity(spot);
            Circle(dc, White, 32, 32, 5.2);
            Circle(dc, White, 52, 27, 3.8);
            Circle(dc, White, 68, 37, 4.4);
            Circle(dc, White, 42, 40, 2.6);
            dc.Pop();

            BeadEyes(dc, s, 56, 6, 2.9, Blink(t, 4.4, s.Seed));
            Smile(dc, P(s.InkBrush, 1.4), 50, 62, 7, 2.6);
            Blush(dc, 59, 11);
            dc.Pop();
        }

        private static void Flame(DrawingContext dc, AvatarSpec s, double t)
        {
            double flick = 1 + 0.035 * Math.Sin(t * 6.4 + s.Seed * 6);
            double glow = 0.5 + 0.5 * Math.Sin(t * 5 + s.Seed * 6);

            dc.PushOpacity(0.2 + glow * 0.15);
            Ellipse(dc, s.AccentBrush, 50, 56, 36, 37);
            dc.Pop();

            dc.PushTransform(new ScaleTransform(1 / flick, flick, 50, 82));
            var outer = Geo(g =>
            {
                g.BeginFigure(new Point(50, 15), true, true);
                g.BezierTo(new Point(66, 32), new Point(76, 44), new Point(70, 60), true, false);
                g.BezierTo(new Point(66, 72), new Point(60, 80), new Point(50, 84), true, false);
                g.BezierTo(new Point(40, 80), new Point(34, 72), new Point(30, 60), true, false);
                g.BezierTo(new Point(24, 44), new Point(34, 32), new Point(50, 15), true, false);
            });
            dc.DrawGeometry(s.BodyBrush, null, outer);

            var inner = Geo(g =>
            {
                g.BeginFigure(new Point(50, 40), true, true);
                g.BezierTo(new Point(59, 49), new Point(62, 55), new Point(59, 64), true, false);
                g.BezierTo(new Point(57, 72), new Point(53, 77), new Point(50, 79), true, false);
                g.BezierTo(new Point(47, 77), new Point(43, 72), new Point(41, 64), true, false);
                g.BezierTo(new Point(38, 55), new Point(41, 49), new Point(50, 40), true, false);
            });
            dc.DrawGeometry(s.AccentBrush, null, inner);

            for (int i = 0; i < 3; i++)
            {
                double p = (t * 0.35 + i * 0.33 + s.Seed) % 1.0;
                double y = 32 - p * 24;
                double x = 43 + i * 6 + Math.Sin(t * 3 + i) * 2;
                dc.PushOpacity((1 - p) * 0.9);
                Circle(dc, s.AccentBrush, x, y, 1.8);
                dc.Pop();
            }

            BeadEyes(dc, s, 58, 7, 3, Blink(t, 4.2, s.Seed + 0.4));
            Smile(dc, P(s.InkBrush, 1.4), 50, 66, 7.5, 2.8);
            dc.Pop();
        }

        private static void Wave(DrawingContext dc, AvatarSpec s, double t)
        {
            double slide = Math.Sin(t * 1.2 + s.Seed * 6) * 3;

            var linePen = P(s.LightBrush, 2);
            for (int i = 0; i < 3; i++)
            {
                double y = 78 + i * 5;
                dc.DrawGeometry(null, linePen, Geo(g =>
                {
                    g.BeginFigure(new Point(16 + slide, y), false, false);
                    g.QuadraticBezierTo(new Point(30 + slide, y - 4), new Point(44 + slide, y), true, false);
                    g.QuadraticBezierTo(new Point(58 + slide, y + 4), new Point(72 + slide, y), true, false);
                }));
            }

            double bob = Math.Sin(t * 1.3 + s.Seed * 6) * 1.6;
            dc.PushTransform(new TranslateTransform(0, bob));

            var body = Geo(g =>
            {
                g.BeginFigure(new Point(18, 62), true, true);
                g.QuadraticBezierTo(new Point(21, 40), new Point(43, 36), true, false);
                g.QuadraticBezierTo(new Point(63, 32), new Point(71, 45), true, false);
                g.QuadraticBezierTo(new Point(82, 58), new Point(72, 70), true, false);
                g.QuadraticBezierTo(new Point(62, 81), new Point(47, 78), true, false);
                g.QuadraticBezierTo(new Point(28, 76), new Point(18, 62), true, false);
            });
            dc.DrawGeometry(s.BodyBrush, null, body);

            var foam = P(s.LightBrush, 3);
            dc.DrawGeometry(null, foam, Geo(g =>
            {
                g.BeginFigure(new Point(24, 54), false, false);
                g.QuadraticBezierTo(new Point(37, 40), new Point(53, 42), true, false);
            }));

            BeadEyes(dc, s, 57, 8, 3.1, Blink(t, 4.5, s.Seed));
            Smile(dc, P(s.InkBrush, 1.5), 50, 65, 8, 3);

            double drop = Math.Sin(t * 3 + s.Seed * 6);
            Circle(dc, s.LightBrush, 34, 27 + drop * 2.5, 3);
            Circle(dc, s.LightBrush, 66, 23 - drop * 2.5, 2.4);
            dc.Pop();
        }

        private static void Crystal(DrawingContext dc, AvatarSpec s, double t)
        {
            double rot = Math.Sin(t * 0.8 + s.Seed * 6) * 2.5;
            dc.PushTransform(new RotateTransform(rot, 50, 52));

            Poly(dc, s.BodyBrush, null,
                new Point(50, 11), new Point(78, 36), new Point(70, 76), new Point(50, 89), new Point(30, 76), new Point(22, 36));

            dc.PushOpacity(0.55);
            Poly(dc, s.LightBrush, null, new Point(50, 11), new Point(22, 36), new Point(30, 76), new Point(50, 89));
            dc.Pop();

            dc.PushOpacity(0.16);
            Poly(dc, s.DarkBrush, null, new Point(50, 11), new Point(78, 36), new Point(70, 76), new Point(50, 89));
            dc.Pop();

            dc.DrawLine(P(White, 1.1), new Point(50, 12), new Point(50, 88));

            double shine = 0.12 + 0.25 * (0.5 + 0.5 * Math.Sin(t * 2.4 + s.Seed * 6));
            dc.PushOpacity(shine);
            Poly(dc, White, null, new Point(50, 11), new Point(50, 89), new Point(22, 36));
            dc.Pop();

            BeadEyes(dc, s, 50, 8, 3.1, Blink(t, 4.7, s.Seed));
            Smile(dc, P(s.InkBrush, 1.4), 50, 58, 7.5, 2.8);
            Blush(dc, 55, 14);
            dc.Pop();

            Sparkle(dc, 20, 22, 2, t * 1.5 + s.Seed * 6);
            Sparkle(dc, 81, 68, 1.6, t * 1.2 + s.Seed * 5);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  KARAKTERLER
        // ═════════════════════════════════════════════════════════════════════

        private static void Ghost(DrawingContext dc, AvatarSpec s, double t)
        {
            double bob = Math.Sin(t * 1.1 + s.Seed * 6) * 2.5;
            dc.PushTransform(new TranslateTransform(0, bob));

            double w = 2.4 * Math.Sin(t * 3 + s.Seed * 6);
            var body = Geo(g =>
            {
                g.BeginFigure(new Point(26, 50), true, true);
                g.ArcTo(new Point(74, 50), new Size(24, 24), 0, false, SweepDirection.Clockwise, true, false);
                g.LineTo(new Point(74, 74), true, false);
                g.QuadraticBezierTo(new Point(68, 82 + w), new Point(62, 74), true, false);
                g.QuadraticBezierTo(new Point(56, 82 - w), new Point(50, 74), true, false);
                g.QuadraticBezierTo(new Point(44, 82 + w), new Point(38, 74), true, false);
                g.QuadraticBezierTo(new Point(32, 82 - w), new Point(26, 74), true, false);
                g.LineTo(new Point(26, 50), true, false);
            });
            dc.DrawGeometry(s.BodyBrush, null, body);

            BeadEyes(dc, s, 50, 9, 3.6, Blink(t, 4.3, s.Seed));
            Smile(dc, P(s.InkBrush, 1.5), 50, 57, 8, 3);
            Blush(dc, 61, 17);

            Sparkle(dc, 20, 30, 1.8, t * 1.4 + s.Seed * 6);
            dc.Pop();
        }

        private static void Ninja(DrawingContext dc, AvatarSpec s, double t)
        {
            double sway = Math.Sin(t * 1.6 + s.Seed * 6) * 7;

            dc.PushTransform(new RotateTransform(sway, 74, 40));
            Poly(dc, s.AccentBrush, null, new Point(74, 37), new Point(95, 32), new Point(86, 44));
            Poly(dc, s.DarkBrush, null, new Point(74, 41), new Point(93, 46), new Point(84, 52));
            dc.Pop();

            Circle(dc, s.BodyBrush, 50, 52, 28);
            RoundRect(dc, s.AccentBrush, null, new Rect(21, 35, 58, 7), 3.5, 3.5);
            RoundRect(dc, White, null, new Rect(30, 45, 40, 14), 7, 7);

            BeadEyes(dc, s, 52, 8, 3.2, Blink(t, 4.4, s.Seed + 0.2));
            Circle(dc, s.AccentBrush, 76, 39, 3);
        }

        private static void Wizard(DrawingContext dc, AvatarSpec s, double t)
        {
            double bob = Math.Sin(t * 1.2 + s.Seed * 6) * 1.2;
            dc.PushTransform(new TranslateTransform(0, bob));

            Poly(dc, s.BodyBrush, null, new Point(50, 4), new Point(64, 16), new Point(68, 34), new Point(32, 34), new Point(37, 16));
            Ellipse(dc, s.BodyBrush, 50, 34, 30, 6);
            RoundRect(dc, s.DarkBrush, null, new Rect(36, 28.5, 28, 5), 2.5, 2.5);

            double tw = 0.65 + 0.35 * Math.Sin(t * 3 + s.Seed * 6);
            dc.PushOpacity(tw);
            Poly(dc, White, null, StarPoints(46, 20, 5, 2, 5, -90));
            dc.Pop();

            Circle(dc, s.AccentBrush, 50, 56, 20);

            double wave = Math.Sin(t * 1.4 + s.Seed * 6) * 2.5;
            var beard = Geo(g =>
            {
                g.BeginFigure(new Point(32, 58), true, true);
                g.QuadraticBezierTo(new Point(36, 70 + wave), new Point(42, 72), true, false);
                g.QuadraticBezierTo(new Point(46, 84), new Point(50, 87), true, false);
                g.QuadraticBezierTo(new Point(54, 84), new Point(58, 72), true, false);
                g.QuadraticBezierTo(new Point(64, 70 - wave), new Point(68, 58), true, false);
                g.QuadraticBezierTo(new Point(50, 66), new Point(32, 58), true, false);
            });
            dc.DrawGeometry(White, null, beard);

            BeadEyes(dc, s, 52, 8, 3.2, Blink(t, 4.6, s.Seed));
            dc.Pop();
        }

        private static void Knight(DrawingContext dc, AvatarSpec s, double t)
        {
            double wave = Math.Sin(t * 1.5 + s.Seed * 6) * 8;

            dc.PushTransform(new RotateTransform(wave, 54, 22));
            var plume = Geo(g =>
            {
                g.BeginFigure(new Point(52, 26), true, true);
                g.QuadraticBezierTo(new Point(60, 6), new Point(76, 9), true, false);
                g.QuadraticBezierTo(new Point(66, 16), new Point(64, 27), true, false);
            });
            dc.DrawGeometry(s.AccentBrush, null, plume);
            dc.Pop();

            var helmet = new Rect(30, 24, 40, 50);
            RoundRect(dc, s.BodyBrush, null, helmet, 18, 18);
            dc.PushClip(new RectangleGeometry(helmet, 18, 18));
            double sx = 24 + ((t * 16) % 54);
            dc.PushTransform(new RotateTransform(-18, 50, 48));
            dc.DrawRectangle(WhiteFade, null, new Rect(sx, 10, 9, 80));
            dc.Pop();
            dc.Pop();

            RoundRect(dc, s.InkBrush, null, new Rect(36, 44, 28, 9), 4.5, 4.5);

            double pulse = 0.55 + 0.45 * Math.Sin(t * 2.2 + s.Seed * 6);
            dc.PushOpacity(pulse);
            Circle(dc, s.AccentBrush, 43, 48.5, 2.1);
            Circle(dc, s.AccentBrush, 57, 48.5, 2.1);
            dc.Pop();

            foreach (var pt in new[] { new Point(36, 34), new Point(64, 34), new Point(36, 64), new Point(64, 64) })
                Circle(dc, s.DarkBrush, pt.X, pt.Y, 1.8);

            dc.DrawLine(P(s.DarkBrush, 1.6), new Point(43, 66), new Point(57, 66));
        }

        // ═════════════════════════════════════════════════════════════════════
        //  ÇİZİM YARDIMCILARI
        // ═════════════════════════════════════════════════════════════════════

        private static double Blink(double t, double period, double phase)
        {
            double p = (t / period + phase) % 1.0;
            const double window = 0.075;
            if (p > window) return 1;

            double k = p / window;
            double closed = 1 - Math.Abs(k * 2 - 1);
            return 1 - closed * 0.92;
        }

        private static void BeadEyes(DrawingContext dc, AvatarSpec s, double y, double gap,
                                     double r, double open, double cx = 50)
        {
            double ry = Math.Max(0.6, r * open);
            foreach (double sign in new[] { -1.0, 1.0 })
            {
                var c = new Point(cx + sign * gap, y);
                dc.DrawEllipse(s.InkBrush, null, c, r, ry);
                if (open > 0.6)
                    Circle(dc, White, c.X + r * 0.32, y - ry * 0.34, Math.Max(0.9, r * 0.29));
            }
        }

        private static void ScleraEyes(DrawingContext dc, AvatarSpec s, double y, double gap,
                                       double rx, double ry, double pupil, double open)
        {
            double h = Math.Max(0.8, ry * open);
            foreach (double sign in new[] { -1.0, 1.0 })
            {
                var c = new Point(50 + sign * gap, y);
                dc.DrawEllipse(White, null, c, rx, h);
                dc.DrawEllipse(s.InkBrush, null, c, pupil, Math.Min(pupil, h * 0.92));
                if (open > 0.6)
                    Circle(dc, White, c.X - pupil * 0.35, y - pupil * 0.4, Math.Max(0.7, pupil * 0.3));
            }
        }

        private static void Blush(DrawingContext dc, double y, double gap, double cx = 50)
        {
            dc.DrawEllipse(BlushBrush, null, new Point(cx - gap, y), 4.4, 2.9);
            dc.DrawEllipse(BlushBrush, null, new Point(cx + gap, y), 4.4, 2.9);
        }

        private static void Smile(DrawingContext dc, Pen pen, double x, double y, double w, double h)
        {
            dc.DrawGeometry(null, pen, Geo(g =>
            {
                g.BeginFigure(new Point(x - w / 2, y), false, false);
                g.QuadraticBezierTo(new Point(x, y + h), new Point(x + w / 2, y), true, false);
            }));
        }

        private static void Sparkle(DrawingContext dc, double x, double y, double size, double phase)
        {
            double s = 0.45 + 0.55 * Math.Abs(Math.Sin(phase));
            dc.PushOpacity(s);
            var pen = P(White, 1.5);
            dc.DrawLine(pen, new Point(x - size, y), new Point(x + size, y));
            dc.DrawLine(pen, new Point(x, y - size), new Point(x, y + size));
            dc.Pop();
        }

        private static void Circle(DrawingContext dc, Brush brush, double x, double y, double r, Pen? pen = null)
            => dc.DrawEllipse(brush, pen, new Point(x, y), r, r);

        private static void Ellipse(DrawingContext dc, Brush brush, double x, double y, double rx, double ry)
            => dc.DrawEllipse(brush, null, new Point(x, y), rx, ry);

        private static void RoundRect(DrawingContext dc, Brush brush, Pen? pen, Rect rect, double rx, double ry)
            => dc.DrawRoundedRectangle(brush, pen, rect, rx, ry);

        private static void Poly(DrawingContext dc, Brush brush, Pen? pen, params Point[] points)
        {
            var geo = Geo(g =>
            {
                g.BeginFigure(points[0], true, true);
                for (int i = 1; i < points.Length; i++) g.LineTo(points[i], true, false);
            });
            dc.DrawGeometry(brush, pen, geo);
        }

        private static StreamGeometry Geo(Action<StreamGeometryContext> build)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
                build(ctx);
            geometry.Freeze();
            return geometry;
        }

        private static Pen P(Brush brush, double width, PenLineCap cap = PenLineCap.Round)
        {
            int key = (int)Math.Round(width * 10);
            if (!Pens.TryGetValue((brush, key, cap), out var pen))
            {
                pen = new Pen(brush, key / 10.0)
                {
                    StartLineCap = cap,
                    EndLineCap = cap,
                    LineJoin = PenLineJoin.Round
                };
                pen.Freeze();
                Pens[(brush, key, cap)] = pen;
            }
            return pen;
        }

        private static Point[] StarPoints(double cx, double cy, double outer, double inner, int n, double rotDeg)
        {
            var pts = new Point[n * 2];
            for (int i = 0; i < n * 2; i++)
            {
                double r = i % 2 == 0 ? outer : inner;
                double a = (rotDeg + i * 180.0 / n) * Math.PI / 180;
                pts[i] = new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            }
            return pts;
        }

        private static Color A(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        private static Brush FadeGrad(Color from, Color to, bool vertical)
        {
            if (!Grads.TryGetValue((from, to, vertical), out var brush))
            {
                brush = vertical
                    ? new LinearGradientBrush(from, to, new Point(0.5, 0), new Point(0.5, 1))
                    : new LinearGradientBrush(from, to, new Point(1, 0.5), new Point(0, 0.5));
                brush.Freeze();
                Grads[(from, to, vertical)] = brush;
            }
            return brush;
        }

        private static Brush Solid(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
