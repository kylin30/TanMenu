using System.IO;
using Microsoft.Win32;

namespace TanMenu.Wpf.Services;

/// <summary>
/// Resolves and relocates the writable data root (config + caches + logs). Default is
/// <c>%LOCALAPPDATA%\TanMenu</c>; a user-chosen location is remembered in
/// <c>HKCU\Software\TanMenuWpf\DataFolder</c> (a tiny pointer that survives the data move).
/// </summary>
public static class DataLocation
{
    private const string RegKey = @"Software\TanMenuWpf";
    private const string RegValue = "DataFolder";

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TanMenu");

    public static string GetDataRoot()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RegKey);
            if (k?.GetValue(RegValue) is string s && !string.IsNullOrWhiteSpace(s) && Directory.Exists(s))
                return s;
        }
        catch { /* fall back to default */ }
        return DefaultRoot;
    }

    public static void SetDataRoot(string path)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RegKey);
        k.SetValue(RegValue, path);
    }

    /// <summary>
    /// Point the app at <paramref name="newFolder"/>. If it already holds a config.json, use that
    /// data as-is; otherwise move the current config + caches into it. Caller should restart so the
    /// new location is loaded. Returns false (no change) if newFolder is the current root.
    /// </summary>
    public static bool Relocate(string newFolder, out bool usedExisting)
    {
        usedExisting = false;
        var current = GetDataRoot();

        if (string.Equals(Path.GetFullPath(newFolder).TrimEnd('\\'),
                          Path.GetFullPath(current).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return false;

        Directory.CreateDirectory(newFolder);
        var newConfig = Path.Combine(newFolder, "config.json");

        if (File.Exists(newConfig))
        {
            // Target already has data — switch to using it.
            usedExisting = true;
        }
        else
        {
            // Move config.json (copy+delete is cross-volume safe) + cache files. Logs are
            // transient and may be locked, so they're left to regenerate at the new location.
            var curConfig = Path.Combine(current, "config.json");
            if (File.Exists(curConfig))
            {
                File.Copy(curConfig, newConfig, overwrite: true);
                try { File.Delete(curConfig); } catch { /* ignore */ }
            }

            var curCache = Path.Combine(current, "cache");
            if (Directory.Exists(curCache))
            {
                var newCache = Path.Combine(newFolder, "cache");
                Directory.CreateDirectory(newCache);
                foreach (var f in Directory.GetFiles(curCache))
                {
                    try
                    {
                        File.Copy(f, Path.Combine(newCache, Path.GetFileName(f)), overwrite: true);
                        File.Delete(f);
                    }
                    catch { /* best effort; caches regenerate */ }
                }
            }
        }

        SetDataRoot(newFolder);
        return true;
    }
}
