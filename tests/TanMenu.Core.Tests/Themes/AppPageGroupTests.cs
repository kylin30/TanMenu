using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace TanMenu.Core.Tests.Themes;

/// <summary>Guards the HeroUI all-app overview and responsive ItemCard launcher.</summary>
public class AppPageGroupTests
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
    private static XDocument AppPage() => XDocument.Load(Path.Combine(WpfRoot, "Views", "AppPage.axaml"));

    [Fact]
    public void AllCollectionsShareOneHorizontalNoScrollSurface()
    {
        var doc = AppPage();
        var body = Assert.Single(doc.Descendants(), e =>
            e.Name.LocalName == "ScrollViewer" && (string?)e.Attribute(XamlName) == "Body");
        Assert.Equal("Disabled", (string?)body.Attribute("HorizontalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)body.Attribute("VerticalScrollBarVisibility"));

        var collections = Assert.Single(body.Descendants(), e =>
            e.Name.LocalName == "ItemsControl" &&
            (string?)e.Attribute("ItemsSource") == "{Binding DisplayGroups}");
        var panel = Assert.Single(collections.Elements()
            .Single(e => e.Name.LocalName == "ItemsControl.ItemsPanel").Descendants(),
            e => e.Name.LocalName == "StackPanel");
        Assert.Equal("Horizontal", (string?)panel.Attribute("Orientation"));
        Assert.Equal("12", (string?)panel.Attribute("Spacing"));

        Assert.Contains(collections.Descendants(), e =>
            e.Name.LocalName == "Border" && (string?)e.Attribute("Classes") == "hero-launcher-group-panel");
        Assert.DoesNotContain(doc.Descendants(), e => (string?)e.Attribute("Click") == "OnCategoryClick");
    }

    [Fact]
    public void OverviewAlwaysUsesAllGroupsAndSearchOnlyRemovesEmptyMatches()
    {
        var doc = AppPage();
        var display = Assert.Single(doc.Descendants(), e =>
            e.Name.LocalName == "ItemsControl" &&
            (string?)e.Attribute("ItemsSource") == "{Binding DisplayGroups}");
        Assert.Equal("{Binding HasVisibleItems}", (string?)display.Attribute("IsVisible"));

        var columns = Assert.Single(display.Descendants(), e =>
            e.Name.LocalName == "ItemsControl" &&
            (string?)e.Attribute("ItemsSource") == "{Binding Columns}");
        var columnPanel = Assert.Single(columns.Elements()
            .Single(e => e.Name.LocalName == "ItemsControl.ItemsPanel").Descendants(),
            e => e.Name.LocalName == "StackPanel");
        Assert.Equal("Horizontal", (string?)columnPanel.Attribute("Orientation"));
        Assert.Contains(columns.Descendants(), e =>
            e.Name.LocalName == "ItemsControl" && (string?)e.Attribute("ItemsSource") == "{Binding Items}");

        var viewModel = File.ReadAllText(Path.Combine(WpfRoot, "ViewModels", "LauncherViewModel.cs"));
        Assert.Contains("public IReadOnlyList<MenuGroupVm> DisplayGroups", viewModel);
        Assert.Contains(": Groups;", viewModel);
        Assert.Contains("? Groups.Where(static group => group.HasVisibleItems).ToList()", viewModel);
        Assert.DoesNotContain("SelectedGroup", viewModel);

        var codeBehind = File.ReadAllText(Path.Combine(WpfRoot, "Views", "AppPage.axaml.cs"));
        Assert.DoesNotContain("OnCategoryClick", codeBehind);
    }

    [Fact]
    public void ApplicationsUseHeroUiItemCards()
    {
        var doc = AppPage();
        var tile = Assert.Single(doc.Descendants(), e =>
            e.Name.LocalName == "Button" &&
            ((string?)e.Attribute("Classes"))?.Contains("tm-tile", StringComparison.Ordinal) == true);

        Assert.Equal("hero-launcher-command tm-tile", (string?)tile.Attribute("Classes"));
        Assert.Null((string?)tile.Attribute("Width"));
        Assert.Equal("36", (string?)tile.Attribute("Height"));
        Assert.Equal("112", (string?)tile.Attribute("MinWidth"));
        Assert.Equal("Stretch", (string?)tile.Attribute("HorizontalAlignment"));
        Assert.Equal("{Binding !IsDisabled}", (string?)tile.Attribute("IsEnabled"));
        Assert.Equal("{Binding FullPath}", (string?)tile.Attribute("ToolTip.Tip"));

        var grid = Assert.Single(tile.Elements(), e => e.Name.LocalName == "Grid");
        Assert.Equal("24,*", (string?)grid.Attribute("ColumnDefinitions"));
        Assert.Equal("8", (string?)grid.Attribute("ColumnSpacing"));
        var icon = Assert.Single(tile.Descendants(), e => e.Name.LocalName == "Image");
        Assert.Equal("hero-launcher-command-icon", (string?)icon.Parent!.Attribute("Classes"));
        Assert.DoesNotContain(tile.Descendants(), e =>
            e.Name.LocalName == "TextBlock" && (string?)e.Attribute("Text") == "{Binding FullPath}");
        Assert.Single(tile.Descendants(), e =>
            e.Name.LocalName == "TextBlock" && (string?)e.Attribute("Text") == "{Binding Name}");
        var name = tile.Descendants().Single(e =>
            e.Name.LocalName == "TextBlock" && (string?)e.Attribute("Text") == "{Binding Name}");
        Assert.Null((string?)name.Attribute("MaxWidth"));
        Assert.Null((string?)name.Attribute("TextTrimming"));
        Assert.Equal("NoWrap", (string?)name.Attribute("TextWrapping"));

        var codeBehind = File.ReadAllText(Path.Combine(WpfRoot, "Views", "AppPage.axaml.cs"));
        Assert.Contains("_viewModel.LaunchItem(item);", codeBehind);
    }

    [Fact]
    public void ItemCardsHaveHeroUiHoverFocusAndPressedFeedback()
    {
        var theme = XDocument.Load(Path.Combine(WpfRoot, "Themes", "HeroUI.axaml"));
        var styles = theme.Descendants().Where(e => e.Name.LocalName == "Style").ToList();

        var card = styles.Single(e => (string?)e.Attribute("Selector") == "Button.hero-launcher-command");
        Assert.Contains(card.Descendants(), e =>
            e.Name.LocalName == "TransformOperationsTransition" &&
            (string?)e.Attribute("Duration") == "0:0:0.16");
        Assert.Contains(styles, e =>
            (string?)e.Attribute("Selector") == "Button.hero-launcher-command:pointerover /template/ ContentPresenter#PART_ContentPresenter");
        Assert.Contains(styles, e =>
            (string?)e.Attribute("Selector") == "Button.hero-launcher-command:pointerover" &&
            e.Elements().Any(setter => (string?)setter.Attribute("Property") == "RenderTransform"));
        var pressed = styles.Single(e => (string?)e.Attribute("Selector") == "Button.hero-launcher-command:pressed");
        Assert.Contains(pressed.Elements(), e =>
            (string?)e.Attribute("Property") == "RenderTransform" &&
            (string?)e.Attribute("Value") == "scale(0.97)");
        Assert.Contains(styles, e =>
            (string?)e.Attribute("Selector") == "Button.hero-launcher-command:focus-visible /template/ ContentPresenter#PART_ContentPresenter");

        var groupPanel = styles.Single(e => (string?)e.Attribute("Selector") == "Border.hero-launcher-group-panel");
        Assert.Contains(groupPanel.Elements(), e =>
            (string?)e.Attribute("Property") == "Margin" &&
            (string?)e.Attribute("Value") == "0,0,0,4");
        Assert.Contains(groupPanel.Elements(), e =>
            (string?)e.Attribute("Property") == "VerticalAlignment" &&
            (string?)e.Attribute("Value") == "Stretch");
        Assert.Contains(groupPanel.Elements(), e =>
            (string?)e.Attribute("Property") == "Padding" &&
            (string?)e.Attribute("Value") == "16");
        Assert.Contains(groupPanel.Elements(), e =>
            (string?)e.Attribute("Property") == "Background" &&
            (string?)e.Attribute("Value") == "{DynamicResource Hero.SurfaceBrush}");
        Assert.Contains(groupPanel.Elements(), e =>
            (string?)e.Attribute("Property") == "BoxShadow" &&
            (string?)e.Attribute("Value") == "{DynamicResource Hero.SurfaceShadow}");

        var commandPresenter = styles.Single(e =>
            (string?)e.Attribute("Selector") ==
            "Button.hero-launcher-command /template/ ContentPresenter#PART_ContentPresenter");
        Assert.Contains(commandPresenter.Elements(), e =>
            (string?)e.Attribute("Property") == "Background" &&
            (string?)e.Attribute("Value") == "{DynamicResource Hero.DefaultBrush}");
        Assert.Contains(commandPresenter.Elements(), e =>
            (string?)e.Attribute("Property") == "BorderThickness" &&
            (string?)e.Attribute("Value") == "0");
    }

    [Fact]
    public void CollectionHeaderProvidesAnAccessibleFolderAction()
    {
        var doc = AppPage();
        var button = Assert.Single(doc.Descendants(), e =>
            e.Name.LocalName == "Button" && (string?)e.Attribute("Click") == "OnGroupTitleClick");
        Assert.Equal("32", (string?)button.Parent!.Attribute("Height"));
        Assert.Equal("{Binding IsDirectoryClickable}", (string?)button.Attribute("IsVisible"));
        Assert.Equal("{Binding TitleTooltip}", (string?)button.Attribute("AutomationProperties.Name"));
        Assert.Contains(button.Descendants(), e =>
            e.Name.LocalName == "FASymbolIcon" && (string?)e.Attribute("Symbol") == "OpenFolder");
    }

    [Fact]
    public void PageUsesACompactSixteenDipNoScrollGrid()
    {
        var doc = AppPage();
        var body = doc.Descendants().Single(e =>
            e.Name.LocalName == "ScrollViewer" && (string?)e.Attribute(XamlName) == "Body");
        Assert.Equal("Disabled", (string?)body.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)body.Attribute("HorizontalScrollBarVisibility"));
        var content = Assert.Single(body.Elements(), e => e.Name.LocalName == "Grid");
        Assert.Null((string?)content.Attribute("Width"));
        var workspace = Assert.Single(content.Elements(), e =>
            e.Name.LocalName == "ItemsControl" && (string?)e.Attribute("ItemsSource") == "{Binding DisplayGroups}");
        Assert.Equal("16,4,16,16", (string?)workspace.Attribute("Margin"));

        var noticeHost = Assert.Single(doc.Descendants(), e =>
            e.Name.LocalName == "StackPanel" && (string?)e.Attribute("DockPanel.Dock") == "Top");
        Assert.Equal("NoticeHost", (string?)noticeHost.Attribute(XamlName));
        Assert.Equal("16,4,16,12", (string?)noticeHost.Attribute("Margin"));
        Assert.Equal("False", (string?)noticeHost.Attribute("IsVisible"));
        Assert.Equal(2, noticeHost.Elements().Count(e => e.Name.LocalName == "FAInfoBar"));

        var codeBehind = File.ReadAllText(Path.Combine(WpfRoot, "Views", "AppPage.axaml.cs"));
        Assert.Contains("UpdateNoticeHostVisibility()", codeBehind);
        Assert.Contains("NoticeHost.IsVisible = PinNotice.IsOpen || _viewModel.ShowUpdateNotice", codeBehind);
    }

    [Fact]
    public void LauncherHasDedicatedLoadingEmptyAndNoMatchStates()
    {
        var doc = AppPage();
        foreach (var binding in new[] { "{Binding ShowSplash}", "{Binding ShowEmpty}", "{Binding ShowNoMatches}" })
            Assert.Single(doc.Descendants(), e => (string?)e.Attribute("IsVisible") == binding);

        Assert.Contains(doc.Descendants(), e =>
            e.Name.LocalName == "Button" && (string?)e.Attribute("Click") == "OnClearFilterClick");
        Assert.Contains(doc.Descendants(), e =>
            e.Name.LocalName == "Button" && (string?)e.Attribute("Click") == "OnOpenSettingsClick");
    }

    [Fact]
    public void HeroUiTokensKeepTheFourDipScale()
    {
        var theme = XDocument.Load(Path.Combine(WpfRoot, "Themes", "HeroUI.axaml"));
        var values = theme.Descendants().Where(e => e.Name.LocalName == "Double")
            .ToDictionary(e => (string)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!, e => e.Value);
        Assert.Equal("4", values["Hero.Space.1"]);
        Assert.Equal("8", values["Hero.Space.2"]);
        Assert.Equal("12", values["Hero.Space.3"]);
        Assert.Equal("16", values["Hero.Space.4"]);
    }
}
