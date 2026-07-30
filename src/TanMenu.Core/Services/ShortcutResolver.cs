using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Interop;
using TanMenu.Core.Serialization;

namespace TanMenu.Core.Services;

public sealed class ShortcutResolver : IShortcutResolver, IDisposable
{
    private readonly ConcurrentDictionary<string, ShortcutCacheEntry> _cache = new();
    private readonly IAppDataPaths _paths;
    // Resolved fresh each access (a computed property, like ConfigService._configPath) so a runtime
    // data-folder relocation (MutableAppDataPaths.SetRoot) is picked up immediately — otherwise the
    // cache would keep reading/writing/deleting linkCache.json at the OLD root after a move.
    private string _cacheFile => _paths.LinkCacheFilePath;
    private readonly object _fileLock = new();
    private readonly System.Threading.Timer _saveTimer;

    private long _totalRequests;
    private long _cacheHits;
    private readonly object _statsLock = new();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetLongPathName(string lpszShortPath, StringBuilder lpszLongPath, uint cchBuffer);

    public ShortcutResolver(IAppDataPaths paths)
    {
        _paths = paths;
        _saveTimer = new System.Threading.Timer(_ => SaveToDisk());
        LoadCache();
    }

    public string? ResolveShortcut(string shortcutPath)
    {
        lock (_statsLock) { _totalRequests++; }

        if (string.IsNullOrEmpty(shortcutPath) || !File.Exists(shortcutPath))
            return null;

        var key = shortcutPath.ToLowerInvariant();

        // Capture the entry by value via TryGetValue, then validate it — so a concurrent ClearCache()
        // (_cache.Clear() on the UI thread, now reachable via the 清理缓存 button while the STA build
        // thread is resolving shortcuts) can't make a follow-up _cache[key] indexer throw
        // KeyNotFoundException between the validity check and the read.
        if (_cache.TryGetValue(key, out var cached) && IsStillValid(cached, shortcutPath))
        {
            lock (_statsLock) { _cacheHits++; }
            return cached.TargetPath;
        }

        var targetPath = ResolveShortcutActual(shortcutPath);
        UpdateCache(shortcutPath, key, targetPath);
        return targetPath;
    }

    private static bool IsStillValid(ShortcutCacheEntry cached, string shortcutPath)
    {
        try
        {
            var fileInfo = new FileInfo(shortcutPath);
            // Trust last-write-time + size as the validator. A .lnk whose content changes virtually
            // always changes its size or mtime, so re-reading + SHA256-hashing every file on every
            // cache hit was pure hot-path cost (a cold scan re-resolves on any real change anyway).
            return cached.LastModified == fileInfo.LastWriteTime && cached.FileSize == fileInfo.Length;
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

            _cache[key] = new ShortcutCacheEntry
            {
                TargetPath = targetPath,
                LastModified = fileInfo.LastWriteTime,
                FileSize = fileInfo.Length,
            };

            ScheduleSave();
        }
        catch
        {
            // ignore cache update errors
        }
    }

    // Resolve via IShellLink/IPersistFile rather than the WScript.Shell ProgID — WSH is blocked by
    // policy in many hardened/managed environments and isn't available in an MSIX sandbox, where the
    // old path silently returned null for EVERY .lnk. IShellLink works in those environments.
    // The COM plumbing lives in TanMenu.Core.Interop.ShellLink because it must be source-generated
    // (built-in COM interop cannot work under NativeAOT) and TaskbarPinService needs the same types.
    private static string? ResolveShortcutActual(string shortcutPath)
    {
        try
        {
            using var link = ShellLink.Create();
            link.Load(shortcutPath);
            var target = link.GetPath();
            return string.IsNullOrEmpty(target) ? null : ConvertToLongPath(target);
        }
        catch
        {
            return null;
        }
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
                var data = JsonSerializer.Deserialize(
                    json, LinkCacheJsonContext.Default.DictionaryStringShortcutCacheEntry);
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
                var json = JsonSerializer.Serialize(
                    snapshot, LinkCacheJsonContext.Default.DictionaryStringShortcutCacheEntry);
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
        // Disarm the trailing-save timer so a NOT-YET-FIRED ScheduleSave (armed by a resolve just before
        // this clear) won't recreate the just-deleted cache file. This can't stop a callback the timer has
        // ALREADY dispatched — that one may briefly rewrite the file — but it's self-healing: the
        // following RaiseSettingsChanged rebuild re-resolves and re-saves the cache ~1s later anyway.
        _saveTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
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
