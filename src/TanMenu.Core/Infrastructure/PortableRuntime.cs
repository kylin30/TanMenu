using System.IO;

namespace TanMenu.Core.Infrastructure;

/// <summary>
/// Detects the marker used by the no-install ZIP distribution and defines every portable-only
/// writable location. Keeping this policy in one place prevents a portable build from silently
/// falling back to Documents, LocalAppData, or the registry.
/// </summary>
public static class PortableRuntime
{
    public const string MarkerFileName = "portable.flag";
    public const string DataFolderName = "Data";
    public const string WebView2FolderName = "WebView2";

    public static bool IsEnabled() => IsEnabledAt(AppContext.BaseDirectory);

    public static bool IsEnabledAt(string appBaseDirectory) =>
        File.Exists(Path.Combine(ValidateBaseDirectory(appBaseDirectory), MarkerFileName));

    public static string GetDataRoot() => GetDataRoot(AppContext.BaseDirectory);

    public static string GetDataRoot(string appBaseDirectory) =>
        Path.Combine(ValidateBaseDirectory(appBaseDirectory), DataFolderName);

    public static string GetWebView2DataFolder() => GetWebView2DataFolder(AppContext.BaseDirectory);

    public static string GetWebView2DataFolder(string appBaseDirectory) =>
        Path.Combine(GetDataRoot(appBaseDirectory), WebView2FolderName);

    /// <summary>
    /// Moves data created beside an older portable executable to the stable Velopack application
    /// root. Velopack replaces its <c>current</c> content directory during updates, while the root
    /// directory survives. Existing destination data always wins so a stale copy is never merged
    /// over a newer profile.
    /// </summary>
    public static bool MigrateLegacyDataIfNeeded(string legacyAppBaseDirectory, string stableAppRoot)
    {
        var source = GetDataRoot(legacyAppBaseDirectory);
        var destination = GetDataRoot(stableAppRoot);

        if (string.Equals(source.TrimEnd(Path.DirectorySeparatorChar),
                destination.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(source) ||
            Directory.Exists(destination))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.Move(source, destination);
        return true;
    }

    private static string ValidateBaseDirectory(string appBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appBaseDirectory);
        return Path.GetFullPath(appBaseDirectory);
    }
}
