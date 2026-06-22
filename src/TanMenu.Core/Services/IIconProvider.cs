namespace TanMenu.Core.Services;

/// <summary>
/// Core seam for icon extraction. Returns raw PNG bytes for a filesystem path.
/// The App layer wraps this to produce a WinUI ImageSource (M3).
/// </summary>
public interface IIconProvider
{
    byte[]? GetIconPngBytes(string path);
}
