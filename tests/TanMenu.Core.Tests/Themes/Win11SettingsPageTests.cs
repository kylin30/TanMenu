using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Themes;

public class Win11SettingsPageTests
{
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
    public void MainWindowIsAnAppWindowWithBuiltInSettingsNavigation()
    {
        var main = XDocument.Load(Path.Combine(WpfRoot, "MainWindow.axaml"));
        Assert.Equal("FAAppWindow", main.Root!.Name.LocalName);
        var nav = Assert.Single(main.Descendants(), e => e.Name.LocalName == "FANavigationView");
        Assert.Equal("False", (string?)nav.Attribute("IsSettingsVisible"));
        Assert.Contains(main.Descendants(), e => e.Name.LocalName == "SettingsPage");
    }

    [Fact]
    public void MainWindowUsesTheIntegratedTitleBarAndMicaBackdrop()
    {
        var main = XDocument.Load(Path.Combine(WpfRoot, "MainWindow.axaml"));
        Assert.Contains(main.Descendants(), e =>
            (string?)e.Attribute("WindowDecorationProperties.ElementRole") == "TitleBar");

        var nav = main.Descendants().Single(e => e.Name.LocalName == "FANavigationView");
        Assert.Equal("1", (string?)nav.Attribute("Grid.Row"));
        Assert.Null(nav.Attribute("Grid.RowSpan"));
        Assert.Null(nav.Attribute("WindowDecorationProperties.ElementRole"));
        Assert.Equal("OnNavigationSelectionChanged", (string?)nav.Attribute("SelectionChanged"));

        var titleBar = main.Descendants().Single(e =>
            (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "TitleBarHost");
        Assert.Equal("48", (string?)titleBar.Attribute("Height"));

        var shellGrid = Assert.Single(main.Root!.Elements(), e => e.Name.LocalName == "Grid");
        Assert.Equal("48,*", (string?)shellGrid.Attribute("RowDefinitions"));

        var code = File.ReadAllText(Path.Combine(WpfRoot, "MainWindow.axaml.cs"));
        Assert.Contains("TitleBar.ExtendsContentIntoTitleBar = true", code);
        Assert.Contains("WindowTransparencyLevel.Mica", code);

        var host = File.ReadAllText(Path.Combine(WpfRoot, "Services", "WindowHost.cs"));
        Assert.Contains("TitleBarHeightDip = 48", host);
    }

    [Fact]
    public void MainContentExposesTheMicaBackdrop()
    {
        var main = XDocument.Load(Path.Combine(WpfRoot, "MainWindow.axaml"));
        var navigation = main.Descendants().Single(e => e.Name.LocalName == "FANavigationView");
        var resources = Assert.Single(navigation.Elements(), e =>
            e.Name.LocalName == "FANavigationView.Resources");
        var contentBackground = Assert.Single(resources.Elements(), e =>
            (string?)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) ==
            "NavigationViewContentBackground");

        Assert.Equal("SolidColorBrush", contentBackground.Name.LocalName);
        Assert.Equal("Transparent", (string?)contentBackground.Attribute("Color"));
    }

