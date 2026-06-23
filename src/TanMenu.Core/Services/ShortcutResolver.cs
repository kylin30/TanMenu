using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TanMenu.Core.Infrastructure;

namespace TanMenu.Core.Services;

public sealed class ShortcutResolver : IShortcutResolver
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly string _cacheFile;
    private readonly object _fileLock = new();

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

            SaveCacheAsync();
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
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            return Convert.ToHexString(md5.ComputeHash(stream));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? ResolveShortcutActual(string shortcutPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            var shortcut = shell.CreateShortcut(shortcutPath);
            string targetPath = shortcut.TargetPath;

            return !string.IsNullOrEmpty(targetPath) ? ConvertToLongPath(targetPath) : null;
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

    private void SaveCacheAsync()
    {
        var snapshot = _cache.ToDictionary(x => x.Key, x => x.Value);
        var cacheFile = _cacheFile;
        Task.Run(() =>
        {
            lock (_fileLock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
                    var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = false });
                    File.WriteAllText(cacheFile, json, Encoding.UTF8);
                }
                catch
                {
                    // ignore cache save errors
                }
            }
        });
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
