using System.IO;
using Microsoft.Win32;
using TanMenu.Core.Services;

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

    /// <summary>The <c>%LOCALAPPDATA%\TanMenu</c> root — the pre-Documents legacy default, and the
    /// always-writable fallback used at startup when the chosen data root can't be created.</summary>
    public static string LocalAppDataRoot =>
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

    /// <summary>True when two paths refer to the same folder (full-path, trailing-slash- and case-insensitive).</summary>
    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="child"/> is strictly inside <paramref name="ancestor"/>
    /// (a proper descendant). Used to reject relocating the data folder into its own subtree, where the
    /// copy-then-delete would delete the freshly-copied data.</summary>
    private static bool IsInside(string child, string ancestor)
    {
        var c = Path.GetFullPath(child).TrimEnd('\\');
        var a = Path.GetFullPath(ancestor).TrimEnd('\\');
        return c.Length > a.Length
            && c.StartsWith(a, StringComparison.OrdinalIgnoreCase)
            && c[a.Length] == '\\';
    }

    /// <summary>True when <paramref name="folder"/> is the implicit default (Documents\TanMenu).</summary>
    public static bool IsDefaultLocation(string folder) => SamePath(folder, DefaultRoot);

    /// <summary>True when the registry data root is the implicit default (Documents\TanMenu) — i.e. no
    /// custom HKCU pointer is currently in effect.</summary>
    public static bool IsUsingDefaultLocation() => IsDefaultLocation(GetDataRoot());

    /// <summary>Total size in bytes of files under <paramref name="folder"/>. Recurses manually so one
    /// unreadable subfolder doesn't abort the whole walk, and skips reparse points (junctions/symlinks)
    /// so a directory cycle can't loop forever or double-count. Returns whatever it could sum.</summary>
    public static long GetFolderSize(string folder)
    {
        long total = 0;
        var stack = new Stack<string>();
        stack.Push(folder);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    try { total += new FileInfo(f).Length; } catch { /* skip a file removed/locked mid-scan */ }
                }
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    try
                    {
                        if ((new DirectoryInfo(sub).Attributes & FileAttributes.ReparsePoint) == 0)
                            stack.Push(sub); // don't follow junctions/symlinks
                    }
                    catch { /* skip an unreadable subdirectory */ }
                }
            }
            catch { /* skip a directory we can't enumerate (ACL) — keep summing the rest */ }
        }
        return total;
    }

    private static readonly object _sizeCacheLock = new();
    private static string? _sizeCacheRoot;
    private static long _sizeCacheBytes;

    /// <summary>Folder size for the data-location label, cached per root so repeatedly opening the
    /// settings window doesn't re-walk the whole data tree each time. Invalidated by
    /// <see cref="InvalidateFolderSizeCache"/> after operations that change the on-disk size
    /// (clear cache / relocate). The walk itself runs outside the lock — at worst two concurrent
    /// first-time callers each walk once.</summary>
    public static long GetFolderSizeCached(string folder)
    {
        lock (_sizeCacheLock)
        {
            if (_sizeCacheRoot != null && SamePath(_sizeCacheRoot, folder))
                return _sizeCacheBytes;
        }
        var size = GetFolderSize(folder);
        lock (_sizeCacheLock)
        {
            _sizeCacheRoot = folder;
            _sizeCacheBytes = size;
        }
        return size;
    }

    /// <summary>Drop the cached folder size so the next label refresh recomputes it. Call after
    /// clearing the cache or relocating the data root (both change the on-disk size).</summary>
    public static void InvalidateFolderSizeCache()
    {
        lock (_sizeCacheLock) { _sizeCacheRoot = null; }
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
            var legacy = LocalAppDataRoot;

            // New default already has data, or there's nothing to migrate — nothing to do.
            if (File.Exists(Path.Combine(def, "config.json")))
                return;
            if (!File.Exists(Path.Combine(legacy, "config.json")))
                return;

            Directory.CreateDirectory(def);
            // A true move (copy then delete). This runs at startup against the DEFAULT location, so no
            // registry pointer is involved — the copy/delete ordering that matters in Relocate is moot here.
            CopyData(legacy, def);
            DeleteData(legacy);
        }
        catch { /* best effort; leave the app on the fresh default */ }
    }

    /// <summary>
    /// Point the app at <paramref name="newFolder"/>. If it already holds a config.json, use that
    /// data as-is; otherwise move the current config + caches into it. Returns false (no change)
    /// if newFolder is the current root.
    /// </summary>
    public static bool Relocate(string newFolder, out bool usedExisting)
        => Relocate(newFolder, GetDataRoot(), out usedExisting);

    /// <summary>
    /// Point the app at <paramref name="newFolder"/>, treating <paramref name="currentRoot"/> as where
    /// the data lives now (the LIVE root, which can differ from the registry pointer after a startup
    /// fallback). If newFolder already holds a config.json, adopt it; otherwise move the current data in.
    /// </summary>
    public static bool Relocate(string newFolder, string currentRoot, out bool usedExisting)
    {
        usedExisting = false;

        if (SamePath(newFolder, currentRoot))
            return false;

        // Reject a target INSIDE the current root (e.g. picking <root>\cache): CopyData would write the
        // config into that subfolder and the deferred DeleteData(currentRoot) would then delete it again,
        // losing the data. A data folder can't live inside itself, so this is always a user mistake.
        if (IsInside(newFolder, currentRoot))
            throw new InvalidOperationException(AppLanguage.Text("DataFolderInsideItself", null));

        Directory.CreateDirectory(newFolder);

        var moving = !File.Exists(Path.Combine(newFolder, "config.json"));
        if (moving)
            // COPY only (don't delete the source yet): the destructive delete is deferred until AFTER
            // the registry pointer is committed, so a failure mid-relocate can never leave the data's
            // only copy stranded under a folder that nothing points at.
            CopyData(currentRoot, newFolder);
        else
            usedExisting = true; // target already has data — switch to using it

        // Commit the HKCU pointer BEFORE removing the source. If SetDataRoot throws (registry locked /
        // group-policy restricted), the source data is still intact and the pointer still references it,
        // so the caller's error path leaves a consistent state — no silent data loss. A leftover copy in
        // newFolder is harmless (a future relocate there just adopts it).
        SetDataRoot(newFolder);

        // Pointer committed — now it's safe to delete the source copy we relocated.
        if (moving)
            DeleteData(currentRoot);

        return true;
    }

    /// <summary>Copy config.json + the top-level cache files from <paramref name="from"/> to
    /// <paramref name="to"/> WITHOUT touching the source (cross-volume safe). Best-effort per cache file:
    /// caches regenerate. NOTE: the cache/logs subfolder is NOT copied (GetFiles isn't recursive), and the
    /// live Serilog file sink — its path is fixed at startup — keeps writing to the ORIGINAL location for
    /// the rest of the session; logs only land in the new folder after the next restart re-resolves the
    /// root.</summary>
    private static void CopyData(string from, string to)
    {
        var fromConfig = Path.Combine(from, "config.json");
        if (File.Exists(fromConfig))
            File.Copy(fromConfig, Path.Combine(to, "config.json"), overwrite: true);

        var fromCache = Path.Combine(from, "cache");
        if (Directory.Exists(fromCache))
        {
            var toCache = Path.Combine(to, "cache");
            Directory.CreateDirectory(toCache);
            foreach (var f in Directory.GetFiles(fromCache))
            {
                try { File.Copy(f, Path.Combine(toCache, Path.GetFileName(f)), overwrite: true); }
                catch { /* best effort; caches regenerate */ }
            }
        }
    }

    /// <summary>Delete config.json + cache files at <paramref name="from"/> after the data has been
    /// copied elsewhere and the pointer re-committed. Best-effort: a locked file just lingers harmlessly
    /// (it's no longer pointed at), never blocking the relocation.</summary>
    private static void DeleteData(string from)
    {
        try { File.Delete(Path.Combine(from, "config.json")); } catch { /* ignore */ }

        var fromCache = Path.Combine(from, "cache");
        if (Directory.Exists(fromCache))
            foreach (var f in Directory.GetFiles(fromCache))
            {
                try { File.Delete(f); } catch { /* best effort; caches regenerate */ }
            }
    }
}
