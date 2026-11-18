using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Dosyaktar.Services
{
    /// <summary>
    /// Tek bir avatar kimliği: palet, çizim tarifi ve animasyon fazı.
    /// Renkler tema paletinden bağımsızdır — avatarlar her temada aynı karakteri taşır.
    /// </summary>
    public sealed class AvatarSpec
    {
        public string Id       { get; }
        public string Name     { get; }
        public string Category { get; }
        public int    Recipe   { get; }
        public double Seed     { get; }

        public Color BgA    { get; }
        public Color BgB    { get; }
        public Color Body   { get; }
        public Color Dark   { get; }
        public Color Accent { get; }
        public Color Ink    { get; }

        public Brush BgBrush     { get; }
        public Brush BodyBrush   { get; }
        public Brush DarkBrush   { get; }
        public Brush AccentBrush { get; }
        public Brush InkBrush    { get; }
        public Brush LightBrush  { get; }
        public Brush GlossBrush  { get; }
        public Brush SoftInkBrush { get; }

        public AvatarSpec(string id, string name, string category, int recipe,
                          uint bgA, uint bgB, uint body, uint dark, uint accent, uint ink)
        {
            Id = id; Name = name; Category = category; Recipe = recipe;
            Seed = (recipe * 0.61803398875) % 1.0;

            BgA    = FromRgb(bgA);
            BgB    = FromRgb(bgB);
            Body   = FromRgb(body);
            Dark   = FromRgb(dark);
            Accent = FromRgb(accent);
            Ink    = FromRgb(ink);

            BgBrush     = Freeze(new LinearGradientBrush(BgA, BgB, new Point(0.12, 0), new Point(0.88, 1)));
            BodyBrush   = Freeze(new SolidColorBrush(Body));
            DarkBrush   = Freeze(new SolidColorBrush(Dark));
            AccentBrush = Freeze(new SolidColorBrush(Accent));
            InkBrush    = Freeze(new SolidColorBrush(Ink));
            LightBrush  = Freeze(new SolidColorBrush(Lighten(Body, 0.66)));

            var softInk = Ink;
            softInk.A = 120;
            SoftInkBrush = Freeze(new SolidColorBrush(softInk));

            var gloss = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.30, 0.16),
                Center         = new Point(0.30, 0.16),
                RadiusX        = 0.78,
                RadiusY        = 0.62
            };
            gloss.GradientStops.Add(new GradientStop(Color.FromArgb(58, 255, 255, 255), 0));
            gloss.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            GlossBrush = Freeze(gloss);
        }

        internal static Color FromRgb(uint rgb) =>
            Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        internal static Color Lighten(Color c, double amount)
        {
            byte Mix(byte v) => (byte)Math.Round(v + (255 - v) * amount);
            return Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B));
        }

        private static Brush Freeze(Brush b) { b.Freeze(); return b; }
    }

    /// <summary>32 animasyonlu avatarın kataloğu ve kategori listesi.</summary>
    public static class AvatarCatalog
    {
        public const string DefaultId = "kedi";

        public static readonly string[] Categories =
            { "Tümü", "Hayvanlar", "Uzay", "Doğa", "Karakterler" };

        public static readonly IReadOnlyList<AvatarSpec> All = Build();

        public static AvatarSpec Find(string? id)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                foreach (var spec in All)
                    if (string.Equals(spec.Id, id, StringComparison.Ordinal))
                        return spec;
            }
            return All[0];
        }

        private static IReadOnlyList<AvatarSpec> Build() => new[]
        {
            // ── Hayvanlar ────────────────────────────────────────────────────
            new AvatarSpec("kedi", "Kedi", "Hayvanlar", 0,
                0xFFF3E0, 0xFFD3A1, 0xF6A24A, 0xDE7E2A, 0xFFB3C6, 0x4A2B14),
            new AvatarSpec("tilki", "Tilki", "Hayvanlar", 1,
                0xFFE6D6, 0xFFBFA6, 0xF2793D, 0xC95623, 0xFFF6EE, 0x4A2110),
            new AvatarSpec("baykus", "Baykuş", "Hayvanlar", 2,
                0xEDE6FF, 0xD8CBFF, 0xA98AE0, 0x8466C4, 0xFFB84D, 0x352A55),
            new AvatarSpec("panda", "Panda", "Hayvanlar", 3,
                0xE8F5EE, 0xCFEAD9, 0xFFFFFF, 0x2E3440, 0xFFB6C8, 0x23272F),
            new AvatarSpec("ayi", "Ayı", "Hayvanlar", 4,
                0xF2E3D5, 0xE2C6AC, 0xB98055, 0x97623C, 0xF0D9C2, 0x3E2A1B),
            new AvatarSpec("tavsan", "Tavşan", "Hayvanlar", 5,
                0xFFE6F0, 0xFFC7DD, 0xFFF8FB, 0xF0CBD9, 0xFF9EC4, 0x5A2C3E),
            new AvatarSpec("kurbaga", "Kurbağa", "Hayvanlar", 6,
                0xDDF5DC, 0xB9E8B6, 0x6FCB6A, 0x4FA84B, 0xFFD166, 0x23411F),
            new AvatarSpec("penguen", "Penguen", "Hayvanlar", 7,
                0xDFF1FB, 0xB9DFF5, 0x2F3A4F, 0x1F2735, 0xFFAA3C, 0x1B2230),
            new AvatarSpec("koala", "Koala", "Hayvanlar", 8,
                0xECEFF4, 0xD5DBE4, 0x9BA7B8, 0x7C8899, 0xC9D1DC, 0x2C333D),
            new AvatarSpec("aslan", "Aslan", "Hayvanlar", 9,
                0xFFF0CC, 0xFFDA8F, 0xF6C36B, 0xD98A28, 0xFFF0D2, 0x4A330F),
            new AvatarSpec("kaplan", "Kaplan", "Hayvanlar", 10,
                0xFFE9CE, 0xFFD19B, 0xF59B3C, 0xE07C2A, 0xFFF6E8, 0x3A2410),
            new AvatarSpec("ahtapot", "Ahtapot", "Hayvanlar", 11,
                0xFFE1EF, 0xFFC2DE, 0xF27BAE, 0xD95A8F, 0xFFD1E3, 0x4C1830),

            // ── Uzay ─────────────────────────────────────────────────────────
            new AvatarSpec("astronot", "Astronot", "Uzay", 12,
                0xE3E8FF, 0xBFCBFF, 0xFFFFFF, 0xC9D3EE, 0x6C7CFF, 0x1E2440),
            new AvatarSpec("robot", "Robot", "Uzay", 13,
                0xE2F1F8, 0xC3E0EE, 0xB9C9D6, 0x7F94A6, 0x4FE3C1, 0x1B2733),
            new AvatarSpec("uzayli", "Uzaylı", "Uzay", 14,
                0xE4FBE9, 0xBDF3C9, 0x7EDC8E, 0x54B968, 0xB6FFC4, 0x16271C),
            new AvatarSpec("gezegen", "Gezegen", "Uzay", 15,
                0xEFE2FF, 0xD2B8FF, 0x8A63E8, 0x6B47C4, 0xFFD37A, 0x2D1E52),
            new AvatarSpec("ay", "Ay", "Uzay", 16,
                0xEEF2FF, 0xCBD6F7, 0xF5E9C8, 0xDCC9A2, 0xFFF3CF, 0x4C4430),
            new AvatarSpec("yildiz", "Yıldız", "Uzay", 17,
                0xFFF4D6, 0xFFE3A3, 0xFFC93F, 0xE8A51E, 0xFFF0B8, 0x4E3410),
            new AvatarSpec("kuyruklu", "Kuyruklu", "Uzay", 18,
                0xE3ECFF, 0xBFD5FF, 0x5BC8FF, 0x2E9FE0, 0xB8ECFF, 0x102A44),
            new AvatarSpec("ufo", "UFO", "Uzay", 19,
                0xE6E9FF, 0xC6CCF7, 0xAEB9D6, 0x7C89AD, 0x7CE8FF, 0x232B45),

            // ── Doğa ─────────────────────────────────────────────────────────
            new AvatarSpec("gunes", "Güneş", "Doğa", 20,
                0xFFF2CE, 0xFFDF9A, 0xFFB534, 0xFFC95A, 0xFFE9B0, 0x513609),
            new AvatarSpec("bulut", "Bulut", "Doğa", 21,
                0xE4F2FF, 0xC4E2FB, 0xFFFFFF, 0xD7E7F5, 0x6FB8F2, 0x2E4256),
            new AvatarSpec("yaprak", "Yaprak", "Doğa", 22,
                0xE8F8E2, 0xC8ECC0, 0x74C05A, 0x4E9A3C, 0xA8DD8E, 0x23421C),
            new AvatarSpec("kaktus", "Kaktüs", "Doğa", 23,
                0xEAF6E4, 0xCDE9C4, 0x56B36A, 0x3F9653, 0xFF8FBF, 0x1E3A24),
            new AvatarSpec("mantar", "Mantar", "Doğa", 24,
                0xFFE9E5, 0xFFCFC7, 0xE85D5D, 0xC74545, 0xFFEFD9, 0x4A2220),
            new AvatarSpec("alev", "Alev", "Doğa", 25,
                0xFFE8D2, 0xFFC9A3, 0xFF7A3D, 0xF25E22, 0xFFC24D, 0x4C1F0A),
            new AvatarSpec("dalga", "Dalga", "Doğa", 26,
                0xDFF6FF, 0xB9E9FA, 0x43BEE8, 0x2A9BC7, 0xEAFBFF, 0x103B4C),
            new AvatarSpec("kristal", "Kristal", "Doğa", 27,
                0xEEE7FF, 0xD5C6FF, 0x8FE0FF, 0x6BC8F0, 0xB9F0FF, 0x263150),

            // ── Karakterler ──────────────────────────────────────────────────
            new AvatarSpec("hayalet", "Hayalet", "Karakterler", 28,
                0xEFE9FF, 0xD8CEFF, 0xFFFFFF, 0xE4DDFF, 0xFFC2D6, 0x3A3352),
            new AvatarSpec("ninja", "Ninja", "Karakterler", 29,
                0xE4E6EF, 0xC5C9DA, 0x3B4256, 0x2A3042, 0xFF5C6C, 0x1E2230),
            new AvatarSpec("buyucu", "Büyücü", "Karakterler", 30,
                0xF0E6FF, 0xD9C4FF, 0x6C4BC4, 0x55379F, 0xF5CBA8, 0x332456),
            new AvatarSpec("sovalye", "Şövalye", "Karakterler", 31,
                0xE8EEF5, 0xCBD8E6, 0xB8C7D9, 0x8FA2B8, 0x7FD4FF, 0x232C38),
        };
    }
}
