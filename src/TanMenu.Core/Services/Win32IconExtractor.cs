using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace TanMenu.Core.Services;

/// <summary>
/// Raw HICON -> PNG bytes extraction via SHGetFileInfo. No caching, no ImageSource.
/// </summary>
public static class Win32IconExtractor
{
    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
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

    public static byte[]? GetIconPngBytes(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var isDirectory = Directory.Exists(path);
        var isFile = File.Exists(path);
        if (!isDirectory && !isFile) return null;

        IntPtr hIcon = IntPtr.Zero;
        try
        {
            var shfi = new SHFILEINFO();
            var result = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref shfi,
                (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_SMALLICON);

            if (result == IntPtr.Zero) return null;

            hIcon = shfi.hIcon;
            if (hIcon == IntPtr.Zero) return null;

            using var icon = Icon.FromHandle(hIcon);
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hIcon != IntPtr.Zero) DestroyIcon(hIcon);
        }
    }
}
