using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace TanMenu.Core.Tests.Themes;

/// <summary>Guards the boundary between the official Fluent theme and TanMenu's layout-only rules.</summary>
public class ShellConstantTests
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

    private static string WpfRoot => Path.Combine(RepoRoot(), "src", "TanMenu.Wpf");

    [Fact]
    public void Application_UsesOfficialFluentThemeWithHeroUiTranslationLayer()
    {
        var styles = XDocument.Load(Path.Combine(WpfRoot, "App.axaml"))
            .Descendants().Where(e => e.Parent?.Name.LocalName == "Application.Styles").ToList();

        Assert.Equal(2, styles.Count);
        Assert.Equal("FluentAvaloniaTheme", styles[0].Name.LocalName);
        Assert.Equal("StyleInclude", styles[1].Name.LocalName);
        Assert.Equal("avares://TanMenu.Wpf/Themes/HeroUI.axaml", (string?)styles[1].Attribute("Source"));
    }

    [Fact]
    public void ThirdPartyAeroAndLegacyShellThemes_AreRemoved()
    {
        foreach (var retired in new[] { "Aero", "Shell" })
        {
            var directory = Path.Combine(WpfRoot, "Themes", retired);
            if (Directory.Exists(directory))
                Assert.Empty(Directory.GetFiles(directory, "*", SearchOption.AllDirectories));
        }

        var sources = Directory.GetFiles(WpfRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".axaml" or ".cs")
            .Select(File.ReadAllText);
        Assert.DoesNotContain(sources, body =>
            body.Contains("aero-theme-for-avalonia", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LayoutStyles_DoNotReplaceOfficialControlOrWindowTemplates()
    {
        foreach (var file in Directory.GetFiles(WpfRoot, "*.axaml", SearchOption.AllDirectories))
        {
            var doc = XDocument.Load(file);
            Assert.DoesNotContain(doc.Root!.DescendantsAndSelf(), e => e.Name.LocalName is
                "ControlTheme" or "ControlTemplate" or "WindowDrawnDecorationsTemplate");
        }
    }

    [Fact]
    public void EveryWindow_InheritsTheApplicationTheme()
    {
        foreach (var file in Directory.GetFiles(WpfRoot, "*Window.axaml", SearchOption.AllDirectories))
        {
            var root = XDocument.Load(file).Root!;
            foreach (var property in new[]
                     {
                         "Background", "TransparencyLevelHint", "SystemDecorations",
                         "ExtendClientAreaToDecorationsHint", "ExtendClientAreaTitleBarHeightHint",
                         "WindowDecorationsTheme"
                     })
                Assert.Null(root.Attribute(property));
        }
    }

    [Fact]
    public void MainWindowUsesStableResponsiveDimensions()
    {
        var main = XDocument.Load(Path.Combine(WpfRoot, "MainWindow.axaml")).Root!;
        var host = File.ReadAllText(Path.Combine(WpfRoot, "Services", "WindowHost.cs"));

        var placeholderWidth = Regex.Match(host, @"PlaceholderWidthDip\s*=\s*(?<v>[0-9.]+)\s*;")
            .Groups["v"].Value;
        var placeholderHeight = Regex.Match(host, @"PlaceholderHeightDip\s*=\s*(?<v>[0-9.]+)\s*;")
            .Groups["v"].Value;

        Assert.Equal((string?)main.Attribute("Width"), placeholderWidth);
        Assert.Equal((string?)main.Attribute("Height"), placeholderHeight);
        Assert.DoesNotContain(main.Descendants(), e => e.Name.LocalName == "FANavigationView");
        Assert.Contains(main.Descendants(), e =>
            e.Name.LocalName == "Border" &&
            (string?)e.Attribute(XamlName) == "SidebarPanel" &&
            (string?)e.Attribute("Classes") == "hero-sidebar-panel");
        Assert.Contains(main.Descendants(), e =>
            e.Name.LocalName == "Button" &&
            (string?)e.Attribute(XamlName) == "OpenRootNavButton");
        Assert.DoesNotContain(main.Descendants(), e => e.Name.LocalName is "Menu" or "MenuItem");
    }

    [Fact]
    public void MainWindow_DoesNotAppearInTaskbar()
    {
        var main = XDocument.Load(Path.Combine(WpfRoot, "MainWindow.axaml")).Root!;

        Assert.Equal("False", (string?)main.Attribute("ShowInTaskbar"));
    }

    [Fact]
    public void MinWindowWidth_MatchesThePlacementFloor()
    {
        var main = XDocument.Load(Path.Combine(WpfRoot, "MainWindow.axaml")).Root!;
        var host = File.ReadAllText(Path.Combine(WpfRoot, "Services", "WindowHost.cs"));
        var fit = Regex.Match(host, @"MinWindowWidthDip\s*=\s*(?<v>[0-9.]+)\s*;")
            .Groups["v"].Value;
        Assert.Equal((string?)main.Attribute("MinWidth"), fit);
    }

    [Fact]
    public void MinWindowHeight_MatchesThePlacementFloor()
    {
        var main = XDocument.Load(Path.Combine(WpfRoot, "MainWindow.axaml")).Root!;
        var host = File.ReadAllText(Path.Combine(WpfRoot, "Services", "WindowHost.cs"));
        var fit = Regex.Match(host, @"MinWindowHeightDip\s*=\s*(?<v>[0-9.]+)\s*;")
            .Groups["v"].Value;
        Assert.Equal((string?)main.Attribute("MinHeight"), fit);
    }
}
