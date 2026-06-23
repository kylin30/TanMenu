using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class MenuDataServiceTests
{
    private sealed class FakeShortcutResolver : IShortcutResolver
    {
        public string? ResolveShortcut(string shortcutPath) => null;
        public void ClearCache() { }
        public int CacheCount => 0;
        public double HitRate => 0;
    }

    [Theory]
    [InlineData("Notepad.lnk", "Notepad")]
    [InlineData("Visual Studio - 快捷方式.lnk", "Visual Studio")]
    [InlineData("Chrome - Shortcut.lnk", "Chrome")]
    [InlineData("Plain.txt", "Plain.txt")]
    public void CleanShortcutName_StripsSuffixes(string input, string expected)
    {
        Assert.Equal(expected, MenuDataService.CleanShortcutName(input));
    }

    [Fact]
    public async Task GetDirectoryContents_ReturnsFilesAndDirs_WithIconKeys()
    {
        var root = Path.Combine(Path.GetTempPath(), "TanMenuScan_" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        try
        {
            var subDir = Path.Combine(root, "SubFolder");
            Directory.CreateDirectory(subDir);
            var filePath = Path.Combine(root, "readme.txt");
            await File.WriteAllTextAsync(filePath, "hi");

            var svc = new MenuDataService(new FakeShortcutResolver(), NullLogger<MenuDataService>.Instance);
            var result = await svc.GetDirectoryContents(new[] { root });

            Assert.Single(result);
            var contents = result[0];
            Assert.Equal(root, contents.Directory);
            Assert.Equal(Path.GetFileName(root), contents.DirectoryName);

            var file = contents.Items.Single(i => !i.IsDirectory);
            Assert.Equal("readme.txt", file.Name);
            Assert.Equal(filePath, file.IconKey);

            var dir = contents.Items.Single(i => i.IsDirectory);
            Assert.Equal("SubFolder", dir.Name);
            Assert.Equal(subDir, dir.IconKey);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task GetDirectoryContents_SkipsMissingDirectory()
    {
        var svc = new MenuDataService(new FakeShortcutResolver(), NullLogger<MenuDataService>.Instance);
        var result = await svc.GetDirectoryContents(new[] { @"C:\definitely\not\here" });
        Assert.Empty(result);
    }
}
