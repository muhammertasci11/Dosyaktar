using System;
using System.Windows;

namespace Dosyaktar
{
    /// <summary>
    /// Tema yöneticisi — LightTheme.xaml / DarkTheme.xaml arasında geçiş yapar.
    /// Uygulama kaynaklarındaki birleştirilmiş sözlüğün ilk elemanını değiştirir.
    /// </summary>
    public static class ThemeManager
    {
        private const string LightUri = "Themes/LightTheme.xaml";
        private const string DarkUri  = "Themes/DarkTheme.xaml";

        public static bool IsDark { get; private set; }

        public static void Apply(bool dark)
        {
            IsDark = dark;
            string uri = dark ? DarkUri : LightUri;

            var dict = new ResourceDictionary
            {
                Source = new Uri(uri, UriKind.Relative)
            };

            // Merged dictionary'nin ilk elemanı tema sözlüğüdür → değiştir
            var merged = Application.Current.Resources.MergedDictionaries;
            if (merged.Count > 0)
                merged[0] = dict;
            else
                merged.Insert(0, dict);
        }

        public static void Toggle() => Apply(!IsDark);
    }
}
