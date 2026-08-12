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
        Assert.DoesNotContain(doc.Descendants(), e => e.Name.LocalName == "FACommandBar");
        Assert.DoesNotContain(doc.Descendants(), e => e.Name.LocalName is "Menu" or "MenuItem");

        var search = doc.Descendants().Single(e => (string?)e.Attribute(XamlName) == "SearchBox");
        Assert.Equal("{Binding Filter, Mode=TwoWay}", (string?)search.Attribute("Text"));
        Assert.Equal("Left", (string?)search.Attribute("HorizontalAlignment"));
        Assert.Equal("clearButton", (string?)search.Attribute("Classes"));
        Assert.Equal("320", (string?)search.Attribute("Width"));
        Assert.Equal("32", (string?)search.Attribute("MinHeight"));
        Assert.Null(search.Attribute("Padding"));
        Assert.Null(search.Attribute("MaxWidth"));
        Assert.Null(search.Attribute("MinWidth"));
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
    public void PageActionsImmediatelyFollowTheSearchBox()
    {
        var doc = XDocument.Load(Source("MainWindow.axaml"));
        var search = doc.Descendants().Single(e => (string?)e.Attribute(XamlName) == "SearchBox");
        var toolbar = search.Parent!;
        var actions = toolbar.Elements().Where(e => e.Name.LocalName == "Button").ToArray();

        Assert.Equal(new[] { "MenuOpenRoot", "MenuRefresh" },
            actions.Select(e => (string?)e.Attribute(XamlName)).ToArray());
        Assert.Equal(new[] { "1", "2" },
            actions.Select(e => (string?)e.Attribute("Grid.Column")).ToArray());
    }

    [Fact]
    public void EveryPageCommandUsesTheLibraryIconAndLocalizedLabel()
    {
        var doc = XDocument.Load(Source("MainWindow.axaml"));
        var buttons = doc.Descendants().Where(e =>
            e.Name.LocalName == "Button" &&
            (string?)e.Attribute(XamlName) is "MenuOpenRoot" or "MenuRefresh").ToList();
        Assert.Equal(2, buttons.Count);
        Assert.All(buttons, button =>
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)button.Attribute(XamlName)));
            Assert.False(string.IsNullOrWhiteSpace((string?)button.Attribute("Click")));
            Assert.Contains(button.Descendants(), e => e.Name.LocalName == "FASymbolIcon");
        });

        var code = File.ReadAllText(Source("MainWindow.axaml.cs"));
        var keys = Regex.Matches(code, @"Menu(?:OpenRoot|Refresh)Text\.Text\s*=\s*AppLanguage\.Text\(""(?<k>[^""]+)"", lang\)")
            .Select(m => m.Groups["k"].Value).ToArray();
        Assert.Equal(new[] { "MenuOpenRoot", "MenuRefresh" }, keys);
        Assert.All(keys, key => Assert.False(string.IsNullOrWhiteSpace(AppLanguage.Text(key, AppLanguage.EnUs))));
    }
}
