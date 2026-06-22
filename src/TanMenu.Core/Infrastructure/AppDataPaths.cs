using System.IO;

namespace TanMenu.Core.Infrastructure;

public sealed class AppDataPaths : IAppDataPaths
{
    public string LocalFolder { get; }
    public string LocalCacheFolder { get; }

    public string LogsFolder => Path.Combine(LocalCacheFolder, "logs");
    public string ConfigFilePath => Path.Combine(LocalFolder, "config.json");
    public string LinkCacheFilePath => Path.Combine(LocalCacheFolder, "linkCache.json");
    public string IconCacheFilePath => Path.Combine(LocalCacheFolder, "iconCache.json");

    /// <summary>Production ctor: packaged -> ApplicationData; unpackaged -> %LOCALAPPDATA%\TanMenu.</summary>
    public AppDataPaths()
    {
        if (PackageRuntime.HasPackageIdentity)
        {
            var (local, cache) = GetPackagedPaths();
            LocalFolder = local;
            LocalCacheFolder = cache;
        }
        else
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TanMenu");
            LocalFolder = baseDir;
            LocalCacheFolder = Path.Combine(baseDir, "Cache");
        }
    }

    /// <summary>Test/override ctor: explicit roots, no WinRT.</summary>
    public AppDataPaths(string localFolder, string localCacheFolder)
    {
        LocalFolder = localFolder;
        LocalCacheFolder = localCacheFolder;
    }

    /// <summary>
    /// Isolated method so the WinRT ApplicationData type is only touched on the packaged path.
    /// Calling this when unpackaged would throw InvalidOperationException.
    /// </summary>
    private static (string local, string cache) GetPackagedPaths()
    {
        var local = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        var cache = Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path;
        return (local, cache);
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(LocalFolder);
        Directory.CreateDirectory(LocalCacheFolder);
        Directory.CreateDirectory(LogsFolder);
    }
}
