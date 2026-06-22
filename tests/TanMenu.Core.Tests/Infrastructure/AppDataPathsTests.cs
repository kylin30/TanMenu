using System.IO;
using TanMenu.Core.Infrastructure;
using Xunit;

namespace TanMenu.Core.Tests.Infrastructure;

public class AppDataPathsTests
{
    [Fact]
    public void DerivedPaths_AreUnderRoots_AndEnsureCreatedMakesFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "TanMenuTest_" + Path.GetRandomFileName());
        var local = Path.Combine(root, "Local");
        var cache = Path.Combine(root, "Cache");

        try
        {
            IAppDataPaths paths = new AppDataPaths(local, cache);

            Assert.Equal(local, paths.LocalFolder);
            Assert.Equal(cache, paths.LocalCacheFolder);
            Assert.Equal(Path.Combine(cache, "logs"), paths.LogsFolder);
            Assert.Equal(Path.Combine(local, "config.json"), paths.ConfigFilePath);
            Assert.Equal(Path.Combine(cache, "linkCache.json"), paths.LinkCacheFilePath);
            Assert.Equal(Path.Combine(cache, "iconCache.json"), paths.IconCacheFilePath);

            paths.EnsureCreated();

            Assert.True(Directory.Exists(local));
            Assert.True(Directory.Exists(cache));
            Assert.True(Directory.Exists(paths.LogsFolder));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
