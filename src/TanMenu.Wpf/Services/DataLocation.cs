using System.IO;
using Microsoft.Win32;

namespace TanMenu.Wpf.Services;

/// <summary>
/// Resolves and relocates the writable data root (config + caches + logs). Default is
/// <c>%USERPROFILE%\Documents\TanMenu</c>; a user-chosen location is remembered in
/// <c>HKCU\Software\TanMenuWpf\DataFolder</c> (a tiny pointer that survives the data move).
/// </summary>
public static class DataLocation
{
    private const string RegKey = @"Software\TanMenuWpf";
    private const string RegValue = "DataFolder";

    /// <summary>Default data root: <c>Documents\TanMenu</c>.</summary>
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TanMenu");

    /// <summary>The old default (<c>%LOCALAPPDATA%\TanMenu</c>) used before the move to Documents.</summary>
    private static string LegacyRoot =>
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
    /// One-time migration for installs that predate the Documents default: if the user has NOT
    /// chosen a custom data folder (no registry pointer) and the new default (Documents\TanMenu)
    /// has no config yet, but the old %LOCALAPPDATA%\TanMenu location does, move that data over so
    /// the user keeps their config and caches. No registry pointer is written — Documents stays the
    /// (implicit) default. Best-effort: any failure leaves the app on the default with fresh data.
    /// </summary>
    public static void MigrateLegacyIfNeeded()
    {
        try
        {
            // Skip if the user has explicitly pinned a data folder.
            using (var k = Registry.CurrentUser.OpenSubKey(RegKey))
            {
                if (k?.GetValue(RegValue) is string s && !string.IsNullOrWhiteSpace(s) && Directory.Exists(s))
                    return;
            }

            var def = DefaultRoot;
            var legacy = LegacyRoot;

            // New default already has data, or there's nothing to migrate — nothing to do.
            if (File.Exists(Path.Combine(def, "config.json")))
                return;
            if (!File.Exists(Path.Combine(legacy, "config.json")))
                return;

            Directory.CreateDirectory(def);
            MoveData(legacy, def);
        }
        catch { /* best effort; leave the app on the fresh default */ }
    }

    /// <summary>
    /// Point the app at <paramref name="newFolder"/>. If it already holds a config.json, use that
    /// data as-is; otherwise move the current config + caches into it. Returns false (no change)
    /// if newFolder is the current root.
    /// </summary>
    public static bool Relocate(string newFolder, out bool usedExisting)
    {
        usedExisting = false;
        var current = GetDataRoot();

        if (string.Equals(Path.GetFullPath(newFolder).TrimEnd('\\'),
                          Path.GetFullPath(current).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return false;

        Directory.CreateDirectory(newFolder);

        if (File.Exists(Path.Combine(newFolder, "config.json")))
        {
            // Target already has data — switch to using it.
            usedExisting = true;
        }
        else
        {
            // Move config.json + cache files from the current root into the new one.
            MoveData(current, newFolder);
        }

        SetDataRoot(newFolder);
        return true;
    }

    /// <summary>
    /// Move config.json (copy+delete is cross-volume safe) and the cache folder from
    /// <paramref name="from"/> to <paramref name="to"/>. Logs are transient and may be locked, so
    /// they're left to regenerate at the new location. Best-effort per file: caches regenerate.
    /// </summary>
    private static void MoveData(string from, string to)
    {
        var fromConfig = Path.Combine(from, "config.json");
        if (File.Exists(fromConfig))
        {
            File.Copy(fromConfig, Path.Combine(to, "config.json"), overwrite: true);
            try { File.Delete(fromConfig); } catch { /* ignore */ }
        }

        var fromCache = Path.Combine(from, "cache");
        if (Directory.Exists(fromCache))
        {
            var toCache = Path.Combine(to, "cache");
            Directory.CreateDirectory(toCache);
            foreach (var f in Directory.GetFiles(fromCache))
            {
                try
                {
                    File.Copy(f, Path.Combine(toCache, Path.GetFileName(f)), overwrite: true);
                    File.Delete(f);
                }
                catch { /* best effort; caches regenerate */ }
            }
        }
    }
}
