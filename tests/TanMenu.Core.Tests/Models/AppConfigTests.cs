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
        // WindowWidth/WindowHeight (Round 2) and WindowLeft/WindowTop (Round 4, item 4) are all retired
        // in place — nothing reads or writes them. Their DEFAULTS still matter: an absent key in an old
        // config.json deserializes to them, so pinning the defaults pins the retirement contract.
        Assert.Equal(1000, general.WindowWidth);
        Assert.Equal(640, general.WindowHeight);
        Assert.Null(general.WindowLeft);
        Assert.Null(general.WindowTop);
        Assert.False(general.WindowMaximized);
        Assert.False(general.TaskbarPinPromptDismissed);
    }

    [Fact]
    public void NewGeneralConfig_NavRailIsCollapsedByDefault()
    {
        // NavRailExpanded is retired as of Round 4, item 1: the rail is permanently icon-only (48 px)
        // and nothing reads or writes this property any more. The default still has to be false — an
        // absent key in an old config.json deserializes to it, and the field is kept purely so such a
        // file still loads. The test name predates the retirement and is kept so the case's history is
        // greppable.
        var general = new GeneralConfig();

        Assert.False(general.NavRailExpanded);
    }
}
