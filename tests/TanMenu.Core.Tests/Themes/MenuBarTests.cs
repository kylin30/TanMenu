using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Themes;

/// <summary>Guards the HeroUI AppLayout navigation and title-bar command surface.</summary>
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
    public void LauncherUsesAStableCustomAppLayout()
    {
        var doc = XDocument.Load(Source("MainWindow.axaml"));
        Assert.DoesNotContain(doc.Descendants(), e => e.Name.LocalName == "FANavigationView");
        Assert.DoesNotContain(doc.Descendants(), e => e.Name.LocalName is "Menu" or "MenuItem");

        var sidebar = Assert.Single(doc.Descendants(), e =>
            (string?)e.Attribute(XamlName) == "SidebarPanel");
        Assert.Equal("hero-sidebar-panel", (string?)sidebar.Attribute("Classes"));
        Assert.Equal("hero-sidebar-chrome", (string?)sidebar.Parent!.Parent!.Attribute("Classes"));

        var shell = Assert.Single(doc.Root!.Elements(), e => e.Name.LocalName == "Grid");
        Assert.Equal("64,*", (string?)shell.Attribute("ColumnDefinitions"));
        Assert.Equal("#F5F5F5", (string?)shell.Attribute("Background"));

        var titleBar = Assert.Single(doc.Descendants(), e =>
            (string?)e.Attribute(XamlName) == "TitleBarHost");
        Assert.Equal("#F5F5F5", (string?)titleBar.Attribute("Background"));

        var main = Assert.Single(doc.Descendants(), e =>
            (string?)e.Attribute(XamlName) == "ContentHost");
        Assert.Equal("Grid", main.Name.LocalName);
        Assert.Equal("1", (string?)main.Attribute("Grid.Row"));
        Assert.Null((string?)main.Attribute("Classes"));
        Assert.Null((string?)main.Attribute("Background"));
        Assert.Contains(main.Descendants(), e => (string?)e.Attribute(XamlName) == "LauncherContent");
        Assert.Contains(main.Descendants(), e => (string?)e.Attribute(XamlName) == "SettingsContent");
    }

    [Fact]
    public void TitleBarSearchUsesHeroUiFieldAndIsLauncherOnly()
    {
        var doc = XDocument.Load(Source("MainWindow.axaml"));
        var search = doc.Descendants().Single(e => (string?)e.Attribute(XamlName) == "SearchBox");
        Assert.Equal("{Binding Filter, Mode=TwoWay}", (string?)search.Attribute("Text"));
        Assert.Equal("clearButton hero-search-input", (string?)search.Attribute("Classes"));
        Assert.Equal("User", (string?)search.Attribute("WindowDecorationProperties.ElementRole"));

        var shell = search.Parent!;
        Assert.Equal("SearchShell", (string?)shell.Attribute(XamlName));
        Assert.Equal("hero-search-shell", (string?)shell.Attribute("Classes"));
        Assert.Equal("Left", (string?)shell.Attribute("HorizontalAlignment"));
        Assert.Equal("Stretch", (string?)shell.Attribute("VerticalAlignment"));

        var commandBar = shell.Parent!;
        Assert.Equal("LauncherTitleCommands", (string?)commandBar.Attribute(XamlName));
        Assert.Equal("Horizontal", (string?)commandBar.Attribute("Orientation"));
        Assert.Equal("Left", (string?)commandBar.Attribute("HorizontalAlignment"));
        Assert.Equal("16,12,0,12", (string?)commandBar.Attribute("Margin"));
        Assert.Equal("8", (string?)commandBar.Attribute("Spacing"));

        var code = File.ReadAllText(Source("MainWindow.axaml.cs"));
        Assert.Contains("LauncherTitleCommands.IsVisible = false", code);
        Assert.Contains("LauncherTitleCommands.IsVisible = true", code);
        Assert.Contains("SearchBox.DataContext = LauncherContent.ViewModel", code);
    }

    [Fact]
    public void SidebarCommandsShareOneButtonGrammar()
    {
        var doc = XDocument.Load(Source("MainWindow.axaml"));
        var navigationExpected = new[]
        {
            "LauncherNavButton", "SettingsNavButton", "ExitNavButton"
        };
        var navigationButtons = doc.Descendants().Where(e =>
            e.Name.LocalName == "Button" && navigationExpected.Contains((string?)e.Attribute(XamlName))).ToList();

        Assert.Equal(navigationExpected, navigationButtons.Select(e => (string)e.Attribute(XamlName)!).ToArray());
        Assert.All(navigationButtons, button => Assert.Contains("hero-sidebar-item", (string?)button.Attribute("Classes")));
        Assert.All(navigationButtons, button => Assert.Single(button.Elements()));
        Assert.Same(navigationButtons[0].Parent, navigationButtons[1].Parent);
        Assert.Equal("1", (string?)navigationButtons[2].Attribute("Grid.Row"));

        var titleCommands = new[] { "OpenRootNavButton", "RefreshNavButton" };
        var titleButtons = doc.Descendants().Where(e =>
            e.Name.LocalName == "Button" && titleCommands.Contains((string?)e.Attribute(XamlName))).ToList();
        Assert.Equal(titleCommands, titleButtons.Select(e => (string)e.Attribute(XamlName)!).ToArray());
        Assert.All(titleButtons, button =>
        {
            Assert.Contains("hero-icon-button", (string?)button.Attribute("Classes"));
            Assert.Contains("hero-title-command", (string?)button.Attribute("Classes"));
        });
        Assert.All(titleButtons, button =>
            Assert.Equal("LauncherTitleCommands", (string?)button.Parent!.Attribute(XamlName)));

        var appIcon = Assert.Single(doc.Descendants(), e =>
            e.Name.LocalName == "Image" && (string?)e.Attribute("Source") == "/wwwroot/app-tile.png");
        Assert.Contains(appIcon.Ancestors(), e =>
            (string?)e.Attribute("Classes") == "hero-sidebar-chrome");
    }

    [Fact]
    public void SidebarIsAPermanentFixedWidthIconRail()
    {
        var main = XDocument.Load(Source("MainWindow.axaml"));
        Assert.DoesNotContain(main.Descendants(), e => (string?)e.Attribute(XamlName) == "TogglePaneButton");

        var code = File.ReadAllText(Source("MainWindow.axaml.cs"));
        Assert.DoesNotContain("OnTogglePaneClick", code);
        Assert.DoesNotContain("_sidebarCollapsed", code);
        Assert.Contains("LauncherNavButton.Classes.Set(\"is-active\", !settings)", code);
        Assert.Contains("SettingsNavButton.Classes.Set(\"is-active\", settings)", code);

        var theme = XDocument.Load(Source(Path.Combine("Themes", "HeroUI.axaml")));
        Assert.Contains(theme.Descendants(), e =>
            e.Name.LocalName == "Style" &&
            (string?)e.Attribute("Selector") == "Border.hero-sidebar-chrome");
        var sidebar = Assert.Single(theme.Descendants(), e =>
            e.Name.LocalName == "Style" && (string?)e.Attribute("Selector") == "Border.hero-sidebar-panel");
        Assert.Contains(sidebar.Elements(), e =>
            (string?)e.Attribute("Property") == "Width" && (string?)e.Attribute("Value") == "48");
        Assert.Contains(sidebar.Elements(), e =>
            (string?)e.Attribute("Property") == "Padding" && (string?)e.Attribute("Value") == "6,4,6,12");
        Assert.DoesNotContain(theme.Descendants(), e =>
            ((string?)e.Attribute("Selector"))?.Contains("collapsed", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(theme.Descendants(), e =>
            e.Name.LocalName == "DoubleTransition" && (string?)e.Attribute("Property") == "Width");
    }

    [Fact]
    public void NavigationLabelsAreLocalizedAndExitIsExplicit()
    {
        var code = File.ReadAllText(Source("MainWindow.axaml.cs"));
        foreach (var key in new[] { "NavApps", "MenuOpenRoot", "MenuRefresh", "NavSettings", "NavExit" })
        {
            Assert.Contains($"AppLanguage.Text(\"{key}\", lang)", code);
            Assert.False(string.IsNullOrWhiteSpace(AppLanguage.Text(key, AppLanguage.EnUs)));
        }
        Assert.Contains("AutomationProperties.NameProperty", code);

        Assert.Equal("彻底退出", AppLanguage.Text("NavExit", AppLanguage.ZhHans));
        Assert.Contains("await SettingsContent.FlushAsync()", code);
        Assert.Contains("(Application.Current as App)?.ExitApp()", code);
    }
}
