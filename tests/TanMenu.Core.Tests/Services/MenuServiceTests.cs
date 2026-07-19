using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Models;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public sealed class MenuServiceTests
{
    [Fact]
    public async Task PersistedIconCacheIsUsedByNextServiceInstance()
    {
        var testRoot = CreateTempDirectory();
        try
        {
            var menuRoot = Path.Combine(testRoot, "menu");
            var group = Path.Combine(menuRoot, "Group");
            Directory.CreateDirectory(group);
            File.WriteAllBytes(Path.Combine(group, "app.exe"), [1, 2, 3]);

            var paths = new AppDataPaths(
                Path.Combine(testRoot, "data"),
                Path.Combine(testRoot, "cache"));
            paths.EnsureCreated();
            var expectedPng = new byte[] { 10, 20, 30, 40 };

            var firstIcons = new FakeIconProvider(expectedPng);
            using (var first = CreateService(paths, firstIcons))
            {
                var result = await first.BuildMenuAsync(menuRoot, GeneralWithoutTools(), null);
                Assert.Single(result.Pending);
                await first.ExtractIconsAsync(result.Pending, _ => { }, () => false);
            }

            Assert.True(File.Exists(paths.IconCacheFilePath));

            var secondIcons = new FakeIconProvider([99]);
            using (var second = CreateService(paths, secondIcons))
            {
                var result = await second.BuildMenuAsync(menuRoot, GeneralWithoutTools(), null);
                Assert.Empty(result.Pending);
                Assert.Equal(Convert.ToBase64String(expectedPng),
                    Assert.Single(Assert.Single(result.Groups).Items).IconBase64);
            }

            Assert.Equal(0, secondIcons.ExtractionCount);
        }
        finally
        {
            Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ChangedFileInvalidatesPersistedIcon()
    {
        var testRoot = CreateTempDirectory();
        try
        {
            var menuRoot = Path.Combine(testRoot, "menu");
            var group = Path.Combine(menuRoot, "Group");
            Directory.CreateDirectory(group);
            var appPath = Path.Combine(group, "app.exe");
            File.WriteAllBytes(appPath, [1, 2, 3]);

            var paths = new AppDataPaths(
                Path.Combine(testRoot, "data"),
                Path.Combine(testRoot, "cache"));
            paths.EnsureCreated();
            using (var first = CreateService(paths, new FakeIconProvider([10])))
            {
                var result = await first.BuildMenuAsync(menuRoot, GeneralWithoutTools(), null);
                await first.ExtractIconsAsync(result.Pending, _ => { }, () => false);
            }

            File.WriteAllBytes(appPath, [1, 2, 3, 4]);

            using var second = CreateService(paths, new FakeIconProvider([20]));
            var changed = await second.BuildMenuAsync(menuRoot, GeneralWithoutTools(), null);
            Assert.Single(changed.Pending);
        }
        finally
        {
            Directory.Delete(testRoot, true);
        }
    }

    private static MenuService CreateService(IAppDataPaths paths, IIconProvider icons)
    {
        var data = new MenuDataService(new FakeShortcutResolver(), NullLogger<MenuDataService>.Instance);
        return new MenuService(data, icons, paths);
    }

    private static GeneralConfig GeneralWithoutTools() => new()
    {
        ShowDefaultTools = false,
        DefaultTools = [],
    };

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TanMenuTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeIconProvider(byte[] png) : IIconProvider
    {
        public int ExtractionCount { get; private set; }

        public byte[]? GetIconPngBytes(string path)
        {
            ExtractionCount++;
            return png;
        }

        public byte[]? GetDefaultAppIconPngBytes() => [0];
    }

    private sealed class FakeShortcutResolver : IShortcutResolver
    {
        public string? ResolveShortcut(string shortcutPath) => null;
        public void ClearCache() { }
        public int CacheCount => 0;
        public double HitRate => 0;
    }
}
