using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;
using Xunit;

namespace TanMenu.Core.Tests.Themes;

public class Win11SettingsPageTests
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
    public void MainWindowIsAnAppWindowWithHeroUiAppLayout()
    {
        var main = XDocument.Load(Path.Combine(WpfRoot, "MainWindow.axaml"));
        Assert.Equal("FAAppWindow", main.Root!.Name.LocalName);
        Assert.DoesNotContain(main.Descendants(), e => e.Name.LocalName == "FANavigationView");
        Assert.Contains(main.Descendants(), e => (string?)e.Attribute("Classes") == "hero-sidebar-panel");
        var content = Assert.Single(main.Descendants(), e =>
            (string?)e.Attribute(XamlName) == "ContentHost");
        Assert.Equal("Grid", content.Name.LocalName);
        Assert.Null((string?)content.Attribute("Classes"));
        Assert.Contains(main.Descendants(), e => e.Name.LocalName == "SettingsPage");
    }

    [Fact]
    public void SecondInstanceReturnsTheExistingWindowToAppsAndGrantsForegroundPermission()
    {
        var events = new AppEvents();
        var requested = false;
        events.LauncherPageRequested += () => requested = true;
        events.RaiseLauncherPageRequested();
        Assert.True(requested);

        var program = File.ReadAllText(Path.Combine(WpfRoot, "Program.cs"));
        Assert.Contains("AllowSetForegroundWindow(AsfwAny)", program);
        Assert.True(program.IndexOf("AllowSetForegroundWindow(AsfwAny)", StringComparison.Ordinal) <
                    program.IndexOf("activationEvent.Set()", StringComparison.Ordinal));
        Assert.Contains("EventResetMode.AutoReset", program);

        var app = File.ReadAllText(Path.Combine(WpfRoot, "App.axaml.cs"));
        Assert.Contains("StartSingleInstanceActivationListener()", app);
        Assert.True(app.IndexOf("RaiseLauncherPageRequested()", StringComparison.Ordinal) <
                    app.IndexOf("ShowAndActivate()", StringComparison.Ordinal));

        var window = File.ReadAllText(Path.Combine(WpfRoot, "MainWindow.axaml.cs"));
        Assert.Contains("_events.LauncherPageRequested += ShowLauncherPage", window);
        Assert.Contains("_events.LauncherPageRequested -= ShowLauncherPage", window);
    }

    [Fact]
    public void MainWindowUsesRightSideTitleBarAndSolidWindowBackground()
    {
        var main = XDocument.Load(Path.Combine(WpfRoot, "MainWindow.axaml"));
        var titleBar = main.Descendants().Single(e => (string?)e.Attribute(XamlName) == "TitleBarHost");
        Assert.Equal("60", (string?)titleBar.Attribute("Height"));
        Assert.Equal("TitleBar", (string?)titleBar.Attribute("WindowDecorationProperties.ElementRole"));

        var shellGrid = Assert.Single(main.Root!.Elements(), e => e.Name.LocalName == "Grid");
        Assert.Equal("64,*", (string?)shellGrid.Attribute("ColumnDefinitions"));
        Assert.Equal("#F5F5F5", (string?)shellGrid.Attribute("Background"));
        var code = File.ReadAllText(Path.Combine(WpfRoot, "MainWindow.axaml.cs"));
        Assert.Contains("TitleBar.ExtendsContentIntoTitleBar = true", code);
        Assert.Contains("TitleBar.Height = 60", code);
        Assert.Contains("Color.FromRgb(0xF5, 0xF5, 0xF5)", code);
        Assert.DoesNotContain("WindowTransparencyLevel.Mica", code);
    }

    [Fact]
    public void LauncherFitsAllApplicationColumnsWhilePageSwitchingDoesNotFitSettings()
    {
        var main = XDocument.Load(Path.Combine(WpfRoot, "MainWindow.axaml")).Root!;
        Assert.Equal("1180", (string?)main.Attribute("Width"));
        Assert.Equal("760", (string?)main.Attribute("Height"));
        Assert.Equal("840", (string?)main.Attribute("MinWidth"));
        Assert.Equal("600", (string?)main.Attribute("MinHeight"));

        var code = File.ReadAllText(Path.Combine(WpfRoot, "MainWindow.axaml.cs"));
        Assert.Contains("QueueLauncherFit", code);
        Assert.Contains("RefitLauncherToContent", code);
        Assert.Contains("ContentMetricsChanged +=", code);
        Assert.Contains("NoticeVisibilityChanged +=", code);
        Assert.Contains("_host.FitToContent", code);
        Assert.Contains("await LauncherContent.BeginInitialize()", code);
        var showSettings = code[(code.IndexOf("public void ShowSettingsPage()", StringComparison.Ordinal))..];
        showSettings = showSettings[..showSettings.IndexOf("public void ShowLauncherPage()", StringComparison.Ordinal)];
        Assert.DoesNotContain("FitToContent", showSettings);
        Assert.Contains("Grid.SetRow(ContentHost, 0)", showSettings);
        Assert.Contains("Grid.SetRowSpan(ContentHost, 2)", showSettings);
        Assert.Contains("TitleBarHost.Background = Brushes.Transparent", showSettings);

        var showLauncher = code[(code.IndexOf("public void ShowLauncherPage()", StringComparison.Ordinal))..];
        Assert.Contains("Grid.SetRow(ContentHost, 1)", showLauncher);
        Assert.Contains("Grid.SetRowSpan(ContentHost, 1)", showLauncher);
        Assert.Contains("TitleBarHost.Background = WindowBackgroundBrush", showLauncher);
    }

    [Fact]
    public void SettingsUseSectionNavigationAndOneContinuousContentRegion()
    {
        var page = XDocument.Load(Path.Combine(WpfRoot, "Views", "SettingsPage.axaml"));
        var rootGrid = Assert.Single(page.Root!.Elements(), e => e.Name.LocalName == "Grid");
        Assert.Equal("184,1,*", (string?)rootGrid.Attribute("ColumnDefinitions"));
        Assert.Equal("24", (string?)rootGrid.Attribute("Margin"));
        Assert.Equal("24", (string?)rootGrid.Attribute("ColumnSpacing"));

        var nav = Assert.Single(page.Descendants(), e =>
            (string?)e.Attribute("Classes") == "hero-settings-section-nav");
        var navButtons = nav.Descendants().Where(e =>
            e.Name.LocalName == "Button" && ((string?)e.Attribute("Classes"))?.Contains("hero-settings-nav-item") == true).ToList();
        Assert.Equal(6, navButtons.Count);
        Assert.Equal(Enumerable.Range(0, 6).Select(i => i.ToString()),
            navButtons.Select(e => (string?)e.Attribute("Tag")));

        var scroll = Assert.Single(page.Descendants(), e => (string?)e.Attribute(XamlName) == "SettingsScroll");
        Assert.Equal("Hidden", (string?)scroll.Attribute("HorizontalScrollBarVisibility"));
        Assert.Equal("Hidden", (string?)scroll.Attribute("VerticalScrollBarVisibility"));
        var contentColumn = Assert.Single(scroll.Elements(), e => e.Name.LocalName == "StackPanel");
        Assert.Equal("{Binding #SettingsScroll.Viewport.Width}", (string?)contentColumn.Attribute("Width"));
        Assert.Equal("1000", (string?)contentColumn.Attribute("MaxWidth"));
        Assert.Equal(6, page.Descendants().Count(e => e.Name.LocalName.EndsWith("Card", StringComparison.Ordinal)));
        Assert.Equal(6, page.Descendants().Count(e =>
            ((string?)e.Attribute(XamlName))?.EndsWith("Section", StringComparison.Ordinal) == true));
        Assert.DoesNotContain(page.Descendants(), e =>
            e.Name.LocalName.EndsWith("Card", StringComparison.Ordinal) &&
            string.Equals((string?)e.Attribute("IsVisible"), "False", StringComparison.OrdinalIgnoreCase));

        var code = File.ReadAllText(Path.Combine(WpfRoot, "Views", "SettingsPage.axaml.cs"));
        Assert.Contains("SelectSection(_selectedSection, scrollToSection: false)", code);
        Assert.Contains("sections[_selectedSection].BringIntoView", code);
        Assert.DoesNotContain(".IsVisible = selected", code);
        Assert.Contains("buttons[index].Classes.Set(\"is-active\", index == _selectedSection)", code);
    }

    [Fact]
    public void SettingsCardsUseHeroUiRowsWithoutFluentSettingsExpanders()
    {
        var cardFiles = Directory.GetFiles(Path.Combine(WpfRoot, "Views", "Settings"), "*Card.axaml");
        Assert.NotEmpty(cardFiles);
        Assert.All(cardFiles, file =>
        {
            var doc = XDocument.Load(file);
            Assert.Equal("Stretch", (string?)doc.Root!.Attribute("HorizontalContentAlignment"));
            Assert.DoesNotContain(doc.Descendants(), e => e.Name.LocalName is "FASettingsExpander" or "FASettingsExpanderItem");
            Assert.Contains(doc.Descendants(), e =>
                ((string?)e.Attribute("Classes"))?.Contains("hero-setting-card", StringComparison.Ordinal) == true ||
                ((string?)e.Attribute("Classes"))?.Contains("hero-setting-group", StringComparison.Ordinal) == true);
            Assert.All(doc.Descendants().Where(e => e.Name.LocalName == "Button"), button =>
                Assert.Contains("hero-button", (string?)button.Attribute("Classes")));
            Assert.All(doc.Descendants().Where(e => e.Name.LocalName == "ToggleSwitch"), toggle =>
                Assert.Equal("hero-switch", (string?)toggle.Attribute("Classes")));
            Assert.All(doc.Descendants().Where(e => e.Name.LocalName is "ComboBox" or "TextBox"), field =>
                Assert.Equal("hero-field", (string?)field.Attribute("Classes")));
        });

        var pageCode = File.ReadAllText(Path.Combine(WpfRoot, "Views", "SettingsPage.axaml.cs"));
        Assert.Contains("TimeSpan.FromMilliseconds(300)", pageCode);
        Assert.DoesNotContain("OkButton", pageCode);
        Assert.DoesNotContain("CancelButton", pageCode);
        Assert.DoesNotContain("ApplyButton", pageCode);
    }

    [Fact]
    public void LauncherUsesAutomaticColumnsWithoutExposingAManualColumnSetting()
    {
        var appearanceXaml = File.ReadAllText(Path.Combine(WpfRoot, "Views", "Settings", "AppearanceCard.axaml"));
        var appearanceCode = File.ReadAllText(Path.Combine(WpfRoot, "Views", "Settings", "AppearanceCard.axaml.cs"));
        Assert.DoesNotContain("ColumnCount", appearanceXaml);
        Assert.DoesNotContain("ColButtonCount", appearanceCode);
        Assert.DoesNotContain("NumericUpDown", appearanceXaml);

        var appPage = File.ReadAllText(Path.Combine(WpfRoot, "Views", "AppPage.axaml"));
        Assert.Contains("ItemsSource=\"{Binding Columns}\"", appPage);
        Assert.Contains("ItemsSource=\"{Binding Items}\"", appPage);
        Assert.DoesNotContain("<WrapPanel", appPage);
    }

    [Fact]
    public void SettingsCardsRetainDescriptionsAndInteractiveBehavior()
    {
        var appearance = File.ReadAllText(Path.Combine(WpfRoot, "Views", "Settings", "AppearanceCard.axaml.cs"));
        var behavior = File.ReadAllText(Path.Combine(WpfRoot, "Views", "Settings", "BehaviorCard.axaml.cs"));
        var folders = File.ReadAllText(Path.Combine(WpfRoot, "Views", "Settings", "FoldersCard.axaml.cs"));
        Assert.Contains("LanguageDescription.Text", appearance);
        Assert.Contains("AppearanceDescription.Text", appearance);
        Assert.Contains("AutoCloseDescription.Text", behavior);
        Assert.Contains("ButtonSoundsDescription.Text", behavior);
        Assert.Contains("HoverSoundEnabled = HoverSoundToggle.IsChecked == true", behavior);
        Assert.Contains("ClickSoundEnabled = ClickSoundToggle.IsChecked == true", behavior);
        Assert.Contains("MainFolderDescription.Text", folders);
        Assert.Contains("DataFolderDescription.Text", folders);
    }

    [Fact]
    public void SoundPreferencesAreGroupedAsHeroUiSubrows()
    {
        var behavior = XDocument.Load(Path.Combine(WpfRoot, "Views", "Settings", "BehaviorCard.axaml"));
        var soundGroup = Assert.Single(behavior.Descendants(), e =>
            (string?)e.Attribute("Classes") == "hero-setting-group");
        Assert.Contains(soundGroup.Descendants(), e =>
            e.Name.LocalName == "FASymbolIcon" && (string?)e.Attribute("Symbol") == "Volume");
        Assert.Equal(2, soundGroup.Descendants().Count(e =>
            (string?)e.Attribute("Classes") == "hero-setting-subrow"));
        Assert.Contains(soundGroup.Descendants(), e => (string?)e.Attribute(XamlName) == "HoverSoundToggle");
        Assert.Contains(soundGroup.Descendants(), e => (string?)e.Attribute(XamlName) == "ClickSoundToggle");
    }

    [Fact]
    public void LanguageChoicesUseTheirOwnLanguage()
    {
        Assert.Equal("中文（简体）", AppLanguage.LanguageLabel(AppLanguage.ZhHans, AppLanguage.EnUs));
        Assert.Equal("English (United States)", AppLanguage.LanguageLabel(AppLanguage.EnUs, AppLanguage.ZhHans));
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

        var program = File.ReadAllText(Path.Combine(WpfRoot, "Program.cs"));
        Assert.Contains(".WithBundledFonts()", program);
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
}
