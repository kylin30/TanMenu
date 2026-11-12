using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Services;
using TanMenu.Wpf.Island;
using TanMenu.Wpf.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class IslandPreviewHostTests
{
    [Fact]
    public void Constructor_DoesNotCreateAWindow()
    {
        var created = 0;
        _ = NewHost(out _, out _, () =>
        {
            created++;
            throw new InvalidOperationException("window was not requested");
        });

        Assert.Equal(0, created);
    }

    [Fact]
    public void Show_WhenCreationThrows_DoesNotEscape()
    {
        var host = NewHost(out _, out _, () => throw new InvalidOperationException("create failed"));

        var error = Record.Exception(() => host.Show());

        Assert.Null(error);
    }

    [Fact]
    public void SettingsChanged_WhenSwitchTurnsOn_AttemptsToShow_AndDisposeStopsListening()
    {
        var attempts = 0;
        var host = NewHost(out var config, out var events, () =>
        {
            attempts++;
            throw new InvalidOperationException("no window in this test");
        });

        config.Config.General.ShowIslandPreview = false;
        events.RaiseSettingsChanged();
        Assert.Equal(0, attempts);

        config.Config.General.ShowIslandPreview = true;
        events.RaiseSettingsChanged();
        Assert.Equal(1, attempts);

        host.Dispose();
        host.Dispose();
        events.RaiseSettingsChanged();
        Assert.Equal(1, attempts);
    }

    private static IslandPreviewHost NewHost(
        out ConfigService config,
        out AppEvents events,
        Func<IslandPreviewWindow> create)
    {
        var root = Path.Combine(Path.GetTempPath(), "TanMenuIsland_" + Path.GetRandomFileName());
        var paths = new AppDataPaths(Path.Combine(root, "Local"), Path.Combine(root, "Cache"));
        config = new ConfigService(paths, NullLogger<ConfigService>.Instance);
        events = new AppEvents();
        return new IslandPreviewHost(config, events, create);
    }
}
