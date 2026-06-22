using System.IO;
using TanMenu.Core.Models;

namespace TanMenu.Core.Services;

public class MenuDataService
{
    private readonly IShortcutResolver _shortcutResolver;

    public MenuDataService(IShortcutResolver shortcutResolver)
    {
        _shortcutResolver = shortcutResolver;
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
                return null;

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
        catch
        {
            return null;
        }
    }

    private IEnumerable<DirectoryItem> GetFiles(string directory)
    {
        foreach (var file in Directory.GetFiles(directory))
        {
            var item = new DirectoryItem
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
        return Directory.GetDirectories(directory)
            .Select(d => new DirectoryItem
            {
                Name = Path.GetFileName(d) ?? string.Empty,
                FullPath = d,
                IsDirectory = true,
                IconKey = d
            });
    }
}
