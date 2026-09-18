using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RsyncZilla.Services
{
    /// <summary>
    /// Helper to retrieve and cache native Windows Shell icons for files, folders, and drives.
    /// Uses SHGFI_USEFILEATTRIBUTES so remote SFTP files get their correct native Windows icons
    /// without requiring the file to exist on the local hard disk.
    /// </summary>
    public static class ShellIconHelper
    {
        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private static readonly ConcurrentDictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Retrieves the native Windows 16x16 icon for a file, folder, or drive.
        /// Returns null for the parent folder '..' navigation item so custom vector UI can be used.
        /// </summary>
        public static ImageSource? GetIcon(bool isDirectory, string name, bool isDrive = false, bool isParent = false)
        {
            if (isParent)
            {
                return null;
            }

            string cacheKey;
            if (isDrive)
            {
                cacheKey = "::drive::" + (string.IsNullOrWhiteSpace(name) ? "default" : name);
            }
            else if (isDirectory)
            {
                cacheKey = "::folder::";
            }
            else
            {
                var ext = Path.GetExtension(name);
                cacheKey = string.IsNullOrEmpty(ext) ? "::file_no_ext::" : ext.ToLowerInvariant();
            }

            return _iconCache.GetOrAdd(cacheKey, key => ExtractIcon(key, isDirectory, isDrive, name));
        }

        private static ImageSource? ExtractIcon(string cacheKey, bool isDirectory, bool isDrive, string originalName)
        {
            try
            {
                SHFILEINFO shinfo = new();
                uint flags = SHGFI_ICON | SHGFI_SMALLICON;
                uint attributes = FILE_ATTRIBUTE_NORMAL;
                string queryPath;

                if (isDrive)
                {
                    // Drive path e.g. "C:\" or root system drive
                    queryPath = string.IsNullOrWhiteSpace(originalName) ? (Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\") : originalName;
                    if (!queryPath.EndsWith(Path.DirectorySeparatorChar.ToString()) && !queryPath.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                    {
                        queryPath += Path.DirectorySeparatorChar;
                    }
                    flags &= ~SHGFI_USEFILEATTRIBUTES;
                }
                else if (isDirectory)
                {
                    flags |= SHGFI_USEFILEATTRIBUTES;
                    attributes = FILE_ATTRIBUTE_DIRECTORY;
                    queryPath = "dummy_folder";
                }
                else
                {
                    flags |= SHGFI_USEFILEATTRIBUTES;
                    attributes = FILE_ATTRIBUTE_NORMAL;
                    var ext = Path.GetExtension(originalName);
                    queryPath = string.IsNullOrEmpty(ext) ? "dummy_file" : "dummy_file" + ext.ToLowerInvariant();
                }

                IntPtr result = SHGetFileInfo(queryPath, attributes, out shinfo, (uint)Marshal.SizeOf(shinfo), flags);
                if (result != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                            shinfo.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());

                        // Freezing makes the BitmapSource cross-thread accessible and immutable
                        if (bitmapSource.CanFreeze)
                        {
                            bitmapSource.Freeze();
                        }
                        return bitmapSource;
                    }
                    finally
                    {
                        DestroyIcon(shinfo.hIcon);
                    }
                }
            }
            catch
            {
                // Gracefully fallback to null on systems or environments where SHGetFileInfo cannot complete
            }

            return null;
        }
    }
}
