using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TanMenu.Core.Infrastructure;

namespace TanMenu.Core.Services;

public sealed class ShortcutResolver : IShortcutResolver, IDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly string _cacheFile;
    private readonly object _fileLock = new();
    private readonly System.Threading.Timer _saveTimer;

    private long _totalRequests;
    private long _cacheHits;
    private readonly object _statsLock = new();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetLongPathName(string lpszShortPath, StringBuilder lpszLongPath, uint cchBuffer);

    private sealed class CacheEntry
    {
        public string? TargetPath { get; set; }
        public DateTime LastModified { get; set; }
        public long FileSize { get; set; }
        public string FileHash { get; set; } = string.Empty;
    }

    public ShortcutResolver(IAppDataPaths paths)
    {
        _cacheFile = paths.LinkCacheFilePath;
        _saveTimer = new System.Threading.Timer(_ => SaveToDisk());
        LoadCache();
    }

    public string? ResolveShortcut(string shortcutPath)
    {
        lock (_statsLock) { _totalRequests++; }

        if (string.IsNullOrEmpty(shortcutPath) || !File.Exists(shortcutPath))
            return null;

        var key = shortcutPath.ToLowerInvariant();

        if (IsCacheValid(shortcutPath, key))
        {
            lock (_statsLock) { _cacheHits++; }
            return _cache[key].TargetPath;
        }

        var targetPath = ResolveShortcutActual(shortcutPath);
        UpdateCache(shortcutPath, key, targetPath);
        return targetPath;
    }

    private bool IsCacheValid(string shortcutPath, string key)
    {
        if (!_cache.TryGetValue(key, out var cached))
            return false;

        try
        {
            var fileInfo = new FileInfo(shortcutPath);
            if (cached.LastModified != fileInfo.LastWriteTime || cached.FileSize != fileInfo.Length)
                return false;

            if (fileInfo.Length < 102400 && !string.IsNullOrEmpty(cached.FileHash))
            {
                var currentHash = CalculateFileHash(shortcutPath);
                if (currentHash != cached.FileHash)
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateCache(string shortcutPath, string key, string? targetPath)
    {
        try
        {
            var fileInfo = new FileInfo(shortcutPath);
            var fileHash = fileInfo.Length < 102400 ? CalculateFileHash(shortcutPath) : string.Empty;

            _cache[key] = new CacheEntry
            {
                TargetPath = targetPath,
                LastModified = fileInfo.LastWriteTime,
                FileSize = fileInfo.Length,
                FileHash = fileHash
            };

            ScheduleSave();
        }
        catch
        {
            // ignore cache update errors
        }
    }

    private static string CalculateFileHash(string filePath)
    {
        try
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            return Convert.ToHexString(sha.ComputeHash(stream));
        }
        catch
        {
            return string.Empty;
        }
    }

    // Resolve via IShellLink/IPersistFile rather than the WScript.Shell ProgID — WSH is blocked by
    // policy in many hardened/managed environments and isn't available in an MSIX sandbox, where the
    // old path silently returned null for EVERY .lnk. IShellLink works in those environments.
    private static string? ResolveShortcutActual(string shortcutPath)
    {
        IShellLinkW? link = null;
        try
        {
            link = (IShellLinkW)new ShellLink();
            ((IPersistFile)link).Load(shortcutPath, 0);
            // Don't call Resolve() — it can pop UI / hit the network for dead links; read the stored path.
            var sb = new StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            var target = sb.ToString();
            return string.IsNullOrEmpty(target) ? null : ConvertToLongPath(target);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (link != null)
                Marshal.ReleaseComObject(link);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    private static string ConvertToLongPath(string shortPath)
    {
        try
        {
            var buffer = new StringBuilder(260);
            var result = GetLongPathName(shortPath, buffer, (uint)buffer.Capacity);
            if (result == 0)
                return shortPath; // API failed (e.g. path doesn't exist) — keep the original

            // When the long path needs more room than the buffer, GetLongPathName returns the
            // REQUIRED size (incl. null) WITHOUT writing the buffer. Re-allocate and retry, else
            // we'd cache an empty/garbage string and permanently disable the item.
            if (result > buffer.Capacity)
            {
                buffer = new StringBuilder((int)result);
                result = GetLongPathName(shortPath, buffer, (uint)buffer.Capacity);
                if (result == 0 || result > buffer.Capacity)
                    return shortPath;
            }
            return buffer.ToString();
        }
        catch
        {
            return shortPath;
        }
    }

    private void LoadCache()
    {
        lock (_fileLock)
        {
            try
            {
                if (!File.Exists(_cacheFile)) return;
                var json = File.ReadAllText(_cacheFile, Encoding.UTF8);
                var data = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(json);
                if (data != null)
                    foreach (var kvp in data) _cache[kvp.Key] = kvp.Value;
            }
            catch
            {
                // ignore cache load errors
            }
        }
    }

    // Coalesce cache writes: a burst of cache misses (a cold scan of N shortcuts) schedules a single
    // trailing write instead of N full-file serializations of a growing dictionary.
    private void ScheduleSave() => _saveTimer.Change(800, System.Threading.Timeout.Infinite);

    private void SaveToDisk()
    {
        lock (_fileLock)
        {
            try
            {
                var snapshot = _cache.ToDictionary(x => x.Key, x => x.Value);
                Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = false });
                File.WriteAllText(_cacheFile, json, Encoding.UTF8);
            }
            catch
            {
                // ignore cache save errors
            }
        }
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        SaveToDisk(); // flush the final cache state synchronously on shutdown
    }

    public void ClearCache()
    {
        _cache.Clear();
        lock (_fileLock)
        {
            try
            {
                if (File.Exists(_cacheFile)) File.Delete(_cacheFile);
            }
            catch
            {
                // ignore clear errors
            }
        }

        lock (_statsLock)
        {
            _totalRequests = 0;
            _cacheHits = 0;
        }
    }

    public int CacheCount => _cache.Count;

    public double HitRate
    {
        get
        {
            lock (_statsLock)
            {
                return _totalRequests > 0 ? (double)_cacheHits / _totalRequests : 0;
            }
        }
    }
}
