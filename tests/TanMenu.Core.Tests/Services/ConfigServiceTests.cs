using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class ConfigServiceTests
{
    private static IAppDataPaths NewPaths(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "TanMenuCfg_" + Path.GetRandomFileName());
        var paths = new AppDataPaths(Path.Combine(root, "Local"), Path.Combine(root, "Cache"));
        paths.EnsureCreated();
        return paths;
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsFoldersAndGeneral()
    {
        var paths = NewPaths(out var root);
        try
        {
            var svc1 = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc1.LoadAsync();                       // creates default (empty folders) + saves
            Assert.Empty(svc1.Config.Folders);

            svc1.Config.Folders.Add(@"C:\Windows");
            svc1.Config.General.ColButtonCount = 4;
            await svc1.SaveAsync();

            Assert.True(File.Exists(paths.ConfigFilePath));

            var svc2 = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc2.LoadAsync();

            Assert.True(svc2.HasValidConfig);
            Assert.Contains(@"C:\Windows", svc2.Config.Folders);
            Assert.Equal(4, svc2.Config.General.ColButtonCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_EmptyOrWhitespaceConfig_BacksUpBeforeResetting()
    {
        var paths = NewPaths(out var root);
        try
        {
            // Simulate a crash-truncated config file (existing but effectively empty).
            await File.WriteAllTextAsync(paths.ConfigFilePath, "   ");

            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();

            // The empty file must be backed up before being replaced with defaults, so the
            // user's prior setup is never silently discarded.
            var dir = Path.GetDirectoryName(paths.ConfigFilePath)!;
            var backups = Directory.GetFiles(dir, Path.GetFileName(paths.ConfigFilePath) + ".backup.*");
            Assert.NotEmpty(backups);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
