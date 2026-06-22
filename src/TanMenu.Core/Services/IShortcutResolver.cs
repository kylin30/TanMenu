namespace TanMenu.Core.Services;

public interface IShortcutResolver
{
    /// <summary>Resolves a .lnk to its absolute target path, or null if unresolved.</summary>
    string? ResolveShortcut(string shortcutPath);
    void ClearCache();
    int CacheCount { get; }
    double HitRate { get; }
}
