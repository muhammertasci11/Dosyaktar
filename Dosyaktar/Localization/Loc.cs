using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace Dosyaktar.Localization
{
    /// <summary>
    /// Runtime localization. Source strings are Turkish; every other language is
    /// looked up by the Turkish source text, so a missing entry always falls back
    /// to readable Turkish instead of a key.
    /// </summary>
    public static class Loc
    {
        public static readonly (string Code, string Name)[] Languages =
        {
            ("tr", "Türkçe"),
            ("en", "English"),
            ("de", "Deutsch"),
            ("fr", "Français"),
            ("es", "Español"),
            ("ja", "日本語"),
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Map = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, LocalizedEntry> Entries = new(StringComparer.Ordinal);

        public static string Current { get; private set; } = "tr";

        public static event EventHandler? LanguageChanged;

        static Loc()
        {
            Load();
        }

        private static void Load()
        {
            try
            {
                var asm = typeof(Loc).Assembly;
                using var stream = asm.GetManifestResourceStream("Dosyaktar.Resources.strings.json");
                if (stream == null) return;

                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
                if (parsed == null) return;

                foreach (var pair in parsed)
                    Map[pair.Key] = pair.Value;
            }
            catch
            {
                // A broken pack must never take the app down; Turkish fallback remains.
            }
        }

        public static string S(string turkish)
        {
            if (string.IsNullOrEmpty(turkish) || Current == "tr") return turkish;
            return Map.TryGetValue(turkish, out var translations)
                && translations.TryGetValue(Current, out var value)
                && !string.IsNullOrWhiteSpace(value)
                    ? value
                    : turkish;
        }

        /// <summary>Translates a Turkish format string, then fills its arguments.</summary>
        public static string F(string turkishFormat, params object?[] args)
        {
            try { return string.Format(S(turkishFormat), args); }
            catch (FormatException) { return S(turkishFormat); }
        }

        public static LocalizedEntry Entry(string turkish)
        {
            lock (Entries)
            {
                if (!Entries.TryGetValue(turkish, out var entry))
                {
                    entry = new LocalizedEntry(turkish);
                    Entries[turkish] = entry;
                }
                return entry;
            }
        }

        public static void SetLanguage(string? code)
        {
            string next = Normalize(code);
            if (next == Current) return;

            Current = next;
            foreach (var entry in Entries.Values) entry.Refresh();
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        public static string Normalize(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "tr";
            string c = code.Trim().ToLowerInvariant();
            foreach (var (id, _) in Languages)
                if (id == c) return c;
            return "tr";
        }
    }

    /// <summary>One observable string that updates every place bound to it.</summary>
    public sealed class LocalizedEntry : INotifyPropertyChanged
    {
        public LocalizedEntry(string turkish) => Turkish = turkish;

        public string Turkish { get; }

        public string Value => Loc.S(Turkish);

        public event PropertyChangedEventHandler? PropertyChanged;

        internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }
}
