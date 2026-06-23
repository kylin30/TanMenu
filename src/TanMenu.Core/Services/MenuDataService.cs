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

    public Task<List<DirectoryContents>> GetDirectoryContents(IEnumerable<string> directories)
    {
        var result = new List<DirectoryContents>();

        foreach (var directory in directories)
        {
            var content = GetDirectoryContent(directory);
            if (content != null)
                result.Add(content);
        }

        return Task.FromResult(result);
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
        string[] files;
        try
        {
            files = Directory.GetFiles(directory);
        }
        catch (Exception ex)
        {
            // Permission denied / offline share on the group folder → no files, but the group
            // (and its subfolders) still render. Logged so it's diagnosable.
            _logger.LogWarning(ex, "枚举文件失败: {Directory}", directory);
            yield break;
        }

        foreach (var file in files)
        {
            DirectoryItem? item = null;
            try
            {
                item = new DirectoryItem
                {
                    Name = Path.GetFileName(file),
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
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "处理文件项失败，跳过: {File}", file);
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

        var targetExists = File.Exists(targetPath) || Directory.Exists(targetPath);
        if (!targetExists)
        {
            item.IsDisabled = true;
        }
        else
        {
            item.IconKey = targetPath;
        }
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
                IconKey = d
            };
        }
    }
}
