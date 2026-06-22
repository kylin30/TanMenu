namespace TanMenu.Core.Services;

/// <summary>
/// Default <see cref="IIconProvider"/> — delegates to <see cref="Win32IconExtractor"/>
/// (SHGetFileInfo -> HICON -> PNG bytes). No caching, no ImageSource: the App layer
/// turns these raw bytes into a WinUI ImageSource and owns caching.
/// </summary>
public sealed class IconProvider : IIconProvider
{
    public byte[]? GetIconPngBytes(string path) => Win32IconExtractor.GetIconPngBytes(path);

    public byte[]? GetDefaultAppIconPngBytes() => Win32IconExtractor.GetStockAppIconPngBytes();
}
