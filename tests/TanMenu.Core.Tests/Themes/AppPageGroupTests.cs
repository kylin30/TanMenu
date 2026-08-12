using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace TanMenu.Core.Tests.Themes;

/// <summary>Guards the responsive, vertically scrolling launcher layout.</summary>
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

    private static XDocument AppPage() => XDocument.Load(Path.Combine(
        RepoRoot(), "src", "TanMenu.Wpf", "Views", "AppPage.axaml"));

    [Fact]
    public void GroupsUseHorizontalThemeCardsThatCollapseWhenFilteredOut()
    {
        var doc = AppPage();
        var section = Assert.Single(doc.Descendants(), e =>
            e.Name.LocalName == "Border" &&
            (string?)e.Attribute("IsVisible") == "{Binding HasVisibleItems}");
        Assert.Null(section.Attribute("MinWidth"));
        Assert.Null(section.Attribute("Width"));
        Assert.Equal("{DynamicResource CardBackgroundFillColorDefaultBrush}",
            (string?)section.Attribute("Background"));
        Assert.Equal("{DynamicResource CardStrokeColorDefaultBrush}",
            (string?)section.Attribute("BorderBrush"));
        Assert.Equal("12", (string?)section.Attribute("CornerRadius"));
        Assert.Equal("0,0,0,12", (string?)section.Attribute("Padding"));

        var header = Assert.Single(section.Descendants(), e =>
            e.Name.LocalName == "Border" &&
            (string?)e.Attribute("Background") == "{DynamicResource CardBackgroundFillColorSecondaryBrush}");
        Assert.Equal("0,0,0,1", (string?)header.Attribute("BorderThickness"));
        Assert.Equal("12,12,0,0", (string?)header.Attribute("CornerRadius"));

        var groups = doc.Descendants().Single(e =>
            e.Name.LocalName == "ItemsControl" &&
            (string?)e.Attribute("ItemsSource") == "{Binding Groups}");
        var panel = groups.Descendants().First(e =>
            e.Name.LocalName == "StackPanel" &&
            (string?)e.Attribute("Spacing") == "16");
        Assert.Equal("Horizontal", (string?)panel.Attribute("Orientation"));
    }

    [Fact]
    public void TilesBindConfiguredColumnsIntoHorizontalGroupsOfVerticalLists()
    {
        var doc = AppPage();
        var columns = Assert.Single(doc.Descendants(), e =>
            e.Name.LocalName == "ItemsControl" &&
            (string?)e.Attribute("ItemsSource") == "{Binding Columns}");
        var columnPanel = columns.Descendants().First(e =>
            e.Name.LocalName == "StackPanel" &&
            (string?)e.Attribute("Orientation") == "Horizontal");
        Assert.Equal("8", (string?)columnPanel.Attribute("Spacing"));

        var tiles = Assert.Single(columns.Descendants(), e =>
            e.Name.LocalName == "ItemsControl" &&
            (string?)e.Attribute("ItemsSource") == "{Binding Items}");
        Assert.Null(tiles.Attribute("Width"));
        var tilePanel = Assert.Single(tiles.Descendants(), e =>
            e.Name.LocalName == "StackPanel" &&
            (string?)e.Attribute("Orientation") == "Vertical");
        Assert.Equal("6", (string?)tilePanel.Attribute("Spacing"));

        var tile = Assert.Single(tiles.Descendants(), e => e.Name.LocalName == "Button");
        Assert.Equal("{DynamicResource TransparentButton}", (string?)tile.Attribute("Theme"));
        Assert.Equal("48", (string?)tile.Attribute("MinHeight"));
        Assert.Equal("8,6", (string?)tile.Attribute("Padding"));
        Assert.Equal("{Binding Name}", (string?)tile.Attribute("ToolTip.Tip"));
        Assert.Equal("{Binding Name}", (string?)tile.Attribute("AutomationProperties.Name"));
        Assert.Null(tile.Attribute("Width"));
        Assert.Equal("Stretch", (string?)tile.Attribute("HorizontalAlignment"));
        Assert.Equal("Left", (string?)tile.Attribute("HorizontalContentAlignment"));
        var content = Assert.Single(tile.Descendants(), e => e.Name.LocalName == "Grid");
        Assert.Equal("36,Auto", (string?)content.Attribute("ColumnDefinitions"));
        var icon = Assert.Single(tile.Descendants(), e => e.Name.LocalName == "Image");
        Assert.Equal("28", (string?)icon.Attribute("Width"));
        Assert.Equal("28", (string?)icon.Attribute("Height"));
        var iconPlate = icon.Parent!;
        Assert.Equal("Border", iconPlate.Name.LocalName);
        Assert.Equal("36", (string?)iconPlate.Attribute("Width"));
        Assert.Equal("36", (string?)iconPlate.Attribute("Height"));
        Assert.Equal("{DynamicResource ControlFillColorSecondaryBrush}",
            (string?)iconPlate.Attribute("Background"));
    }

    [Fact]
    public void ClickableGroupTitlesUseAnAccessibleButton()
    {
        var doc = AppPage();
        var titleButton = Assert.Single(doc.Descendants(), e =>
            e.Name.LocalName == "Button" &&
            (string?)e.Attribute("Click") == "OnGroupTitleClick");

        Assert.Equal("{Binding IsDirectoryClickable}", (string?)titleButton.Attribute("IsVisible"));
        Assert.Equal("{Binding TitleTooltip}", (string?)titleButton.Attribute("AutomationProperties.Name"));
        Assert.Equal("32", (string?)titleButton.Attribute("Width"));
        Assert.Equal("32", (string?)titleButton.Attribute("Height"));
        Assert.Contains(titleButton.Descendants(), e =>
            e.Name.LocalName == "FASymbolIcon" && (string?)e.Attribute("Symbol") == "ChevronRight");
        Assert.DoesNotContain(titleButton.Descendants(), e =>
            e.Name.LocalName == "FASymbolIcon" && (string?)e.Attribute("Symbol") == "OpenFolder");
        Assert.DoesNotContain(doc.Descendants(), e =>
            (string?)e.Attribute("PointerPressed") == "OnGroupTitlePressed");
    }

    [Fact]
    public void GroupTitlesUseCompactHeadingSize()
    {
        var doc = AppPage();
        var titles = doc.Descendants().Where(e =>
            e.Name.LocalName == "TextBlock" &&
            (string?)e.Attribute("Text") == "{Binding DirectoryName}");

        var title = Assert.Single(titles);
        Assert.Equal("16", (string?)title.Attribute("FontSize"));
        Assert.Equal("Left", (string?)title.Attribute("HorizontalAlignment"));
    }

    [Fact]
    public void GroupHeadersUseThemeTokenIconPlatesForFolderAndBuiltInGroups()
    {
        var doc = AppPage();
        var folder = Assert.Single(doc.Descendants(), e =>
            e.Name.LocalName == "FASymbolIcon" && (string?)e.Attribute("Symbol") == "Folder");
        var allApps = Assert.Single(doc.Descendants(), e =>
            e.Name.LocalName == "FASymbolIcon" && (string?)e.Attribute("Symbol") == "AllApps");
        Assert.Equal("{Binding IsDirectoryClickable}", (string?)folder.Attribute("IsVisible"));
        Assert.Equal("{Binding !IsDirectoryClickable}", (string?)allApps.Attribute("IsVisible"));
        var iconPlate = folder.Parent!.Parent!;
        Assert.Equal("Border", iconPlate.Name.LocalName);
        Assert.Equal("{DynamicResource ControlFillColorSecondaryBrush}",
            (string?)iconPlate.Attribute("Background"));
        Assert.Equal("8", (string?)iconPlate.Attribute("CornerRadius"));
    }

    [Fact]
    public void GroupHeaderShowsTheFilteredItemCountAsAThemeTokenBadge()
    {
        var doc = AppPage();
        var count = Assert.Single(doc.Descendants(), e =>
            e.Name.LocalName == "TextBlock" &&
            (string?)e.Attribute("Text") == "{Binding VisibleItemCount}");
        Assert.Equal("12", (string?)count.Attribute("FontSize"));
        var badge = count.Parent!;
        Assert.Equal("Border", badge.Name.LocalName);
        Assert.Equal("{DynamicResource ControlFillColorSecondaryBrush}",
            (string?)badge.Attribute("Background"));
        Assert.Equal("10", (string?)badge.Attribute("CornerRadius"));
    }

    [Fact]
    public void PageScrollsAcrossHorizontalGroupsAndLongVerticalLists()
    {
        var doc = AppPage();
        var body = doc.Descendants().Single(e =>
            e.Name.LocalName == "ScrollViewer" && (string?)e.Attribute(XamlName) == "Body");
        Assert.Equal("Auto", (string?)body.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Auto", (string?)body.Attribute("HorizontalScrollBarVisibility"));
    }
}
