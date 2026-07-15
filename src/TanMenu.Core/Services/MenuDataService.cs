using System.IO;
using Microsoft.Extensions.Logging;
using TanMenu.Core.Models;

namespace TanMenu.Core.Services;

public class MenuDataService
{
    private readonly IShortcutResolver _shortcutResolver;
    private readonly ILogger<MenuDataService> _logger;

    public MenuDataService(IShortcutResolver shortcutResolver, ILogger<MenuDataService> logger)
    {
        _shortcutResolver = shortcutResolver;
        _logger = logger;
    }

    // Synchronous: the whole body is synchronous shell/filesystem work and the caller already runs it
    // on a dedicated STA thread. The old Task wrapper was pure ceremony (a completed Task allocated then
    // synchronously unwrapped) and a latent trap — an await added inside would have hopped off the STA
    // apartment mid-shell-COM.
    public List<DirectoryContents> GetDirectoryContents(IEnumerable<string> directories)
    {
        var result = new List<DirectoryContents>();

        foreach (var directory in directories)
        {
            var content = GetDirectoryContent(directory);
            if (content != null)
                result.Add(content);
        }

        return result;
    }

    private DirectoryContents? GetDirectoryContent(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                _logger.LogWarning("分类目录不存在或不可访问，跳过: {Directory}", directory);
                return null;
            }
        }
        catch (Exception ex)
        {
            // Directory.Exists itself can throw on e.g. an offline UNC path.
            _logger.LogWarning(ex, "检查分类目录失败，跳过: {Directory}", directory);
            return null;
        }

        // GetFiles/GetDirectories are individually resilient (below), so one failing step or
        // item no longer discards the whole group — we return whatever DID enumerate.
        var content = new DirectoryContents
        {
            Directory = directory,
            DirectoryName = Path.GetFileName(directory) ?? string.Empty,
            Items = new List<DirectoryItem>()
        };
        content.Items.AddRange(GetFiles(directory));
        content.Items.AddRange(GetDirectories(directory));
        return content;
    }

    private IEnumerable<DirectoryItem> GetFiles(string directory)
    {
        FileInfo[] files;
        try
        {
            // Enumerate as FileInfo so each item's last-write-time + size come FREE from the directory
            // read — no extra per-file stat later in the icon layer to validate the cache.
            files = new DirectoryInfo(directory).GetFiles();
        }
        catch (Exception ex)
        {
            // Permission denied / offline share on the group folder → no files, but the group
            // (and its subfolders) still render. Logged so it's diagnosable.
            _logger.LogWarning(ex, "枚举文件失败: {Directory}", directory);
            yield break;
        }

        foreach (var fi in files)
        {
            DirectoryItem? item = null;
            try
            {
                var file = fi.FullName;
                item = new DirectoryItem
                {
                    Name = fi.Name,
                    FullPath = file,
                    IsDirectory = false
                };

                if (Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    item.Name = CleanShortcutName(item.Name);
                    ProcessShortcut(item);
                }
                else
                {
                    item.IconKey = file;
                    item.IconMtime = fi.LastWriteTimeUtc; // reuse the enumeration's metadata
                    item.IconSize = fi.Length;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "处理文件项失败，跳过: {File}", fi.FullName);
                item = null;
            }

            if (item != null)
                yield return item;
        }
    }

    public static string CleanShortcutName(string fileName)
    {
        if (fileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            fileName = fileName[..^4];

        if (fileName.EndsWith(" - 快捷方式", StringComparison.OrdinalIgnoreCase))
            fileName = fileName[..^7];
        else if (fileName.EndsWith(" - Shortcut", StringComparison.OrdinalIgnoreCase))
            fileName = fileName[..^11];

        return fileName;
    }

    private void ProcessShortcut(DirectoryItem item)
    {
        var targetPath = _shortcutResolver.ResolveShortcut(item.FullPath);
        if (string.IsNullOrEmpty(targetPath))
        {
            item.IsDisabled = true;
            return;
        }

        item.TargetPath = targetPath;

        // Stat the target ONCE here: a file target yields the icon key + (mtime,size) for the cache
        // validator; a folder target is a static icon key. This replaces the old File.Exists(target)
        // probe followed by a second FileInfo(target) in the icon layer (the per-.lnk double stat).
        try
        {
            var fi = new FileInfo(targetPath);
            if (fi.Exists)
            {
                item.IconKey = targetPath;
                item.IconMtime = fi.LastWriteTimeUtc;
                item.IconSize = fi.Length;
                return;
            }
        }
        catch { /* fall through to the directory check */ }

        if (Directory.Exists(targetPath))
            item.IconKey = targetPath; // folder target → static icon key (mtime/size left 0)
        else
            item.IsDisabled = true;    // target gone → disabled, no icon key
    }

    private IEnumerable<DirectoryItem> GetDirectories(string directory)
    {
        string[] dirs;
        try
        {
            dirs = Directory.GetDirectories(directory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "枚举子目录失败: {Directory}", directory);
            yield break;
        }

        foreach (var d in dirs)
        {
            yield return new DirectoryItem
            {
                Name = Path.GetFileName(d) ?? string.Empty,
                FullPath = d,
                IsDirectory = true,
                IconKey = d // folder icon is static → caches under a stable (default, 0) key
            };
        }
    }
}
