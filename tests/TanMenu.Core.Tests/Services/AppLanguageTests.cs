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

    /// <summary>
    /// <c>DefaultTool.Command</c> is a non-nullable C# string, but <c>DefaultTools</c> is documented as
    /// user-editable via <c>config.json</c> and the source-generated JSON context does not enforce
    /// non-null on deserialization, so a hand-edited <c>"command": null</c> reaches this method as a
    /// real null at runtime despite the type. Before the Task 13 hardening this threw
    /// <see cref="ArgumentNullException"/> out of a <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/>
    /// lookup on a null key (<c>Path.GetFileName(null)</c> returns null, and a null dictionary key is
    /// rejected) — reachable from <c>ToolsCard.ReloadFromConfig</c>, which runs from
    /// <c>OptionsWindow</c>'s constructor and on every <c>SettingsChanged</c> broadcast, with no global
    /// unhandled-exception hook in Avalonia to catch it.
    /// </summary>
    [Fact]
    public void LocalizeToolName_NullCommand_FallsBackToCurrentNameInsteadOfThrowing()
    {
        Assert.Equal("My Tool", AppLanguage.LocalizeToolName(null, "My Tool", AppLanguage.EnUs));
        Assert.Equal("", AppLanguage.LocalizeToolName(null, null, AppLanguage.EnUs));
        Assert.Equal("", AppLanguage.LocalizeToolName(null, "", AppLanguage.ZhHans));
    }

    [Fact]
    public void UpdateNotice_ExplainsThatDownloadRequiresUserAction()
    {
        Assert.Contains("点击下载", AppLanguage.Format("UpdateNoticeAvailable", AppLanguage.ZhHans, "1.2.3"));
        Assert.Contains("click to download", AppLanguage.Format("UpdateNoticeAvailable", AppLanguage.EnUs, "1.2.3"));
        Assert.Contains("是否现在下载", AppLanguage.Format("UpdateDownloadPrompt", AppLanguage.ZhHans, "1.2.3"));
    }
}
