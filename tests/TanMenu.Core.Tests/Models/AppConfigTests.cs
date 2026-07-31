using TanMenu.Core.Models;
using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Models;

public class AppConfigTests
{
    [Fact]
    public void NewAppConfig_HasEmptyFolders_NoPersonalPaths()
    {
        var config = new AppConfig();

        Assert.NotNull(config.Folders);
        Assert.Empty(config.Folders);
    }

    [Fact]
    public void NewGeneralConfig_HasExpectedDefaults()
    {
        var general = new GeneralConfig();

        Assert.False(general.AutoClose);
        Assert.Equal("Auto", general.Language);
        Assert.False(general.TopMost);
        Assert.False(general.ShowInTaskbar);
        Assert.Equal(8, general.PositionOffset);
        Assert.Equal(10, general.ColButtonCount);
        Assert.Equal(AppConfig.CanonicalTheme, general.ThemeName);
        Assert.Equal(AppAppearance.System, general.Appearance);
        // "" = the app's own default (the system UI face). No font ships inside the executable any
        // more, so a non-empty default here would name a family that may not be installed.
        Assert.Equal("", general.FontFamily);
        Assert.True(general.ShowDefaultTools);
        Assert.NotEmpty(general.DefaultTools);
    }

    [Fact]
    public void Defaults_AreAppWindowNotFlyout()
    {
        var general = new GeneralConfig();

        Assert.False(general.AutoClose);
        Assert.False(general.TopMost);
        Assert.Equal(0, general.SchemaVersion); // 0 = "not yet stamped"; see ConfigService.Normalize
        Assert.Equal(1000, general.WindowWidth);
        Assert.Equal(640, general.WindowHeight);
        Assert.Equal(-1, general.WindowLeft);
        Assert.Equal(-1, general.WindowTop);
        Assert.False(general.WindowMaximized);
        Assert.False(general.TaskbarPinPromptDismissed);
    }
}
