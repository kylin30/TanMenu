using TanMenu.Core.Models;
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
        Assert.Equal("Win7", general.ThemeName);
        Assert.Equal("Alibaba PuHuiTi", general.FontFamily);
        Assert.True(general.ShowDefaultTools);
        Assert.NotEmpty(general.DefaultTools);
    }
}
