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

    /// <summary>
    /// A hand-edited <c>config.json</c> can contain <c>"defaultTools": [null, {...}]</c> — the
    /// source-generated JSON context does not enforce non-null on deserialization despite
    /// <c>DefaultTools</c>'s C# element type being non-nullable — and before the Task 13 hardening this
    /// null-forgiving <c>!</c> stands in for exactly that deserialization gap. Both
    /// <c>MenuService.ComputeFingerprint</c> and <c>BuildDefaultToolsGroup</c> used to
    /// <see cref="NullReferenceException"/> on the null element; this pins that the build instead skips
    /// it and still renders every valid entry.
    /// </summary>
    [Fact]
    public async Task BuildMenuAsync_SkipsNullDefaultToolsElementInsteadOfThrowing()
    {
        var testRoot = CreateTempDirectory();
        try
        {
            var menuRoot = Path.Combine(testRoot, "menu");
            Directory.CreateDirectory(menuRoot); // no subfolders — isolates this test to the tools group

            var paths = new AppDataPaths(
                Path.Combine(testRoot, "data"),
                Path.Combine(testRoot, "cache"));
            paths.EnsureCreated();

            using var service = CreateService(paths, new FakeIconProvider([1]));
            var general = new GeneralConfig
            {
                ShowDefaultTools = true,
                DefaultTools = new List<DefaultTool>
                {
                    null!,
                    new() { Name = "计算器", Command = "calc.exe" },
                },
            };

            var result = await service.BuildMenuAsync(menuRoot, general, null);

            var toolsGroup = Assert.Single(result.Groups);
            Assert.Single(toolsGroup.Items); // the null element is skipped; calc.exe still renders
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
