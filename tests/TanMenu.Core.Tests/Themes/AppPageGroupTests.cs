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
        Assert.Equal("{StaticResource Card.BottomPadding}", (string?)section.Attribute("Padding"));
        Assert.Equal("Stretch", (string?)section.Attribute("VerticalAlignment"));

        var header = Assert.Single(section.Descendants(), e =>
            e.Name.LocalName == "Border" &&
            (string?)e.Attribute("Background") == "{DynamicResource CardBackgroundFillColorSecondaryBrush}");
        Assert.Equal("0,0,0,1", (string?)header.Attribute("BorderThickness"));
        Assert.Equal("12,12,0,0", (string?)header.Attribute("CornerRadius"));
        Assert.Equal("{StaticResource Card.HeaderPadding}", (string?)header.Attribute("Padding"));

        var groups = doc.Descendants().Single(e =>
            e.Name.LocalName == "ItemsControl" &&
            (string?)e.Attribute("ItemsSource") == "{Binding Groups}");
        var panel = groups.Descendants().First(e =>
            e.Name.LocalName == "StackPanel" &&
            (string?)e.Attribute("Spacing") == "{StaticResource Spacing.Large}");
        Assert.Equal("Horizontal", (string?)panel.Attribute("Orientation"));
    }

    [Fact]
    public void TilesBindConfiguredColumnsIntoHorizontalGroupsOfVerticalLists()
    {
        var doc = AppPage();
        var columns = Assert.Single(doc.Descendants(), e =>
            e.Name.LocalName == "ItemsControl" &&
            (string?)e.Attribute("ItemsSource") == "{Binding Columns}");
        Assert.Equal("{StaticResource Card.BodyMargin}", (string?)columns.Attribute("Margin"));
        Assert.Equal("Left", (string?)columns.Attribute("HorizontalAlignment"));
        var columnPanel = Assert.Single(columns.Descendants(), e =>
            e.Name.LocalName == "StackPanel" &&
            (string?)e.Attribute("Orientation") == "Horizontal");
        Assert.Equal("{StaticResource Spacing.Small}", (string?)columnPanel.Attribute("Spacing"));

        var tiles = Assert.Single(columns.Descendants(), e =>
            e.Name.LocalName == "ItemsControl" &&
            (string?)e.Attribute("ItemsSource") == "{Binding Items}");
        Assert.Null(tiles.Attribute("Width"));
        var tilePanel = Assert.Single(tiles.Descendants(), e =>
            e.Name.LocalName == "StackPanel" &&
            (string?)e.Attribute("Orientation") == "Vertical");
        Assert.Equal("{StaticResource Spacing.Small}", (string?)tilePanel.Attribute("Spacing"));

        var tile = Assert.Single(tiles.Descendants(), e => e.Name.LocalName == "Button");
        Assert.Equal("launcher-tile", (string?)tile.Attribute("Classes"));
        Assert.Equal("36", (string?)tile.Attribute("MinHeight"));
        Assert.Equal("4,2", (string?)tile.Attribute("Padding"));
        Assert.Equal("5", (string?)tile.Attribute("CornerRadius"));
        Assert.Equal("{Binding Name}", (string?)tile.Attribute("ToolTip.Tip"));
        Assert.Equal("{Binding Name}", (string?)tile.Attribute("AutomationProperties.Name"));
        Assert.Null(tile.Attribute("Width"));
        Assert.Equal("Stretch", (string?)tile.Attribute("HorizontalAlignment"));
        Assert.Equal("Left", (string?)tile.Attribute("HorizontalContentAlignment"));
        var content = Assert.Single(tile.Descendants(), e => e.Name.LocalName == "Grid");
        Assert.Equal("28,Auto", (string?)content.Attribute("ColumnDefinitions"));
        Assert.Equal("{StaticResource Spacing.Small}", (string?)content.Attribute("ColumnSpacing"));
        var icon = Assert.Single(tile.Descendants(), e => e.Name.LocalName == "Image");
        Assert.Equal("22", (string?)icon.Attribute("Width"));
        Assert.Equal("22", (string?)icon.Attribute("Height"));
        var iconPlate = icon.Parent!;
        Assert.Equal("Border", iconPlate.Name.LocalName);
        Assert.Equal("28", (string?)iconPlate.Attribute("Width"));
        Assert.Equal("28", (string?)iconPlate.Attribute("Height"));
        Assert.Equal("5", (string?)iconPlate.Attribute("CornerRadius"));
        Assert.Equal("{DynamicResource ControlFillColorSecondaryBrush}",
            (string?)iconPlate.Attribute("Background"));
    }

    [Fact]
    public void TilesKeepTheDefaultThemeAndUseASubtleHoverAnimation()
    {
        var doc = AppPage();
        var selectors = doc.Descendants()
            .Where(e => e.Name.LocalName == "Style")
            .ToDictionary(e => (string)e.Attribute("Selector")!, e => e);

        var button = selectors["Button.launcher-tile"];
        Assert.DoesNotContain(button.Elements(), e =>
            e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "Theme");
        Assert.Contains(button.Descendants(), e =>
            e.Name.LocalName == "TransformOperationsTransition" &&
            (string?)e.Attribute("Property") == "RenderTransform");

        var hover = selectors["Button.launcher-tile:pointerover"];
        Assert.Contains(hover.Elements(), e =>
            e.Name.LocalName == "Setter" &&
            (string?)e.Attribute("Property") == "RenderTransform" &&
            (string?)e.Attribute("Value") == "translate(0px, -1px)");
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
        Assert.Equal("28", (string?)titleButton.Attribute("Width"));
        Assert.Equal("28", (string?)titleButton.Attribute("Height"));
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
        Assert.Equal("14", (string?)title.Attribute("FontSize"));
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
        Assert.Equal("28", (string?)iconPlate.Attribute("Width"));
        Assert.Equal("28", (string?)iconPlate.Attribute("Height"));
        Assert.Equal("7", (string?)iconPlate.Attribute("CornerRadius"));
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
        Assert.Equal("6,1", (string?)badge.Attribute("Padding"));
        Assert.Equal("9", (string?)badge.Attribute("CornerRadius"));
    }

    [Fact]
    public void PageScrollsWithoutVisibleScrollBars()
    {
        var doc = AppPage();
        var body = doc.Descendants().Single(e =>
            e.Name.LocalName == "ScrollViewer" && (string?)e.Attribute(XamlName) == "Body");
        Assert.Equal("Hidden", (string?)body.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Hidden", (string?)body.Attribute("HorizontalScrollBarVisibility"));
    }

    [Fact]
    public void PageContentUsesAConsistentSixteenDipOuterInset()
    {
        var doc = AppPage();
        var groups = Assert.Single(doc.Descendants(), e =>
            e.Name.LocalName == "ItemsControl" &&
            (string?)e.Attribute("ItemsSource") == "{Binding Groups}");
        Assert.Equal("{StaticResource Page.ContentInset}", (string?)groups.Attribute("Margin"));

        var notices = doc.Descendants().Where(e => e.Name.LocalName == "FAInfoBar").Take(2).ToList();
        Assert.Equal(2, notices.Count);
        Assert.All(notices, notice => Assert.Equal(
            "{StaticResource Page.NoticeMargin}", (string?)notice.Attribute("Margin")));
        Assert.Equal("False", (string?)notices[0].Attribute("IsVisible"));
        Assert.Equal("{Binding ShowUpdateNotice}", (string?)notices[1].Attribute("IsVisible"));
    }

    [Fact]
    public void ApplicationSpacingTokensFollowOneFourDipScale()
    {
        var app = XDocument.Load(Path.Combine(RepoRoot(), "src", "TanMenu.Wpf", "App.axaml"));
        var resources = Assert.Single(app.Root!.Elements(), e => e.Name.LocalName == "Application.Resources");
        var values = resources.Elements().ToDictionary(
            e => (string)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!,
            e => e.Value);

        Assert.Equal("4", values["Spacing.XSmall"]);
        Assert.Equal("8", values["Spacing.Small"]);
        Assert.Equal("12", values["Spacing.Medium"]);
        Assert.Equal("16", values["Spacing.Large"]);
        Assert.Equal("24", values["Spacing.XLarge"]);
        Assert.Equal("16", values["Page.ContentInset"]);
        Assert.Equal("16,16,16,0", values["Page.NoticeMargin"]);
        Assert.Equal("16,8", values["Card.HeaderPadding"]);
        Assert.Equal("16,16,16,0", values["Card.BodyMargin"]);
        Assert.Equal("0,0,0,16", values["Card.BottomPadding"]);
        Assert.Equal("24", values["Toast.Margin"]);
    }
}
