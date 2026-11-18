using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Dosyaktar.Fx
{
    /// <summary>
    /// Rounds the outer window frame on Windows 11. WPF builds a rectangular
    /// frame for a borderless window, so without this the atmosphere layer
    /// meets the desktop in four hard corners.
    /// </summary>
    public static class WindowCorners
    {
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwcpRound = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void Apply(Window window)
        {
            if (window == null) return;

            if (PresentationSource.FromVisual(window) is HwndSource)
                SetPreference(window);
            else
                window.SourceInitialized += (_, _) => SetPreference(window);
        }

        private static void SetPreference(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            int preference = DwmwcpRound;
            try { DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref preference, sizeof(int)); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}
