using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Themes;

/// <summary>Guards the WinUI-style page command surface inside the adaptive app shell.</summary>
public class MenuBarTests
{
    private static readonly XName XamlName =
        XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TanMenu.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Source(string file) => Path.Combine(RepoRoot(), "src", "TanMenu.Wpf", file);

    [Fact]
    public void LauncherUsesAdaptiveNavigationAndThemeNativeActions()
    {
        var doc = XDocument.Load(Source("MainWindow.axaml"));
        var navigation = Assert.Single(doc.Descendants(), e => e.Name.LocalName == "FANavigationView");

        Assert.Equal("Auto", (string?)navigation.Attribute("PaneDisplayMode"));
        Assert.Equal("False", (string?)navigation.Attribute("AlwaysShowHeader"));
        Assert.Equal("0", (string?)navigation.Attribute("CompactModeThresholdWidth"));
        Assert.Equal("1440", (string?)navigation.Attribute("ExpandedModeThresholdWidth"));
        Assert.Equal("48", (string?)navigation.Attribute("CompactPaneLength"));
        Assert.Equal("220", (string?)navigation.Attribute("OpenPaneLength"));
        Assert.Equal("False", (string?)navigation.Attribute("IsPaneOpen"));
        Assert.Equal("False", (string?)navigation.Attribute("IsPaneToggleButtonVisible"));
        Assert.DoesNotContain(navigation.Descendants(), e =>
            e.Name.LocalName == "Style" &&
            ((string?)e.Attribute("Selector"))?.Contains("TogglePaneButton", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(doc.Descendants(), e => e.Name.LocalName == "FACommandBar");
        Assert.DoesNotContain(doc.Descendants(), e => e.Name.LocalName is "Menu" or "MenuItem");

        var search = doc.Descendants().Single(e => (string?)e.Attribute(XamlName) == "SearchBox");
        Assert.Equal("{Binding Filter, Mode=TwoWay}", (string?)search.Attribute("Text"));
        Assert.Equal("Center", (string?)search.Attribute("HorizontalAlignment"));
        Assert.Equal("clearButton", (string?)search.Attribute("Classes"));
        Assert.Equal("320", (string?)search.Attribute("Width"));
        Assert.Equal("32", (string?)search.Attribute("Height"));
        Assert.Equal("0", (string?)search.Attribute("MinHeight"));
        Assert.Equal("0,8", (string?)search.Attribute("Margin"));
        Assert.Null(search.Attribute("Padding"));
        Assert.Null(search.Attribute("MaxWidth"));
        Assert.Null(search.Attribute("MinWidth"));
        Assert.Equal("User", (string?)search.Attribute("WindowDecorationProperties.ElementRole"));
        Assert.Equal("TitleBarHost", (string?)search.Parent!.Attribute(XamlName));
        var findIcon = Assert.Single(search.Descendants(), e =>
            e.Name.LocalName == "FASymbolIcon" && (string?)e.Attribute("Symbol") == "Find");
        Assert.Equal("8,0,0,0", (string?)findIcon.Attribute("Margin"));
        Assert.DoesNotContain(doc.Descendants(), e =>
            (string?)e.Attribute(XamlName) == "LauncherHeading");
    }

    [Fact]
    public void NavigationFooterProvidesAnExplicitFullExit()
    {
        var doc = XDocument.Load(Source("MainWindow.axaml"));
        var footer = Assert.Single(doc.Descendants(), e =>
            e.Name.LocalName == "FANavigationView.FooterMenuItems");
        var items = footer.Elements().ToArray();
        Assert.Equal(new[] { "SettingsNavItem", "ExitNavItem" },
            items.Select(e => (string?)e.Attribute(XamlName)).ToArray());
        var exit = items[1];

        Assert.Equal("exit", (string?)exit.Attribute("Tag"));
        Assert.Contains(exit.Descendants(), e =>
            e.Name.LocalName == "FAFontIconSource" &&
            (string?)e.Attribute("Glyph") == "\uE7E8" &&
            (string?)e.Attribute("FontFamily") == "Segoe Fluent Icons");
        Assert.Equal("彻底退出", AppLanguage.Text("NavExit", AppLanguage.ZhHans));
        Assert.Contains("ReferenceEquals(e.SelectedItem, ExitNavItem)",
            File.ReadAllText(Source("MainWindow.axaml.cs")));
    }

    [Fact]
    public void ToggleAndPageActionsShareTheNavigationItemLayout()
    {
        var doc = XDocument.Load(Source("MainWindow.axaml"));
        var menu = Assert.Single(doc.Descendants(), e => e.Name.LocalName == "FANavigationView.MenuItems");
        var items = menu.Elements().ToArray();
        Assert.Equal(new[] { "TogglePaneNavItem", "LauncherNavItem", "OpenRootNavItem", "RefreshNavItem" },
            items.Select(e => (string?)e.Attribute(XamlName)).ToArray());

        var actions = items.Where(e =>
            (string?)e.Attribute(XamlName) is "TogglePaneNavItem" or "OpenRootNavItem" or "RefreshNavItem").ToArray();
        Assert.Equal(new[] { "TogglePaneNavItem", "OpenRootNavItem", "RefreshNavItem" },
            actions.Select(e => (string?)e.Attribute(XamlName)).ToArray());
        Assert.All(actions, action => Assert.Equal("False", (string?)action.Attribute("SelectsOnInvoked")));
        Assert.Equal(new[] { "toggle-pane", "open-root", "refresh" },
            actions.Select(e => (string?)e.Attribute("Tag")).ToArray());

        var toggle = actions[0];
        Assert.Contains(toggle.Descendants(), e =>
            e.Name.LocalName == "FASymbolIconSource" &&
            (string?)e.Attribute("Symbol") == "GlobalNavigationButton");
        var code = File.ReadAllText(Source("MainWindow.axaml.cs"));
        Assert.Contains("AppNavigation.IsPaneOpen = !AppNavigation.IsPaneOpen", code);
        Assert.Contains("TogglePaneNavItem.Content = AppLanguage.Text(\"NavTogglePane\", lang)", code);
    }

    [Fact]
    public void EveryPageCommandUsesTheLibraryIconAndLocalizedLabel()
    {
        var doc = XDocument.Load(Source("MainWindow.axaml"));
        var actions = doc.Descendants().Where(e =>
            e.Name.LocalName == "FANavigationViewItem" &&
            (string?)e.Attribute(XamlName) is "OpenRootNavItem" or "RefreshNavItem").ToList();
        Assert.Equal(2, actions.Count);
        Assert.All(actions, action => Assert.Contains(action.Descendants(), e =>
            e.Name.LocalName == "FASymbolIconSource"));

        var code = File.ReadAllText(Source("MainWindow.axaml.cs"));
        var keys = Regex.Matches(code, @"(?:OpenRoot|Refresh)NavItem\.Content\s*=\s*AppLanguage\.Text\(""(?<k>[^""]+)"", lang\)")
            .Select(m => m.Groups["k"].Value).ToArray();
        Assert.Equal(new[] { "MenuOpenRoot", "MenuRefresh" }, keys);
        Assert.All(keys, key => Assert.False(string.IsNullOrWhiteSpace(AppLanguage.Text(key, AppLanguage.EnUs))));
    }

    [Fact]
    public void TitleBarSearchIsVisibleOnlyOnTheLauncherPage()
    {
        var code = File.ReadAllText(Source("MainWindow.axaml.cs"));
        Assert.Contains("SearchBox.IsVisible = false", code);
        Assert.Contains("SearchBox.IsVisible = true", code);
        Assert.Contains("SearchBox.DataContext = LauncherContent.ViewModel", code);
    }
}
