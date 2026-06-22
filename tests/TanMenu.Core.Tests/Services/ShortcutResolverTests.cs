using System.IO;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class ShortcutResolverTests
{
    private static IAppDataPaths NewPaths(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "TanMenuLnk_" + Path.GetRandomFileName());
        var paths = new AppDataPaths(Path.Combine(root, "Local"), Path.Combine(root, "Cache"));
        paths.EnsureCreated();
        return paths;
    }

    [Fact]
    public void NonExistentShortcut_ReturnsNull()
    {
        var paths = NewPaths(out var root);
        try
        {
            IShortcutResolver resolver = new ShortcutResolver(paths);
            Assert.Null(resolver.ResolveShortcut(@"C:\definitely\not\here.lnk"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ResolvesRealLnk_WhenOneExists_AndCachesIt()
    {
        var paths = NewPaths(out var root);
        try
        {
            var sysLnkDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                "Programs");
            var anyLnk = Directory.Exists(sysLnkDir)
                ? Directory.EnumerateFiles(sysLnkDir, "*.lnk", SearchOption.AllDirectories).FirstOrDefault()
                : null;

            if (anyLnk is null)
            {
                // Environment has no shortcuts to resolve; the API contract still holds.
                return;
            }

            IShortcutResolver resolver = new ShortcutResolver(paths);
            var target = resolver.ResolveShortcut(anyLnk);

            Assert.False(string.IsNullOrEmpty(target));
            Assert.True(resolver.CacheCount >= 1);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
