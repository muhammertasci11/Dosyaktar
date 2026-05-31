using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Dosyaktar.Helpers
{
    /// <summary>
    /// Windows Shell IFileOpenDialog üzerinden WPF'de klasör seçimi.
    /// System.Windows.Forms bağımlılığı gerektirmez.
    /// </summary>
    public static class FolderPicker
    {
        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(nint parent);
            void SetFileTypes(uint cFileTypes, nint rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(nint pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(FOS fos);
            void GetOptions(out FOS pfos);
            void SetDefaultFolder(nint psi);
            void SetFolder(nint psi);
            void GetFolder(out nint ppsi);
            void GetCurrentSelection(out nint ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out nint ppsi);
            void AddPlace(nint psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(nint pFilter);
            void GetResults(out nint ppenum);
            void GetSelectedItems(out nint ppsai);
        }

        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
        [ClassInterface(ClassInterfaceType.None)]
        private class FileOpenDialogImpl { }

        [Flags]
        private enum FOS : uint
        {
            FOS_PICKFOLDERS   = 0x00000020,
            FOS_FORCEFILESYSTEM = 0x00000040,
            FOS_NOCHANGEDIR    = 0x00000008,
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetNameFromIDList(nint pidl, int sigdnName,
                                                      [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(nint pbc, ref Guid bhid, ref Guid riid, out nint ppv);
            void GetParent(out nint ppsi);
            void GetDisplayName(Sigdn sigdnName,
                                [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(nint psi, uint hint, out int piOrder);
        }

        private enum Sigdn : uint { NormalDisplay = 0, FileSysPath = 0x80058000 }

        /// <summary>
        /// Klasör seçim diyalogunu gösterir. Seçilen yolu döndürür, iptal edilirse null.
        /// </summary>
        public static string? ShowDialog(Window owner, string title = "Klasör Seç")
        {
            try
            {
                var dialog = (IFileOpenDialog)new FileOpenDialogImpl();
                dialog.SetOptions(FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM | FOS.FOS_NOCHANGEDIR);
                dialog.SetTitle(title);

                nint hwnd = owner != null
                    ? new WindowInteropHelper(owner).Handle
                    : nint.Zero;

                int hr = dialog.Show(hwnd);
                if (hr != 0) return null; // İptal

                dialog.GetResult(out nint ppsi);
                var item = (IShellItem)Marshal.GetObjectForIUnknown(ppsi);
                item.GetDisplayName(Sigdn.FileSysPath, out string path);
                Marshal.Release(ppsi);
                return path;
            }
            catch
            {
                return null;
            }
        }
    }
}
