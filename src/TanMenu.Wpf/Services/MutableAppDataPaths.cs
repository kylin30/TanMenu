using System.IO;
using TanMenu.Core.Infrastructure;

namespace TanMenu.Wpf.Services;

/// <summary>
/// An <see cref="IAppDataPaths"/> whose root can be re-pointed at runtime via <see cref="SetRoot"/>,
/// so a data-folder change in settings takes effect immediately (no restart). All paths are computed
/// from the current root on each access — consumers that read the path per call (e.g. ConfigService)
/// see the new location at once.
/// </summary>
public sealed class MutableAppDataPaths : IAppDataPaths
{
    private string _root;

    public MutableAppDataPaths(string root)
    {
        _root = root;
        EnsureCreated();
    }

    public string LocalFolder => _root;
    public string LocalCacheFolder => Path.Combine(_root, "cache");
    public string LogsFolder => Path.Combine(LocalCacheFolder, "logs");
    public string ConfigFilePath => Path.Combine(LocalFolder, "config.json");
    public string LinkCacheFilePath => Path.Combine(LocalCacheFolder, "linkCache.json");
    public string IconCacheFilePath => Path.Combine(LocalCacheFolder, "iconCache.json");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(LocalFolder);
        Directory.CreateDirectory(LocalCacheFolder);
        Directory.CreateDirectory(LogsFolder);
    }

    /// <summary>Re-point the data root and ensure the new folders exist.</summary>
    public void SetRoot(string root)
    {
        _root = root;
        EnsureCreated();
    }
}
