namespace TanMenu.Core.Infrastructure;

/// <summary>
/// The single source of truth for all writable data locations.
/// NEVER write next to the EXE (the MSIX install dir is read-only).
/// </summary>
public interface IAppDataPaths
{
    string LocalFolder { get; }
    string LocalCacheFolder { get; }
    string LogsFolder { get; }
    string ConfigFilePath { get; }
    string LinkCacheFilePath { get; }
    string IconCacheFilePath { get; }
    void EnsureCreated();
}
