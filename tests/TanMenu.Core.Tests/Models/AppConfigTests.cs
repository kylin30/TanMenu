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

        Assert.True(general.AutoClose);
        Assert.Equal("Auto", general.Language);
        Assert.True(general.TopMost);
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
}