    [Fact]
    public void SettingsUseOneBoundedSingleColumnPage()
    {
        var page = XDocument.Load(Path.Combine(WpfRoot, "Views", "SettingsPage.axaml"));
        Assert.DoesNotContain(page.Descendants(), e => (string?)e.Attribute(
            XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "PageTitle");
        Assert.DoesNotContain(page.Descendants(), e => e.Name.LocalName == "FATabView");

        var scroll = Assert.Single(page.Descendants(), e => e.Name.LocalName == "ScrollViewer");
        Assert.Equal("680", (string?)scroll.Attribute("MinWidth"));
        Assert.Equal("820", (string?)scroll.Attribute("MaxWidth"));
        Assert.Equal("700", (string?)scroll.Attribute("MaxHeight"));
        Assert.Equal("Hidden", (string?)scroll.Attribute("HorizontalScrollBarVisibility"));
        Assert.Equal("Hidden", (string?)scroll.Attribute("VerticalScrollBarVisibility"));

        Assert.Equal("SettingsScroll", (string?)scroll.Attribute(
            XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")));
        var viewportGrid = Assert.Single(scroll.Elements(), e => e.Name.LocalName == "Grid");
        Assert.Equal("{Binding #SettingsScroll.Viewport.Width}", (string?)viewportGrid.Attribute("Width"));
        Assert.Equal("*,16", (string?)viewportGrid.Attribute("ColumnDefinitions"));

        var column = Assert.Single(viewportGrid.Elements(), e => e.Name.LocalName == "StackPanel");
        Assert.Equal("0", (string?)column.Attribute("Grid.Column"));
        Assert.Equal("Stretch", (string?)column.Attribute("HorizontalAlignment"));
        Assert.Equal("20", (string?)column.Attribute("Spacing"));

        var sections = column.Elements().Where(e => e.Name.LocalName == "StackPanel").ToList();
        Assert.Equal(6, sections.Count);
        Assert.All(sections, section => Assert.Equal("Stretch", (string?)section.Attribute("HorizontalAlignment")));
        Assert.All(sections, section =>
        {
            var card = Assert.Single(section.Elements(), e => e.Name.LocalName.EndsWith("Card", StringComparison.Ordinal));
            Assert.Equal("Stretch", (string?)card.Attribute("HorizontalAlignment"));
        });

        var headings = page.Descendants().Where(e =>
            e.Name.LocalName == "TextBlock" &&
            ((string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")))?.EndsWith("Heading") == true).ToList();
        Assert.Equal(6, headings.Count);
        Assert.All(headings, heading => Assert.Equal("18", (string?)heading.Attribute("FontSize")));
        Assert.Equal(6, page.Descendants().Count(e => e.Name.LocalName.EndsWith("Card", StringComparison.Ordinal)));
    }

    [Fact]
    public void LanguageChoicesUseTheirOwnLanguage()
    {
        Assert.Equal("中文（简体）", AppLanguage.LanguageLabel(AppLanguage.ZhHans, AppLanguage.EnUs));
        Assert.Equal("English (United States)",
            AppLanguage.LanguageLabel(AppLanguage.EnUs, AppLanguage.ZhHans));
    }

    [Fact]
    public void AlibabaFontsAreEmbeddedAndRegisteredBeforeUiStartup()
    {
        var fontRoot = Path.Combine(WpfRoot, "wwwroot", "fonts");
        Assert.True(File.Exists(Path.Combine(fontRoot, "AlibabaPuHuiTi2-Regular.ttf")));
        Assert.True(File.Exists(Path.Combine(fontRoot, "AlibabaPuHuiTi2-Medium.ttf")));
        Assert.True(File.Exists(Path.Combine(fontRoot, "AlibabaPuHuiTi2-SemiBold.ttf")));
        Assert.True(File.Exists(Path.Combine(fontRoot, "AlibabaPuHuiTi2-Bold.ttf")));
        Assert.True(File.Exists(Path.Combine(fontRoot, "NOTICE.txt")));

        var project = File.ReadAllText(Path.Combine(WpfRoot, "TanMenu.Wpf.csproj"));
        Assert.Contains("AlibabaPuHuiTi2-Regular.ttf", project);
        Assert.Contains("AlibabaPuHuiTi2-Medium.ttf", project);
        Assert.Contains("AlibabaPuHuiTi2-SemiBold.ttf", project);
        Assert.Contains("AlibabaPuHuiTi2-Bold.ttf", project);
        Assert.Contains("FONT-NOTICE.txt", project);

        var program = File.ReadAllText(Path.Combine(WpfRoot, "Program.cs"));
        Assert.Contains(".WithBundledFonts()", program);
    }

    [Fact]
    public void MainWindowRefitsWhenItsActiveContentChanges()
    {
        var code = File.ReadAllText(Path.Combine(WpfRoot, "MainWindow.axaml.cs"));
        Assert.Contains("ContentMetricsChanged += QueueRefitToContent", code);
        Assert.Contains("NoticeVisibilityChanged += QueueRefitToContent", code);
        Assert.Contains("AppNavigation.DisplayModeChanged += OnNavigationDisplayModeChanged", code);
        Assert.Contains("AppNavigation.PaneOpened += OnNavigationPaneLayoutChanged", code);
        Assert.Contains("AppNavigation.PaneClosed += OnNavigationPaneLayoutChanged", code);
        Assert.Contains("AppNavigation.OpenPaneLength", code);
        Assert.Contains("AppNavigation.CompactPaneLength", code);
        Assert.Contains("NavigationContentDividerWidthDip = 1", code);
        Assert.Contains("+ NavigationContentDividerWidthDip", code);
        Assert.Contains("QueuePageRefitToContent();", code);
        Assert.Contains("RefitToContent(forcePageFit: true)", code);
        Assert.Contains("host.FitToContent(activePage, NavigationChromeWidthDip, forcePageFit)", code);

        var hostCode = File.ReadAllText(Path.Combine(WpfRoot, "Services", "WindowHost.cs"));
        Assert.Contains("(_userResized && !forcePageFit)", hostCode);
        Assert.Contains("if (forcePageFit)\n            _userResized = false", hostCode.Replace("\r\n", "\n"));
        Assert.Contains("if (!forcePageFit && !_fitBasisStale", hostCode);
        Assert.Contains("pageContent.Measure(new Size(availableContentWidth, double.PositiveInfinity))", hostCode);
        Assert.Contains("FitRoundingSlackPixels / scale", hostCode);
    }

    [Fact]
    public void SettingsUseLibraryCardsAndHaveNoConfirmationDialog()
    {
        var cardFiles = Directory.GetFiles(Path.Combine(WpfRoot, "Views", "Settings"), "*Card.axaml");
        Assert.NotEmpty(cardFiles);
        Assert.All(cardFiles, file =>
        {
            var doc = XDocument.Load(file);
            Assert.Equal("Stretch", (string?)doc.Root!.Attribute("HorizontalContentAlignment"));
            Assert.Contains(doc.Descendants(), e => e.Name.LocalName == "FASettingsExpander");
        });

        Assert.False(File.Exists(Path.Combine(WpfRoot, "Dialogs", "OptionsWindow.axaml")));
        Assert.False(File.Exists(Path.Combine(WpfRoot, "Dialogs", "AboutWindow.axaml")));
        var pageCode = File.ReadAllText(Path.Combine(WpfRoot, "Views", "SettingsPage.axaml.cs"));
        Assert.Contains("TimeSpan.FromMilliseconds(300)", pageCode);
        Assert.DoesNotContain("OkButton", pageCode);
        Assert.DoesNotContain("CancelButton", pageCode);
        Assert.DoesNotContain("ApplyButton", pageCode);
    }

    [Fact]
    public void SettingsCardsUseLibraryIconsAndHelpfulDescriptions()
    {
        var cardFiles = Directory.GetFiles(Path.Combine(WpfRoot, "Views", "Settings"), "*Card.axaml");
        var cards = cardFiles.SelectMany(file => XDocument.Load(file).Descendants())
            .Where(e => e.Name.LocalName == "FASettingsExpander")
            .ToList();

        Assert.NotEmpty(cards);
        Assert.All(cards, card => Assert.False(string.IsNullOrWhiteSpace((string?)card.Attribute("IconSource"))));

        var appearanceCode = File.ReadAllText(Path.Combine(WpfRoot, "Views", "Settings", "AppearanceCard.axaml.cs"));
        var behaviorCode = File.ReadAllText(Path.Combine(WpfRoot, "Views", "Settings", "BehaviorCard.axaml.cs"));
        var foldersXaml = XDocument.Load(Path.Combine(WpfRoot, "Views", "Settings", "FoldersCard.axaml"));
        var foldersCode = File.ReadAllText(Path.Combine(WpfRoot, "Views", "Settings", "FoldersCard.axaml.cs"));
        Assert.Contains("LanguageRow.Description", appearanceCode);
        Assert.Contains("ColumnCountRow.Description", appearanceCode);
        Assert.Contains("_draft.Working.General.ColButtonCount", appearanceCode);
        Assert.Contains("AutoCloseRow.Description", behaviorCode);
        Assert.Contains("ButtonSoundsRow.Description", behaviorCode);
        Assert.Contains("HoverSoundItem.Description", behaviorCode);
        Assert.Contains("ClickSoundItem.Description", behaviorCode);
        Assert.Contains("HoverSoundEnabled = HoverSoundToggle.IsChecked == true", behaviorCode);
        Assert.Contains("ClickSoundEnabled = ClickSoundToggle.IsChecked == true", behaviorCode);
        Assert.DoesNotContain(foldersXaml.Descendants(), e =>
            (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) is
                "RootPathText" or "DataPathText");
        Assert.Contains("MainFolderRow.Description", foldersCode);
        Assert.Contains("DataFolderRow.Description", foldersCode);
    }

    [Fact]
    public void LauncherSoundPlaybackHonorsBothSoundSwitches()
    {
        var code = File.ReadAllText(Path.Combine(WpfRoot, "ViewModels", "LauncherViewModel.cs"));
        Assert.Contains("if (_cfg.General.ClickSoundEnabled)", code);
        Assert.Contains("if (_cfg.General.HoverSoundEnabled)", code);
        Assert.Contains("_sounds.PlayClickSoundAsync()", code);
        Assert.Contains("_sounds.PlayHoverSoundAsync()", code);
    }

    [Fact]
    public void SoundSwitchesUseTheCommonToolsNestedItemStyle()
    {
        var behavior = XDocument.Load(Path.Combine(WpfRoot, "Views", "Settings", "BehaviorCard.axaml"));
        var soundGroup = behavior.Descendants().Single(e =>
            (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) ==
            "ButtonSoundsRow");

        Assert.Equal("FASettingsExpander", soundGroup.Name.LocalName);
        Assert.Equal("True", (string?)soundGroup.Attribute("IsExpanded"));
        Assert.Equal("Volume", (string?)soundGroup.Attribute("IconSource"));

        var soundItems = soundGroup.Descendants().Where(e => e.Name.LocalName == "FASettingsExpanderItem").ToList();
        Assert.Equal(2, soundItems.Count);
        Assert.Contains(soundItems, item =>
            (string?)item.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) ==
            "HoverSoundItem");
        Assert.Contains(soundItems, item =>
            (string?)item.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) ==
            "ClickSoundItem");
    }
}
