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

    private const int SIID_APPLICATION = 2;     // generic Windows application icon
    private const uint SHGSI_ICON = 0x100;
    private const uint SHGSI_LARGEICON = 0x0;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetStockIconInfo(int siid, uint uFlags, ref SHSTOCKICONINFO psii);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szPath;
    }

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

    // SHGetFileInfo and the GDI HICON→Bitmap step (Icon.FromHandle/ToBitmap) touch shared shell/GDI
    // handle state and are not safe to run concurrently, so they stay under this one process-wide lock.
    // The managed Bitmap→PNG encode does NOT race that state — it works on an already-detached Bitmap —
    // so it is deliberately done OUTSIDE the lock (see EncodePng), shrinking the critical section so a
    // caller never blocks others through the encode. (The xUnit suite disables parallelization too.)
    private static readonly object _extractLock = new();

    public static byte[]? GetIconPngBytes(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var isDirectory = Directory.Exists(path);
        var isFile = File.Exists(path);
        if (!isDirectory && !isFile) return null;

        var bmp = ExtractBitmap(() =>
        {
            var shfi = new SHFILEINFO();
            var result = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref shfi,
                (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_SMALLICON);
            return result == IntPtr.Zero ? IntPtr.Zero : shfi.hIcon;
        });
        return EncodePng(bmp);
    }

    /// <summary>The Windows stock "application" icon (the generic icon shown for executables that
    /// have none of their own) as PNG bytes — used as the standard fallback for items whose icon
    /// can't be extracted (broken shortcuts, alias-stub exes like the Store mspaint).</summary>
    public static byte[]? GetStockAppIconPngBytes()
    {
        var bmp = ExtractBitmap(() =>
        {
            var sii = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
            var hr = SHGetStockIconInfo(SIID_APPLICATION, SHGSI_ICON | SHGSI_LARGEICON, ref sii);
            return hr != 0 ? IntPtr.Zero : sii.hIcon;
        });
        return EncodePng(bmp);
    }

    /// <summary>Run the HICON-producing shell call and the GDI HICON→Bitmap conversion under the
    /// extract lock (both touch shared shell/GDI state), always DestroyIcon, and return a detached
    /// managed <see cref="Bitmap"/> that the caller can encode without the lock. Null on any failure.</summary>
    private static Bitmap? ExtractBitmap(Func<IntPtr> getHicon)
    {
        lock (_extractLock)
        {
            IntPtr hIcon = IntPtr.Zero;
            try
            {
                hIcon = getHicon();
                if (hIcon == IntPtr.Zero) return null;
                using var icon = Icon.FromHandle(hIcon); // does not take ownership of hIcon
                return icon.ToBitmap();                  // detached managed copy; safe to use after DestroyIcon
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

    /// <summary>Encode a detached Bitmap to PNG bytes. Runs OUTSIDE <see cref="_extractLock"/>: it
    /// touches no shared shell/HICON state, so different icons' encodes need not serialize.</summary>
    private static byte[]? EncodePng(Bitmap? bmp)
    {
        if (bmp is null) return null;
        try
        {
            using (bmp)
            using (var stream = new MemoryStream())
            {
                bmp.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }
        catch
        {
            return null;
        }
    }
}
