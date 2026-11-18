using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Dosyaktar
{
    // ═══════════════════════════════════════════════════════════════════════
    //  ThemePalette — one complete colour system for the whole application.
    // ═══════════════════════════════════════════════════════════════════════
    public sealed class ThemePalette
    {
        public string Id          { get; set; } = "prism";
        public string Name        { get; set; } = "Prism";
        public string Description { get; set; } = "Parlak cam";
        public bool   IsDark      { get; set; }

        public Color Background       { get; set; }
        public Color BackgroundAlt    { get; set; }
        public Color Surface          { get; set; }
        public Color Surface2         { get; set; }
        public Color Surface3         { get; set; }
        public Color Border           { get; set; }
        public Color BorderStrong     { get; set; }
        public Color Accent           { get; set; }
        public Color Accent2          { get; set; }
        public Color AccentHover      { get; set; }
        public Color Danger           { get; set; }
        public Color Success          { get; set; }
        public Color Warning          { get; set; }
        public Color TextPrimary      { get; set; }
        public Color TextSecondary    { get; set; }
        public Color TextMuted        { get; set; }
        public Color TabBar           { get; set; }
        public Color PeerCardHover    { get; set; }
        public Color DropZoneHighlight{ get; set; }
        public Color Glow             { get; set; }

        public Color AccentText => Colors.White;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ThemeManager — builds runtime resource dictionaries for 8 palettes.
    //  The theme dictionary always lives at MergedDictionaries[0].
    // ═══════════════════════════════════════════════════════════════════════
    public static class ThemeManager
    {
        public static event EventHandler<ThemePalette>? ThemeChanged;

        private static ThemePalette? _current;

        /// <summary>Aktif palet. Hiç uygulanmadıysa Prism'e düşer.</summary>
        public static ThemePalette Current => _current ?? Prism;

        private static ResourceDictionary? _themeDict;

        // ─── helpers ──────────────────────────────────────────────────────
        private static Color Hex(string value) => (Color)ColorConverter.ConvertFromString(value);

        private static double Channel(double c)
        {
            c /= 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        /// <summary>WCAG 2.1 bağıl parlaklık.</summary>
        internal static double Luminance(Color c) =>
            0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

        /// <summary>İki renk arasındaki WCAG kontrast oranı (1–21).</summary>
        internal static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            if (la < lb) (la, lb) = (lb, la);
            return (la + 0.05) / (lb + 0.05);
        }

        /// <summary>
        /// Metin olarak kullanılacak rengi, hedef kontrasta ulaşana kadar
        /// arka plana göre siyaha (açık tema) veya beyaza (koyu tema) kaydırır.
        /// Ton korunur; yalnızca gerektiği kadar koyulaşır/açılır.
        /// </summary>
        private static Color EnsureContrast(Color fg, Color bg, double target)
        {
            if (Contrast(fg, bg) >= target) return fg;

            Color toward = Luminance(bg) < 0.18 ? Colors.White : Colors.Black;

            for (int step = 1; step <= 40; step++)
            {
                var candidate = Mix(fg, toward, step / 40.0);
                if (Contrast(candidate, bg) >= target) return candidate;
            }

            return toward;
        }

        /// <summary>
        /// A colour that keeps its hue but is guaranteed readable on every
        /// surface it is used over. Used for text and icon-on-surface roles.
        /// </summary>
        private static Color Ink(Color fg, double target, params Color[] backgrounds)
        {
            Color result = fg;
            // A couple of passes settle the case where shifting for one
            // background slightly weakens another.
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var bg in backgrounds)
                    result = EnsureContrast(result, bg, target);
            }
            return result;
        }

        private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        private static Color Mix(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromArgb(
                (byte)(a.A + (b.A - a.A) * t),
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        private static SolidColorBrush Brush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        // ══════════════════════════════════════════════════════════════════
        //  PALETTES
        // ══════════════════════════════════════════════════════════════════

        public static readonly ThemePalette Prism = new()
        {
            Id = "prism", Name = "Prism", Description = "Parlak cam", IsDark = false,
            Background        = Hex("#F7F5FF"),
            BackgroundAlt     = Hex("#EEEAFF"),
            Surface           = Hex("#FFFFFF"),
            Surface2          = Hex("#F1EDFF"),
            Surface3          = Hex("#E6DEFF"),
            Border            = Hex("#E3DBFB"),
            BorderStrong      = Hex("#CFC3F5"),
            Accent            = Hex("#6C3BFF"),
            Accent2           = Hex("#0FC9DE"),
            AccentHover       = Hex("#8259FF"),
            Danger            = Hex("#F43F5E"),
            Success           = Hex("#0FAE7D"),
            Warning           = Hex("#F0A020"),
            TextPrimary       = Hex("#14112B"),
            TextSecondary     = Hex("#5B577A"),
            TextMuted         = Hex("#9691B2"),
            TabBar            = Hex("#FBF9FF"),
            PeerCardHover     = Hex("#F4F0FF"),
            DropZoneHighlight = Hex("#EDE7FF"),
            Glow              = Hex("#6C3BFF")
        };

        public static readonly ThemePalette Midnight = new()
        {
            Id = "midnight", Name = "Midnight", Description = "Derin mavi", IsDark = true,
            Background        = Hex("#0B1020"),
            BackgroundAlt     = Hex("#0E1529"),
            Surface           = Hex("#121A33"),
            Surface2          = Hex("#18213E"),
            Surface3          = Hex("#1F2A4D"),
            Border            = Hex("#28325A"),
            BorderStrong      = Hex("#354272"),
            Accent            = Hex("#7C8CFF"),
            Accent2           = Hex("#22D3EE"),
            AccentHover       = Hex("#94A1FF"),
            Danger            = Hex("#FF6B81"),
            Success           = Hex("#34D399"),
            Warning           = Hex("#FBBF24"),
            TextPrimary       = Hex("#E6E9FF"),
            TextSecondary     = Hex("#A5ADD6"),
            TextMuted         = Hex("#6D77A6"),
            TabBar            = Hex("#0E1529"),
            PeerCardHover     = Hex("#1B2547"),
            DropZoneHighlight = Hex("#1A2344"),
            Glow              = Hex("#7C8CFF")
        };

        public static readonly ThemePalette Nordic = new()
        {
            Id = "nordic", Name = "Nordic", Description = "Buz mavisi", IsDark = false,
            Background        = Hex("#EEF6FF"),
            BackgroundAlt     = Hex("#E3F0FF"),
            Surface           = Hex("#FFFFFF"),
            Surface2          = Hex("#E7F2FF"),
            Surface3          = Hex("#D6E8FF"),
            Border            = Hex("#D2E4F7"),
            BorderStrong      = Hex("#B5D2F0"),
            Accent            = Hex("#1E6FE0"),
            Accent2           = Hex("#38BDF8"),
            AccentHover       = Hex("#3B85F0"),
            Danger            = Hex("#E11D48"),
            Success           = Hex("#0D9488"),
            Warning           = Hex("#D97706"),
            TextPrimary       = Hex("#0D1B2E"),
            TextSecondary     = Hex("#48637E"),
            TextMuted         = Hex("#849BB4"),
            TabBar            = Hex("#F5FAFF"),
            PeerCardHover     = Hex("#EAF4FF"),
            DropZoneHighlight = Hex("#DDEEFF"),
            Glow              = Hex("#1E6FE0")
        };

        public static readonly ThemePalette Aurora = new()
        {
            Id = "aurora", Name = "Aurora", Description = "Kutup yeşili", IsDark = true,
            Background        = Hex("#071A16"),
            BackgroundAlt     = Hex("#08211C"),
            Surface           = Hex("#0D2620"),
            Surface2          = Hex("#123329"),
            Surface3          = Hex("#174034"),
            Border            = Hex("#1D4A3A"),
            BorderStrong      = Hex("#2A6350"),
            Accent            = Hex("#34D399"),
            Accent2           = Hex("#22D3EE"),
            AccentHover       = Hex("#4FE3AC"),
            Danger            = Hex("#FB7185"),
            Success           = Hex("#5EEAD4"),
            Warning           = Hex("#FDE047"),
            TextPrimary       = Hex("#E8FFF6"),
            TextSecondary     = Hex("#9ED3C1"),
            TextMuted         = Hex("#669B8A"),
            TabBar            = Hex("#08211C"),
            PeerCardHover     = Hex("#143A2F"),
            DropZoneHighlight = Hex("#123528"),
            Glow              = Hex("#34D399")
        };

        public static readonly ThemePalette Sunset = new()
        {
            Id = "sunset", Name = "Sunset", Description = "Sıcak kum", IsDark = false,
            Background        = Hex("#FFF6EE"),
            BackgroundAlt     = Hex("#FFEEE0"),
            Surface           = Hex("#FFFDFB"),
            Surface2          = Hex("#FFEEDF"),
            Surface3          = Hex("#FFE0C9"),
            Border            = Hex("#F8D9C4"),
            BorderStrong      = Hex("#F0C2A4"),
            Accent            = Hex("#F97316"),
            Accent2           = Hex("#EC4899"),
            AccentHover       = Hex("#FB8B3C"),
            Danger            = Hex("#DC2626"),
            Success           = Hex("#0F9D70"),
            Warning           = Hex("#CA8A04"),
            TextPrimary       = Hex("#2B1409"),
            TextSecondary     = Hex("#85563F"),
            TextMuted         = Hex("#B98D74"),
            TabBar            = Hex("#FFFBF7"),
            PeerCardHover     = Hex("#FFF0E4"),
            DropZoneHighlight = Hex("#FFE7D4"),
            Glow              = Hex("#F97316")
        };

        public static readonly ThemePalette Cyber = new()
        {
            Id = "cyber", Name = "Cyber", Description = "Neon gece", IsDark = true,
            Background        = Hex("#0B0716"),
            BackgroundAlt     = Hex("#0E0A1D"),
            Surface           = Hex("#150D28"),
            Surface2          = Hex("#1D1136"),
            Surface3          = Hex("#271748"),
            Border            = Hex("#3A2160"),
            BorderStrong      = Hex("#523186"),
            Accent            = Hex("#E879F9"),
            Accent2           = Hex("#22D3EE"),
            AccentHover       = Hex("#F092FF"),
            Danger            = Hex("#FF4D6D"),
            Success           = Hex("#2DD4BF"),
            Warning           = Hex("#FBBF24"),
            TextPrimary       = Hex("#F6E9FF"),
            TextSecondary     = Hex("#BE9EE8"),
            TextMuted         = Hex("#8466AB"),
            TabBar            = Hex("#0E0A1D"),
            PeerCardHover     = Hex("#211340"),
            DropZoneHighlight = Hex("#1E1138"),
            Glow              = Hex("#E879F9")
        };

        public static readonly ThemePalette Forest = new()
        {
            Id = "forest", Name = "Forest", Description = "Orman", IsDark = false,
            Background        = Hex("#F2FBF4"),
            BackgroundAlt     = Hex("#E8F6EB"),
            Surface           = Hex("#FFFFFF"),
            Surface2          = Hex("#E8F6EC"),
            Surface3          = Hex("#D8EEDE"),
            Border            = Hex("#D2E8D8"),
            BorderStrong      = Hex("#B0D7BC"),
            Accent            = Hex("#15803D"),
            Accent2           = Hex("#84CC16"),
            AccentHover       = Hex("#1E9A4C"),
            Danger            = Hex("#DC2626"),
            Success           = Hex("#15803D"),
            Warning           = Hex("#B45309"),
            TextPrimary       = Hex("#0D2318"),
            TextSecondary     = Hex("#476B56"),
            TextMuted         = Hex("#84A390"),
            TabBar            = Hex("#F8FDF9"),
            PeerCardHover     = Hex("#EDF9F0"),
            DropZoneHighlight = Hex("#DFF3E5"),
            Glow              = Hex("#15803D")
        };

        public static readonly ThemePalette Graphite = new()
        {
            Id = "graphite", Name = "Graphite", Description = "Saf gri", IsDark = false,
            Background        = Hex("#F3F4F6"),
            BackgroundAlt     = Hex("#EBECEF"),
            Surface           = Hex("#FFFFFF"),
            Surface2          = Hex("#EAEBEF"),
            Surface3          = Hex("#DEE0E6"),
            Border            = Hex("#D8DAE1"),
            BorderStrong      = Hex("#BEC2CC"),
            Accent            = Hex("#3F4654"),
            Accent2           = Hex("#8B93A5"),
            AccentHover       = Hex("#525A6B"),
            Danger            = Hex("#DC2626"),
            Success           = Hex("#059669"),
            Warning           = Hex("#B45309"),
            TextPrimary       = Hex("#14161C"),
            TextSecondary     = Hex("#545A68"),
            TextMuted         = Hex("#8D93A1"),
            TabBar            = Hex("#F8F9FA"),
            PeerCardHover     = Hex("#EDEEF2"),
            DropZoneHighlight = Hex("#E4E6EB"),
            Glow              = Hex("#525A6B")
        };

        private static readonly ThemePalette[] _all =
        {
            Prism, Midnight, Nordic, Aurora, Sunset, Cyber, Forest, Graphite
        };

        public static IReadOnlyList<ThemePalette> Palettes => _all;

        // ══════════════════════════════════════════════════════════════════
        //  RESOLUTION
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Maps legacy theme tags (v1.x) onto the current palette ids.</summary>
        public static string Normalize(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return Prism.Id;

            return id.Trim().ToLowerInvariant() switch
            {
                "crimson"    => Prism.Id,
                "cyberpunk"  => Cyber.Id,
                "grey" or "gray" => Graphite.Id,
                "light"      => Prism.Id,
                "dark"       => Midnight.Id,
                var other when _all.Any(p => p.Id == other) => other,
                _ => Prism.Id
            };
        }

        public static ThemePalette Find(string? id)
        {
            string key = Normalize(id);
            return _all.FirstOrDefault(p => p.Id == key) ?? Prism;
        }

        public static ThemePalette? Next(ThemePalette from)
        {
            int i = Array.IndexOf(_all, from);
            if (i < 0) return null;
            return _all[(i + 1) % _all.Length];
        }

        // ══════════════════════════════════════════════════════════════════
        //  APPLY
        // ══════════════════════════════════════════════════════════════════

        public static void Apply(string? id) => Apply(Find(id));

        public static void Apply(ThemePalette palette)
        {
            if (palette == null) return;

            _current = palette;

            var dict = Build(palette);
            var merged = Application.Current?.Resources.MergedDictionaries;
            if (merged != null)
            {
                if (merged.Count == 0) merged.Add(dict);
                else merged[0] = dict;
            }

            _themeDict = dict;
            ThemeChanged?.Invoke(null, palette);
        }

        public static void Toggle()
        {
            var next = Next(Current);
            if (next != null) Apply(next);
        }

        // ══════════════════════════════════════════════════════════════════
        //  DICTIONARY BUILDER
        // ══════════════════════════════════════════════════════════════════

        private static ResourceDictionary Build(ThemePalette p)
        {
            var d = new ResourceDictionary();

            bool dark = p.IsDark;
            Color onAccent = Colors.White;

            // ── raw colours ────────────────────────────────────────────────
            void Col(string key, Color c) => d[key] = c;

            Col("BackgroundColor",        p.Background);
            Col("BackgroundAltColor",     p.BackgroundAlt);
            Col("SurfaceColor",           p.Surface);
            Col("Surface2Color",          p.Surface2);
            Col("Surface3Color",          p.Surface3);
            Col("BorderColor",            p.Border);
            Col("BorderStrongColor",      p.BorderStrong);
            Col("AccentColor",            p.Accent);
            Col("Accent2Color",           p.Accent2);
            Col("AccentHoverColor",       p.AccentHover);
            Col("DangerColor",            p.Danger);
            Col("SuccessColor",           p.Success);
            Col("WarningColor",           p.Warning);
            Col("TextPrimary",            p.TextPrimary);
            Col("TextSecondary",          p.TextSecondary);
            Col("TextMuted",              p.TextMuted);
            Col("TabBarColor",            p.TabBar);
            Col("PeerCardHoverColor",     p.PeerCardHover);
            Col("DropZoneHighlightColor", p.DropZoneHighlight);

            // ── brushes ────────────────────────────────────────────────────
            void Bru(string key, Color c) => d[key] = Brush(c);

            Bru("BackgroundBrush",        p.Background);
            Bru("BackgroundAltBrush",     p.BackgroundAlt);
            Bru("SurfaceBrush",           p.Surface);
            Bru("Surface2Brush",          p.Surface2);
            Bru("Surface3Brush",          p.Surface3);
            Bru("BorderBrush",            p.Border);
            Bru("BorderStrongBrush",      p.BorderStrong);
            Bru("AccentBrush",            p.Accent);
            Bru("Accent2Brush",           p.Accent2);
            Bru("AccentHoverBrush",       p.AccentHover);
            Bru("DangerBrush",            p.Danger);
            Bru("SuccessBrush",           p.Success);
            Bru("WarningBrush",           p.Warning);
            Bru("TextPrimaryBrush",       p.TextPrimary);
            Bru("TextSecBrush",           p.TextSecondary);
            Bru("TextSecondaryBrush",     p.TextSecondary);
            Bru("TextMutedBrush",         p.TextMuted);
            Bru("TabBarBrush",            p.TabBar);
            Bru("PeerCardHoverBrush",     p.PeerCardHover);
            Bru("DropZoneHighlightBrush", p.DropZoneHighlight);

            // ── readable-on-surface ink roles ──────────────────────────────
            // Solid accents are tuned for fills; used as text they can fall
            // under 4.5:1 (orange on cream, mint on white). These variants
            // keep the hue but are guaranteed readable on every surface.
            var inkBgs = new[] { p.Background, p.Surface, p.Surface2, p.Surface3 };

            Color accentInk  = Ink(p.Accent,  4.6, inkBgs);
            Color successInk = Ink(p.Success, 4.6, inkBgs);
            Color dangerInk  = Ink(p.Danger,  4.6, inkBgs);
            Color warningInk = Ink(p.Warning, 4.6, inkBgs);
            Color mutedInk   = Ink(p.TextMuted, 4.6, inkBgs);
            Color secInk     = Ink(p.TextSecondary, 6.0, inkBgs);

            Col("AccentInkColor",  accentInk);
            Col("SuccessInkColor", successInk);
            Col("DangerInkColor",  dangerInk);
            Col("WarningInkColor", warningInk);
            Col("MutedInkColor",   mutedInk);

            Bru("AccentInkBrush",  accentInk);
            Bru("SuccessInkBrush", successInk);
            Bru("DangerInkBrush",  dangerInk);
            Bru("WarningInkBrush", warningInk);

            // The muted / secondary text roles point at the contrast-safe
            // values so every existing binding inherits the fix.
            Bru("TextMutedBrush",     mutedInk);
            Bru("TextSecBrush",       secInk);
            Bru("TextSecondaryBrush", secInk);

            // ── fill roles (solid colour with white label on top) ──────────
            Color accentFill  = EnsureContrast(p.Accent,  Colors.White, 4.6);
            Color accent2Fill = EnsureContrast(p.Accent2, Colors.White, 4.6);
            Color dangerFill  = EnsureContrast(p.Danger,  Colors.White, 4.6);
            Color successFill = EnsureContrast(p.Success, Colors.White, 4.6);
            Color warningFill = EnsureContrast(p.Warning, Colors.White, 4.6);

            Col("AccentFillColor",  accentFill);
            Col("Accent2FillColor", accent2Fill);
            Col("DangerFillColor",  dangerFill);
            Col("SuccessFillColor", successFill);
            Col("WarningFillColor", warningFill);

            Bru("AccentFillBrush",  accentFill);
            Bru("Accent2FillBrush", accent2Fill);
            Bru("DangerFillBrush",  dangerFill);
            Bru("SuccessFillBrush", successFill);
            Bru("WarningFillBrush", warningFill);

            // accent on colour
            Bru("AccentTextBrush", onAccent);
            d["AccentTextColor"] = onAccent;

            // soft accent fills (badges, chips, selected rows)
            Bru("AccentSoftBrush",      WithAlpha(p.Accent,  dark ? (byte)0x2E : (byte)0x1F));
            Bru("AccentSoft2Brush",     WithAlpha(p.Accent2, dark ? (byte)0x2A : (byte)0x1A));
            Bru("AccentGhostBrush",     WithAlpha(p.Accent,  dark ? (byte)0x1A : (byte)0x12));
            Bru("AccentBorderBrush",    WithAlpha(p.Accent,  dark ? (byte)0x66 : (byte)0x4D));
            Bru("SuccessSoftBrush",     WithAlpha(p.Success, dark ? (byte)0x2E : (byte)0x1C));
            Bru("DangerSoftBrush",      WithAlpha(p.Danger,  dark ? (byte)0x2E : (byte)0x1C));
            Bru("WarningSoftBrush",     WithAlpha(p.Warning, dark ? (byte)0x2E : (byte)0x1C));

            // glass system
            Bru("GlassBrush",       WithAlpha(p.Surface,       dark ? (byte)0xB8 : (byte)0xD9));
            Bru("GlassStrongBrush", WithAlpha(p.Surface,       dark ? (byte)0xD4 : (byte)0xF0));
            Bru("GlassBorderBrush", dark ? WithAlpha(p.BorderStrong, 0xB0) : WithAlpha(Colors.White, 0xF2));
            Bru("GlassHiBrush",     dark ? WithAlpha(Colors.White, 0x1C)  : WithAlpha(Colors.White, 0xE8));
            Bru("GlassLoBrush",     dark ? WithAlpha(Colors.Black, 0x2A)  : WithAlpha(p.Border, 0x30));

            // tinted glass — used for the hero band and accent surfaces
            Bru("AccentGlassBrush", WithAlpha(p.Accent, dark ? (byte)0x24 : (byte)0x14));

            // ── game provider tints (library cards, badges) ────────────────
            byte tintA = dark ? (byte)0x3C : (byte)0x1F;
            Bru("SteamTintBrush",    WithAlpha(Hex("#1B6FB5"), tintA));
            Bru("EpicTintBrush",     WithAlpha(Hex("#3A3A44"), tintA));
            Bru("GogTintBrush",      WithAlpha(Hex("#8B46C8"), tintA));
            Bru("BattleNetTintBrush",WithAlpha(Hex("#148EFF"), tintA));
            Bru("UbisoftTintBrush",  WithAlpha(Hex("#6C5CE7"), tintA));
            Bru("EaTintBrush",       WithAlpha(Hex("#E4002B"), tintA));
            Bru("RiotTintBrush",     WithAlpha(Hex("#D32936"), tintA));
            Bru("FolderTintBrush",   WithAlpha(p.Accent,  tintA));

            Bru("SteamDotBrush",    Hex("#1B6FB5"));
            Bru("EpicDotBrush",     Hex("#3A3A44"));
            Bru("GogDotBrush",      Hex("#8B46C8"));
            Bru("BattleNetDotBrush",Hex("#148EFF"));
            Bru("UbisoftDotBrush",  Hex("#6C5CE7"));
            Bru("EaDotBrush",       Hex("#E4002B"));
            Bru("RiotDotBrush",     Hex("#D32936"));
            Bru("FolderDotBrush",   p.Accent);

            // chrome
            Bru("ChromeBrush",     WithAlpha(p.Background, dark ? (byte)0xE0 : (byte)0xDC));
            Bru("OverlayBrush",    dark ? WithAlpha(Colors.Black, 0xC4) : WithAlpha(Hex("#1A1038"), 0x7A));
            Bru("DividerBrush",    dark ? WithAlpha(p.Border, 0xCC) : WithAlpha(p.BorderStrong, 0x80));
            Bru("ShadowBrush",     WithAlpha(dark ? Colors.Black : Hex("#2A1A6E"), dark ? (byte)0x66 : (byte)0x22));
            Bru("HighlightBrush",  dark ? WithAlpha(Colors.White, 0x24) : WithAlpha(Colors.White, 0xB0));
            Bru("ScrollThumbBrush", WithAlpha(p.TextMuted, 0x59));
            Bru("ScrollThumbHoverBrush", WithAlpha(p.TextMuted, 0x99));
            Bru("TextSelectionBrush", WithAlpha(p.Accent, dark ? (byte)0x66 : (byte)0x4A));

            // ── gradients ──────────────────────────────────────────────────
            var primary = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint   = new Point(1, 1)
            };
            // Both ends stay inside the accent's own hue family: a filled
            // button that drifts into the second accent reads muddy, and the
            // label has to stay legible over every stop of the ramp.
            Color primaryEnd = EnsureContrast(Mix(p.Accent, p.Accent2, 0.35), Colors.White, 4.6);
            primary.GradientStops.Add(new GradientStop(accentFill, 0.0));
            primary.GradientStops.Add(new GradientStop(Mix(accentFill, primaryEnd, 0.55), 0.58));
            primary.GradientStops.Add(new GradientStop(primaryEnd, 1.0));
            primary.Freeze();
            d["PrimaryGradientBrush"] = primary;

            var progress = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint   = new Point(1, 0.5)
            };
            progress.GradientStops.Add(new GradientStop(p.Success, 0.0));
            progress.GradientStops.Add(new GradientStop(p.Accent2, 1.0));
            progress.Freeze();
            d["ProgressGradientBrush"] = progress;

            var sheen = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint   = new Point(1, 1)
            };
            sheen.GradientStops.Add(new GradientStop(WithAlpha(Colors.White, dark ? (byte)0x14 : (byte)0xB8), 0.0));
            sheen.GradientStops.Add(new GradientStop(WithAlpha(Colors.White, 0x00), 0.55));
            sheen.Freeze();
            d["SheenBrush"] = sheen;

            var surfaceFade = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint   = new Point(0, 1)
            };
            surfaceFade.GradientStops.Add(new GradientStop(p.Surface, 0.0));
            surfaceFade.GradientStops.Add(new GradientStop(p.Surface2, 1.0));
            surfaceFade.Freeze();
            d["SurfaceFadeBrush"] = surfaceFade;

            var heroBand = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint   = new Point(1, 0)
            };
            heroBand.GradientStops.Add(new GradientStop(WithAlpha(p.Accent, dark ? (byte)0x33 : (byte)0x1C), 0.0));
            heroBand.GradientStops.Add(new GradientStop(WithAlpha(p.Accent2, dark ? (byte)0x22 : (byte)0x12), 0.55));
            heroBand.GradientStops.Add(new GradientStop(WithAlpha(p.Surface, 0x00), 1.0));
            heroBand.Freeze();
            d["HeroBandBrush"] = heroBand;

            // faceted prism faces (logo / landing motif)
            var f1 = new LinearGradientBrush(p.AccentHover, p.Accent, new Point(0, 0), new Point(1, 1));
            var f2 = new LinearGradientBrush(p.Accent2, Mix(p.Accent2, p.Accent, 0.35), new Point(0, 0), new Point(1, 1));
            var f3 = new LinearGradientBrush(Mix(p.Accent, p.Danger, 0.35), Mix(p.Accent, p.Accent2, 0.5), new Point(0, 0), new Point(1, 1));
            f1.Freeze(); f2.Freeze(); f3.Freeze();
            d["PrismFace1Brush"] = f1;
            d["PrismFace2Brush"] = f2;
            d["PrismFace3Brush"] = f3;

            // ambient glow behind hero elements
            var glow = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center         = new Point(0.5, 0.5),
                RadiusX        = 0.5,
                RadiusY        = 0.5
            };
            glow.GradientStops.Add(new GradientStop(WithAlpha(p.Glow, dark ? (byte)0x4A : (byte)0x30), 0.0));
            glow.GradientStops.Add(new GradientStop(WithAlpha(p.Glow, 0x00), 1.0));
            glow.Freeze();
            d["AccentGlowBrush"] = glow;

            var glow2 = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center         = new Point(0.5, 0.5),
                RadiusX        = 0.5,
                RadiusY        = 0.5
            };
            glow2.GradientStops.Add(new GradientStop(WithAlpha(p.Accent2, dark ? (byte)0x40 : (byte)0x28), 0.0));
            glow2.GradientStops.Add(new GradientStop(WithAlpha(p.Accent2, 0x00), 1.0));
            glow2.Freeze();
            d["Accent2GlowBrush"] = glow2;

            // ── atmosphere: page wash, mesh grid, cursor light ────────────
            var atmosphere = new LinearGradientBrush
            {
                StartPoint = new Point(0.1, 0),
                EndPoint   = new Point(0.9, 1)
            };
            atmosphere.GradientStops.Add(new GradientStop(Mix(p.Background, Colors.White, dark ? 0.05 : 0.6), 0.0));
            atmosphere.GradientStops.Add(new GradientStop(p.Background, 0.48));
            atmosphere.GradientStops.Add(new GradientStop(Mix(p.Background, p.Accent2, dark ? 0.16 : 0.11), 1.0));
            atmosphere.Freeze();
            d["AtmosphereBrush"] = atmosphere;

            // Tiled hairline grid, faded by an opacity mask in the window.
            var meshPen = new Pen(Brush(WithAlpha(p.BorderStrong, dark ? (byte)0x16 : (byte)0x0F)), 1);
            meshPen.Freeze();
            var meshTile = new GeometryDrawing(null, meshPen, new RectangleGeometry(new Rect(0, 0, 72, 72)));
            meshTile.Freeze();
            var mesh = new DrawingBrush(meshTile)
            {
                TileMode       = TileMode.Tile,
                Viewport       = new Rect(0, 0, 72, 72),
                ViewportUnits  = BrushMappingMode.Absolute,
                Stretch        = Stretch.None
            };
            mesh.Freeze();
            d["MeshBrush"] = mesh;

            var cursorGlow = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center         = new Point(0.5, 0.5),
                RadiusX        = 0.5,
                RadiusY        = 0.5
            };
            cursorGlow.GradientStops.Add(new GradientStop(WithAlpha(p.Accent, dark ? (byte)0x3A : (byte)0x2A), 0.0));
            cursorGlow.GradientStops.Add(new GradientStop(WithAlpha(p.Accent, dark ? (byte)0x18 : (byte)0x12), 0.55));
            cursorGlow.GradientStops.Add(new GradientStop(WithAlpha(p.Accent, 0x00), 1.0));
            cursorGlow.Freeze();
            d["CursorGlowBrush"] = cursorGlow;

            // ── material: card face gradient + active nav pill ───────────
            var cardFace = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint   = new Point(1, 1)
            };
            cardFace.GradientStops.Add(new GradientStop(
                WithAlpha(Mix(p.Surface, Colors.White, dark ? 0.08 : 0.75), dark ? (byte)0xE8 : (byte)0xF6), 0.0));
            cardFace.GradientStops.Add(new GradientStop(
                WithAlpha(Mix(p.Surface, p.Accent2, dark ? 0.09 : 0.065), dark ? (byte)0xD4 : (byte)0xEB), 1.0));
            cardFace.Freeze();
            d["CardFaceBrush"] = cardFace;

            var navActive = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint   = new Point(1, 1)
            };
            navActive.GradientStops.Add(new GradientStop(WithAlpha(accentFill, 0xF2), 0.0));
            navActive.GradientStops.Add(new GradientStop(WithAlpha(accent2Fill, 0xF2), 1.0));
            navActive.Freeze();
            d["NavActiveBrush"] = navActive;

            // ── effects ────────────────────────────────────────────────────
            var cardShadow = new DropShadowEffect
            {
                Color       = dark ? Colors.Black : Hex("#2A1A6E"),
                BlurRadius  = 46,
                ShadowDepth = 14,
                Direction   = 270,
                Opacity     = dark ? 0.50 : 0.22
            };
            cardShadow.Freeze();
            d["CardShadow"] = cardShadow;

            var softShadow = new DropShadowEffect
            {
                Color       = dark ? Colors.Black : Hex("#2A1A6E"),
                BlurRadius  = 18,
                ShadowDepth = 4,
                Direction   = 270,
                Opacity     = dark ? 0.34 : 0.10
            };
            softShadow.Freeze();
            d["SoftShadow"] = softShadow;

            var accentShadow = new DropShadowEffect
            {
                Color       = p.Accent,
                BlurRadius  = 28,
                ShadowDepth = 6,
                Direction   = 270,
                Opacity     = dark ? 0.55 : 0.36
            };
            accentShadow.Freeze();
            d["AccentShadow"] = accentShadow;

            // ── metadata for the UI ────────────────────────────────────────
            d["ThemeName"]        = p.Name;
            d["ThemeId"]          = p.Id;
            d["ThemeDescription"] = p.Description;
            d["ThemeIsDark"]      = p.IsDark;

            return d;
        }
    }
}
