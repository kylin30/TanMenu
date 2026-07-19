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

    [Fact]
    public void UpdateNotice_ExplainsThatDownloadRequiresUserAction()
    {
        Assert.Contains("点击下载", AppLanguage.Format("UpdateNoticeAvailable", AppLanguage.ZhHans, "1.2.3"));
        Assert.Contains("click to download", AppLanguage.Format("UpdateNoticeAvailable", AppLanguage.EnUs, "1.2.3"));
        Assert.Contains("是否现在下载", AppLanguage.Format("UpdateDownloadPrompt", AppLanguage.ZhHans, "1.2.3"));
    }
}
