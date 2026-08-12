using Avalonia.Styling;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class AppThemeTests
{
    [Fact]
    public void DefaultFontsPreferAlibabaPuHuiTiWithWindowsFallbacks()
    {
        Assert.Equal("Alibaba PuHuiTi", AppFonts.BundledConfigName);
        Assert.Contains("Microsoft YaHei UI", AppFonts.UiStack, StringComparison.Ordinal);
        Assert.Contains("Segoe UI", AppFonts.UiStack, StringComparison.Ordinal);
        Assert.Equal(AppFonts.UiStack, AppFonts.DisplayStack);
        Assert.Equal("默认（阿里巴巴普惠体）",
            AppLanguage.Text("DefaultFont", AppLanguage.ZhHans));
    }

    [Theory]
    [InlineData(AppAppearance.Light)]
    [InlineData(AppAppearance.Dark)]
    [InlineData(AppAppearance.System)]
    [InlineData("nonsense")]
    public void ToVariant_MapsEverySettingToTheOfficialThemeVariant(string appearance)
    {
        var expected = appearance switch
        {
            AppAppearance.Light => ThemeVariant.Light,
            AppAppearance.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

        Assert.Equal(expected, AppTheme.ToVariant(appearance));
    }
}
