using System.Globalization;
using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class AppLanguageTests
{
    [Fact]
    public void Resolve_AutoFollowsChineseCulture()
    {
        Assert.Equal(AppLanguage.ZhHans, AppLanguage.Resolve(AppLanguage.Auto, new CultureInfo("zh-CN")));
    }

    [Fact]
    public void Resolve_AutoFallsBackToEnglishForNonChineseCulture()
    {
        Assert.Equal(AppLanguage.EnUs, AppLanguage.Resolve(AppLanguage.Auto, new CultureInfo("en-US")));
    }

    [Fact]
    public void LocalizeToolName_LocalizesUneditedBuiltInNames()
    {
        Assert.Equal("Calculator", AppLanguage.LocalizeToolName("calc.exe", "计算器", AppLanguage.EnUs));
        Assert.Equal("计算器", AppLanguage.LocalizeToolName("calc.exe", "Calculator", AppLanguage.ZhHans));
    }

    [Fact]
    public void LocalizeToolName_PreservesUserEditedNames()
    {
        Assert.Equal("My Tool", AppLanguage.LocalizeToolName("calc.exe", "My Tool", AppLanguage.EnUs));
    }
}
