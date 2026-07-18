using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Models;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf;

public partial class SettingsWindow : Window
{
    private static readonly (string Key, string Label)[] BuiltInThemes =
    {
        ("WinXP", "Windows XP"),
        ("Win7", "Windows 7"),
        ("Windows11", "Windows 11"),
        ("Pixel", "Windows 2000"),
    };

    // Built-in themes + any user .css themes discovered under <data>\themes (populated in the ctor).
    private (string Key, string Label)[] _themes = BuiltInThemes;

    private static readonly string[] ButtonSizes = { "Small", "Medium", "Large" };
    private static readonly string[] LanguageSettings = { AppLanguage.Auto, AppLanguage.ZhHans, AppLanguage.EnUs };

    /// <summary>One entry in the font dropdown. <see cref="Family"/> is the CSS value ("" = the
    /// theme's own font); <see cref="Group"/> drives the 内置字体 / 系统字体 grouping.</summary>
    private sealed record FontItem(string Name, string Family, string Group);

    /// <summary>App-bundled fonts first (default sentinel + the three @font-face families), then
    /// every installed system font family, sorted.</summary>
    private static List<FontItem> BuildFontItems(string language)
    {
        var groupBuiltIn = AppLanguage.Text("BuiltInFonts", language);
        var groupSystem = AppLanguage.Text("SystemFonts", language);
        var items = new List<FontItem>
        {
            new(AppLanguage.Text("DefaultFont", language), "", groupBuiltIn),
            new("阿里巴巴普惠体", "Alibaba PuHuiTi", groupBuiltIn),
            new("Fusion Pixel 像素", "Pixel", groupBuiltIn),
            new("Press Start 2P", "Press Start 2P", groupBuiltIn),
        };
        foreach (var fam in System.Windows.Media.Fonts.SystemFontFamilies
                     .Select(f => f.Source)
                     .Where(s => !string.IsNullOrWhiteSpace(s))
                     .Distinct()
                     .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
        {
            items.Add(new FontItem(fam, fam, groupSystem));
        }
        return items;
    }

    private readonly ConfigService _config;
    private readonly IAutoStartService _autoStart;
    private readonly AppEvents _events;
    private readonly ILaunchService _launch;
    private readonly IShortcutResolver _resolver;
    private readonly MenuService _menu;
    private readonly IAppDataPaths _paths;
    private readonly ThemeService _themeService;
    private readonly IAppUpdateService _updates;
    private readonly TaskbarPinService _taskbarPin;
    private readonly bool _isPackaged;
    private readonly bool _isPortable;

    // Edit-then-apply: all changes go to this working copy; nothing takes effect until 应用/确定.
    private AppConfig _working;
    private bool _pendingAutoStart;
    private bool _dirty;
    private bool _loaded;
    private TaskbarPinState _taskbarPinState = new(TaskbarPinStatus.Available);

    private string UiLanguage => _working?.General.Language ?? _config.Config.General.Language;
    private string L(string key, params object?[] args) => AppLanguage.Format(key, UiLanguage, args);

    public SettingsWindow()
    {
        InitializeComponent();
        _config = App.Services.GetRequiredService<ConfigService>();
        _autoStart = App.Services.GetRequiredService<IAutoStartService>();
        _events = App.Services.GetRequiredService<AppEvents>();
        _launch = App.Services.GetRequiredService<ILaunchService>();
        _resolver = App.Services.GetRequiredService<IShortcutResolver>();
        _menu = App.Services.GetRequiredService<MenuService>();
        _paths = App.Services.GetRequiredService<IAppDataPaths>();
        _themeService = App.Services.GetRequiredService<ThemeService>();
        _updates = App.Services.GetRequiredService<IAppUpdateService>();
        _taskbarPin = App.Services.GetRequiredService<TaskbarPinService>();
        _isPackaged = PackageRuntime.HasPackageIdentity;
        _isPortable = App.IsPortable;
        _themeService.EnsureSeeded();
        _themes = BuiltInThemes.Concat(_themeService.List()).ToArray();
        _working = _config.CloneConfig();

        try
        {
            var ico = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(ico))
                Icon = BitmapFrame.Create(new Uri(ico));
        }
        catch { /* icon optional */ }

        LoadFromConfig();

        _updates.StateChanged += Updates_StateChanged;
        Closed += (_, _) => _updates.StateChanged -= Updates_StateChanged;

        // Position above the launcher once measured, then reveal — so the bottom-center, top-most
        // launcher never covers the settings content.
        Loaded += async (_, _) =>
        {
            PositionAboveLauncher();
            await RefreshTaskbarPinStateAsync();
        };
    }

    private void PositionAboveLauncher()
    {
        try
        {
            var wa = SystemParameters.WorkArea;
            double w = ActualWidth, h = ActualHeight;
            var launcher = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();

            double left, top;
            if (launcher is { ActualWidth: > 0 } && launcher.IsVisible)
            {
                left = launcher.Left + (launcher.ActualWidth - w) / 2; // centered over the launcher
                top = launcher.Top - h - 12;                           // directly above it
            }
            else
            {
                left = wa.Left + (wa.Width - w) / 2;                   // fallback: upper-center
                top = wa.Top + 40;
            }

            Left = Math.Max(wa.Left, Math.Min(left, wa.Right - w));
            Top = Math.Max(wa.Top, Math.Min(top, wa.Bottom - h));
        }
        catch { /* keep default position */ }
        finally { Opacity = 1; }
    }

    /// <summary>(Re)populate the controls from the working copy. Suppresses change handlers.</summary>
    private void LoadFromConfig()
    {
        _loaded = false;
        var g = _working.General;
        RootBox.Text = _working.RootFolder;
        DataBox.Text = _paths.LocalFolder; // the LIVE data root (may differ from the registry pointer)
        ApplyStaticText();
        ConfigureDataLocationControls();
        UpdateDataLocationInfo();

        LanguageCombo.ItemsSource = LanguageSettings.Select(s => AppLanguage.LanguageLabel(s, UiLanguage)).ToList();
        var langIdx = Array.FindIndex(LanguageSettings,
            s => string.Equals(s, AppLanguage.NormalizeSetting(g.Language), StringComparison.OrdinalIgnoreCase));
        LanguageCombo.SelectedIndex = langIdx < 0 ? 0 : langIdx;

        ThemeCombo.ItemsSource = _themes.Select(t => t.Label).ToList();
        var idx = Array.FindIndex(_themes, t => t.Key == g.ThemeName);
        ThemeCombo.SelectedIndex = idx < 0 ? 0 : idx;

        var fontItems = BuildFontItems(UiLanguage);
        var fontView = new System.Windows.Data.ListCollectionView(fontItems);
        fontView.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(FontItem.Group)));
        FontCombo.ItemsSource = fontView;
        FontCombo.SelectedItem = fontItems.FirstOrDefault(i =>
            string.Equals(i.Family, g.FontFamily ?? string.Empty, StringComparison.OrdinalIgnoreCase));

        ColCount.Text = g.ColButtonCount.ToString();

        SizeCombo.ItemsSource = ButtonSizes.Select(SizeLabel).ToList();
        var sizeIdx = Array.FindIndex(ButtonSizes, s => s == g.ButtonSize);
        SizeCombo.SelectedIndex = sizeIdx < 0 ? 0 : sizeIdx; // default = 小 (Small)

        AutoCloseCb.IsChecked = g.AutoClose;
        TopMostCb.IsChecked = g.TopMost;

        _pendingAutoStart = !_isPortable && _autoStart.IsEnabled();
        AutoStartCb.IsChecked = _pendingAutoStart;

        HotkeyEnabledCb.IsChecked = g.GlobalHotkeyEnabled;
        HotkeyBox.Text = string.IsNullOrWhiteSpace(g.GlobalHotkey) ? L("None") : g.GlobalHotkey;
        UpdateHotkeyEnabledState();

        ShowToolsCb.IsChecked = g.ShowDefaultTools;
        BuildToolCheckboxes();

        ApplyWindowFont(); // render this window in the CURRENTLY-applied font

        _loaded = true;
    }

    private void ApplyStaticText()
    {
        Title = L("SettingsTitle");
        OkBtn.Content = L("Ok");
        CancelBtn.Content = L("Cancel");
        ApplyBtn.Content = L("Apply");
        AppearanceTab.Header = L("Appearance");
        BehaviorTab.Header = L("Behavior");
        FoldersTab.Header = L("Folders");
        ToolsTab.Header = L("CommonTools");
        UpdatesTab.Header = L("Updates");
        LanguageLabel.Text = L("Language");
        ThemeLabel.Text = L("Theme");
        ThemeFolderBtn.Content = L("ThemeFolder");
        ThemeFolderBtn.ToolTip = L("ThemeFolderTooltip");
        FontLabel.Text = L("Font");
        ColumnCountLabel.Text = L("ColumnCount");
        ButtonSizeLabel.Text = L("ButtonSize");
        AutoCloseCb.Content = L("AutoClose");
        TopMostCb.Content = L("TopMost");
        ApplyTaskbarPinState(_taskbarPinState);
        AutoStartCb.Content = L("AutoStart");
        HotkeyEnabledCb.Content = L("EnableHotkey");
        HotkeyLabel.Text = L("Hotkey");
        HotkeyBox.ToolTip = L("HotkeyTooltip");
        HotkeyClearBtn.Content = L("Clear");
        HotkeyHelpText.Text = L("HotkeyHelp");
        MainFolderTitle.Text = L("MainFolder");
        MainFolderHelpText.Text = L("MainFolderHelp");
        OpenRootBtn.Content = L("Open");
        PickRootBtn.Content = L("Choose");
        DataFolderTitle.Text = L("DataFolder");
        DataHelpText.Text = L("DataFolderHelp");
        OpenDataBtn.Content = L("Open");
        ChangeDataBtn.Content = L("Change");
        ResetDataBtn.Content = L("ResetDefault");
        ClearCacheBtn.Content = L("ClearCache");
        BackupBtn.Content = L("Backup");
        RestoreBtn.Content = L("Restore");
        ShowToolsCb.Content = L("ShowCommonTools");
        ToolsHintText.Text = L("CommonToolsHint");
        CurrentVersionText.Text = L("CurrentVersion", GetCurrentVersion());
        UpdateHelpText.Text = _updates.State.Status switch
        {
            AppUpdateStatus.StoreManaged => L("UpdateStoreManaged"),
            AppUpdateStatus.Disabled => L("UpdateUnavailable"),
            _ => L("UpdatePortableHelp"),
        };
        RenderUpdateState();
    }

    private static string GetCurrentVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    private void Updates_StateChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(RenderUpdateState);
            return;
        }

        RenderUpdateState();
    }

    private void RenderUpdateState()
    {
        if (UpdateStatusText is null)
            return;

        var state = _updates.State;
        UpdateStatusText.Text = state.Status switch
        {
            AppUpdateStatus.StoreManaged => L("UpdateStoreManagedStatus"),
            AppUpdateStatus.Disabled => L("UpdateDisabledStatus"),
            AppUpdateStatus.Checking => L("UpdateChecking"),
            AppUpdateStatus.UpToDate => L("UpdateUpToDate"),
            AppUpdateStatus.Available => L("UpdateAvailable", state.Version ?? ""),
            AppUpdateStatus.Downloading => L("UpdateDownloading", state.Version ?? "", state.Progress),
            AppUpdateStatus.ReadyToRestart => L("UpdateReady", state.Version ?? ""),
            AppUpdateStatus.Failed => L("UpdateFailed", state.Error ?? L("UnknownError")),
            _ => L("UpdateIdle"),
        };

        UpdateProgress.Visibility = state.Status == AppUpdateStatus.Downloading
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateProgress.Value = state.Progress;

        CheckUpdateBtn.Content = L("CheckForUpdates");
        CheckUpdateBtn.IsEnabled = state.Status is AppUpdateStatus.Idle
            or AppUpdateStatus.UpToDate
            or AppUpdateStatus.Available
            or AppUpdateStatus.Failed;

        UpdateActionBtn.Visibility = state.Status is AppUpdateStatus.Available
            or AppUpdateStatus.ReadyToRestart
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateActionBtn.Content = state.Status == AppUpdateStatus.ReadyToRestart
            ? L("RestartToUpdate")
            : L("DownloadUpdate");
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e) =>
        await _updates.CheckForUpdatesAsync();

    private async void UpdateAction_Click(object sender, RoutedEventArgs e)
    {
        if (_updates.State.Status == AppUpdateStatus.ReadyToRestart)
            _updates.ApplyAndRestart();
        else if (_updates.State.Status == AppUpdateStatus.Available)
            await _updates.DownloadUpdateAsync();
    }

    private string SizeLabel(string key) => key switch
    {
        "Large" => L("SizeLarge"),
        "Medium" => L("SizeMedium"),
        _ => L("SizeSmall"),
    };

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        if (ApplyBtn != null)
            ApplyBtn.IsEnabled = dirty;
    }

    // ---- Apply the configured UI font to this native WPF window ----

    private static readonly Dictionary<string, string> IntegratedFontFaces = new(StringComparer.OrdinalIgnoreCase)
    {
        // CSS family name -> the TTF's internal family name (bundled under <exe>\fonts).
        ["Alibaba PuHuiTi"] = "阿里巴巴普惠体 2.0 55 Regular",
        ["Pixel"] = "Fusion Pixel 12px Monospaced zh_hans",
        ["Press Start 2P"] = "Press Start 2P",
    };

    // Use the live (applied) font, not the pending one — the settings window only re-fonts on Apply.
    private void ApplyWindowFont() => FontFamily = ResolveWpfFont(_config.Config.General.FontFamily);

    private static System.Windows.Media.FontFamily ResolveWpfFont(string? configFont)
    {
        var name = string.IsNullOrWhiteSpace(configFont) ? "Alibaba PuHuiTi" : configFont.Trim();
        try
        {
            if (IntegratedFontFaces.TryGetValue(name, out var face))
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "fonts") + Path.DirectorySeparatorChar;
                var ff = new System.Windows.Media.FontFamily(new Uri(dir), "./#" + face);
                // A wrong face name or a missing fonts dir does NOT throw — it silently falls back to
                // a global default. Verify the face actually resolves; otherwise use Segoe UI.
                return FontResolves(ff) ? ff : new System.Windows.Media.FontFamily("Segoe UI");
            }
            return new System.Windows.Media.FontFamily(name); // installed system font
        }
        catch
        {
            return new System.Windows.Media.FontFamily("Segoe UI");
        }
    }

    private static bool FontResolves(System.Windows.Media.FontFamily ff)
    {
        try
        {
            var tf = new System.Windows.Media.Typeface(ff, System.Windows.FontStyles.Normal,
                System.Windows.FontWeights.Normal, System.Windows.FontStretches.Normal);
            return tf.TryGetGlyphTypeface(out _);
        }
        catch { return false; }
    }

    // ---- "常用工具" customization (against the working copy) ----

    private void BuildToolCheckboxes()
    {
        ToolsPanel.Children.Clear();
        foreach (var tool in _working.General.DefaultTools ?? Enumerable.Empty<DefaultTool>())
        {
            // Show the launch command (not just the name) so a tampered/roamed config that repoints
            // an innocuous-looking tool to an arbitrary command is visible before the user enables it.
            var displayName = AppLanguage.LocalizeToolName(tool.Command, tool.Name, UiLanguage);
            var cb = new CheckBox
            {
                Content = L("ToolCheckbox", displayName, tool.Command),
                ToolTip = L("ToolCommand", tool.Command),
                IsChecked = tool.Show,
                Margin = new Thickness(0, 3, 0, 3),
                Tag = tool,
            };
            cb.Click += Tool_Changed;
            ToolsPanel.Children.Add(cb);
        }
        ToolsPanel.IsEnabled = _working.General.ShowDefaultTools;
    }

    private void ShowTools_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;
        _working.General.ShowDefaultTools = ShowToolsCb.IsChecked == true;
        ToolsPanel.IsEnabled = _working.General.ShowDefaultTools;
        SetDirty(true);
    }

    private void Tool_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;
        if (sender is CheckBox { Tag: DefaultTool tool } cb)
        {
            tool.Show = cb.IsChecked == true;
            SetDirty(true);
        }
    }

    private void PickRoot_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = L("SelectRootFolder") };
        if (!string.IsNullOrEmpty(_working.RootFolder) && Directory.Exists(_working.RootFolder))
            dlg.InitialDirectory = _working.RootFolder;
        if (dlg.ShowDialog() == true)
        {
            _working.RootFolder = dlg.FolderName;
            RootBox.Text = dlg.FolderName;
            SetDirty(true);
        }
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || ThemeCombo.SelectedIndex < 0)
            return;
        _working.General.ThemeName = _themes[ThemeCombo.SelectedIndex].Key;
        // The Windows 2000 theme is paired with Verdana: set it on selection so it looks right out of
        // the box (the uniform font override would otherwise keep the previous font). Chinese labels
        // fall through Verdana to the system CJK font. Only sets it when not already chosen, so it
        // never fights a later manual font pick.
        if (string.Equals(_working.General.ThemeName, "Pixel", StringComparison.Ordinal))
            PairThemeFont("Verdana");
        SetDirty(true);
    }

    /// <summary>Set the working font family and reflect it in the font dropdown. Selecting the combo item
    /// re-enters Font_SelectionChanged, which no-ops because FontFamily already matches — no feedback loop.</summary>
    private void PairThemeFont(string family)
    {
        if (string.Equals(_working.General.FontFamily, family, StringComparison.OrdinalIgnoreCase))
            return;
        _working.General.FontFamily = family;
        foreach (var obj in FontCombo.Items)
        {
            if (obj is FontItem fi && string.Equals(fi.Family, family, StringComparison.OrdinalIgnoreCase))
            {
                FontCombo.SelectedItem = fi;
                break;
            }
        }
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || LanguageCombo.SelectedIndex < 0)
            return;

        _working.General.Language = LanguageSettings[LanguageCombo.SelectedIndex];
        SetDirty(true);
        RefreshLocalizedControls();
    }

    private void RefreshLocalizedControls()
    {
        var wasLoaded = _loaded;
        _loaded = false;
        var selectedLanguage = LanguageCombo.SelectedIndex;
        var selectedSize = _working.General.ButtonSize;
        var selectedFont = _working.General.FontFamily ?? string.Empty;

        ApplyStaticText();
        ConfigureDataLocationControls();

        LanguageCombo.ItemsSource = LanguageSettings.Select(s => AppLanguage.LanguageLabel(s, UiLanguage)).ToList();
        LanguageCombo.SelectedIndex = selectedLanguage < 0 ? 0 : selectedLanguage;

        var fontItems = BuildFontItems(UiLanguage);
        var fontView = new System.Windows.Data.ListCollectionView(fontItems);
        fontView.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(FontItem.Group)));
        FontCombo.ItemsSource = fontView;
        FontCombo.SelectedItem = fontItems.FirstOrDefault(i =>
            string.Equals(i.Family, selectedFont, StringComparison.OrdinalIgnoreCase));

        SizeCombo.ItemsSource = ButtonSizes.Select(SizeLabel).ToList();
        var sizeIdx = Array.FindIndex(ButtonSizes, s => s == selectedSize);
        SizeCombo.SelectedIndex = sizeIdx < 0 ? 0 : sizeIdx;

        HotkeyBox.Text = string.IsNullOrWhiteSpace(_working.General.GlobalHotkey)
            ? L("None")
            : _working.General.GlobalHotkey;
        BuildToolCheckboxes();
        UpdateDataLocationInfo();
        _loaded = wasLoaded;
    }

    /// <summary>Open the user-themes folder (creating + seeding it first) so the user can drop in
    /// their own .css. New files appear in the theme dropdown next time the settings window opens.</summary>
    private void OpenThemesFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _themeService.EnsureSeeded();
            _launch.OpenFolder(_themeService.ThemesDirectory);
        }
        catch { /* best effort: opening Explorer is not critical */ }
    }

    private void Font_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || FontCombo.SelectedItem is not FontItem item)
            return;
        var fam = FontUtil.Sanitize(item.Family); // "" for the theme-default entry
        if (fam == _working.General.FontFamily)
            return;
        _working.General.FontFamily = fam;
        SetDirty(true);
    }

    private void Size_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || SizeCombo.SelectedIndex < 0)
            return;
        _working.General.ButtonSize = ButtonSizes[SizeCombo.SelectedIndex];
        SetDirty(true);
    }

    private void Col_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;
        if (int.TryParse(ColCount.Text, out var n))
        {
            var clamped = Math.Clamp(n, 1, 30);
            ColCount.Text = clamped.ToString();
            if (clamped != _working.General.ColButtonCount)
            {
                _working.General.ColButtonCount = clamped;
                SetDirty(true);
            }
        }
        else
        {
            ColCount.Text = _working.General.ColButtonCount.ToString();
        }
    }

    private void Opt_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;
        var g = _working.General;
        g.AutoClose = AutoCloseCb.IsChecked == true;
        g.TopMost = TopMostCb.IsChecked == true;
        SetDirty(true);
    }

    private async void PinTaskbar_Click(object sender, RoutedEventArgs e)
    {
        PinTaskbarBtn.IsEnabled = false;
        PinTaskbarStatusText.Text = L("PinToTaskbarWorking");

        if (_taskbarPinState.Status is TaskbarPinStatus.Unsupported or TaskbarPinStatus.NotAllowed or
            TaskbarPinStatus.Failed)
        {
            OpenManualTaskbarPinLocation(_taskbarPinState);
            return;
        }

        var state = await _taskbarPin.RequestPinAsync();
        if (state.Status is TaskbarPinStatus.Unsupported or TaskbarPinStatus.NotAllowed or
            TaskbarPinStatus.Failed)
        {
            OpenManualTaskbarPinLocation(state);
            return;
        }

        ApplyTaskbarPinState(state);
    }

    private void OpenManualTaskbarPinLocation(TaskbarPinState state)
    {
        ApplyTaskbarPinState(state);
        if (_taskbarPin.OpenManualPinLocation())
        {
            PinTaskbarStatusText.Text = L("PinToTaskbarManualOpened");
            return;
        }

        PinTaskbarStatusText.Text = L("PinToTaskbarManualOpenFailed");
        Err(L("PinToTaskbarManualOpenFailed"));
    }

    private async Task RefreshTaskbarPinStateAsync()
    {
        PinTaskbarBtn.IsEnabled = false;
        ApplyTaskbarPinState(await _taskbarPin.GetStateAsync());
    }

    private void ApplyTaskbarPinState(TaskbarPinState state)
    {
        _taskbarPinState = state;
        PinTaskbarBtn.IsEnabled = state.Status is not TaskbarPinStatus.Pinned;
        PinTaskbarBtn.Content = state.Status is TaskbarPinStatus.Pinned
            ? L("PinnedToTaskbar")
            : state.Status is TaskbarPinStatus.Available
                ? L("PinToTaskbar")
                : L("PinToTaskbarManual");
        PinTaskbarStatusText.Text = state.Status switch
        {
            TaskbarPinStatus.Pinned => L("PinnedToTaskbarHelp"),
            TaskbarPinStatus.NotAllowed => L("PinToTaskbarNotAllowed"),
            TaskbarPinStatus.Unsupported => L("PinToTaskbarUnsupported"),
            TaskbarPinStatus.Failed => L("PinToTaskbarFailed", state.Error ?? L("UnknownError")),
            _ => L("PinToTaskbarHelp"),
        };
    }

    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _isPortable)
            return;
        _pendingAutoStart = AutoStartCb.IsChecked == true;
        SetDirty(true);
    }

    private void ConfigureDataLocationControls()
    {
        AutoStartCb.IsEnabled = !_isPortable;
        AutoStartCb.ToolTip = _isPortable ? L("AutoStartPortableHelp") : null;

        if (_isPortable)
        {
            DataHelpText.Text = L("DataFolderPortableHelp");
            ChangeDataBtn.IsEnabled = false;
            ResetDataBtn.IsEnabled = false;
            return;
        }

        if (!_isPackaged)
            return;

        DataHelpText.Text = L("DataFolderPackagedHelp");
        ChangeDataBtn.IsEnabled = false;
        ResetDataBtn.IsEnabled = false;
    }

    // ---- Global hotkey (capture combo into the working copy) ----

    private void UpdateHotkeyEnabledState() => HotkeyBox.IsEnabled = HotkeyEnabledCb.IsChecked == true;

    private void Hotkey_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;
        _working.General.GlobalHotkeyEnabled = HotkeyEnabledCb.IsChecked == true;
        UpdateHotkeyEnabledState();
        SetDirty(true);
    }

    private void Hotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_loaded)
            return;
        e.Handled = true; // capture the combo; never type into the read-only box
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var combo = HotkeyParser.FromKeyEvent(key, Keyboard.Modifiers);
        if (combo == null)
            return; // still composing (modifier-only) or no modifier held — keep waiting
        _working.General.GlobalHotkey = combo;
        HotkeyBox.Text = combo;
        SetDirty(true);
    }

    private void HotkeyClear_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;
        _working.General.GlobalHotkey = "";
        HotkeyBox.Text = L("None");
        SetDirty(true);
    }

    // ---- 确定 / 取消 / 应用 ----

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty)
            await CommitAsync();
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty && !await CommitAsync())
            return; // save failed (error already shown) — keep the window open so the user can retry
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close(); // discard the working copy

    /// <summary>Apply the working copy to the live config + autostart, save, and notify the launcher.
    /// Returns false (and rolls the in-memory swap back, telling the user) if the save fails, so a failed
    /// 应用/确定 never leaves live config diverged from disk or silently aliased to further edits.</summary>
    private async Task<bool> CommitAsync()
    {
        var prevGeneral = _config.Config.General;
        var prevRoot = _config.Config.RootFolder;
        _config.Config.General = _working.General;
        _config.Config.RootFolder = _working.RootFolder;
        try
        {
            await _config.SaveAsync();
        }
        catch (Exception ex)
        {
            // Roll the in-memory swap back so live config matches what's actually on disk, and SHOW the
            // error — the async-void callers would otherwise route the throw to the global dispatcher
            // handler, which swallows it (Handled=true) and leaves no dialog. _dirty stays true so the
            // user can retry; the rollback also un-aliases _config.Config.General from _working.General.
            _config.Config.General = prevGeneral;
            _config.Config.RootFolder = prevRoot;
            Err(L("SaveFailed", ex.Message));
            return false;
        }
        _autoStart.SetEnabled(_pendingAutoStart);
        _events.RaiseSettingsChanged();

        _working = _config.CloneConfig(); // re-baseline (fresh copy, not aliased to the live config)
        LoadFromConfig();                 // rebind controls (incl. tool checkbox Tags) to the new copy
        SetDirty(false);
        return true;
    }

    private async void ChangeData_Click(object sender, RoutedEventArgs e)
    {
        if (_isPortable)
        {
            Info(L("PortableDataCannotChange"));
            return;
        }

        if (_isPackaged)
        {
            Info(L("PackagedDataCannotChange"));
            return;
        }

        var dlg = new OpenFolderDialog { Title = L("SelectDataFolder") };
        if (dlg.ShowDialog() != true)
            return;
        await RelocateDataTo(dlg.FolderName);
    }

    private async void ResetData_Click(object sender, RoutedEventArgs e)
    {
        if (_isPortable)
        {
            Info(L("PortableDataCannotChange"));
            return;
        }

        if (_isPackaged)
        {
            Info(L("PackagedDataCannotReset"));
            return;
        }

        if (DataLocation.IsDefaultLocation(_paths.LocalFolder))
        {
            Info(L("DataAlreadyDefault"));
            return;
        }
        var def = DataLocation.DefaultRoot;
        if (File.Exists(Path.Combine(def, "config.json")))
        {
            // The default location already has a (leftover) config — don't silently adopt it; let the
            // user choose between using it and overwriting it with the current data.
            var r = MessageBox.Show(this,
                L("DefaultDataExists", def),
                L("ResetToDefault"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel)
                return;
            if (r == MessageBoxResult.No)
            {
                // Overwrite: remove the leftover config so RelocateDataTo MOVES the current data in.
                try { File.Delete(Path.Combine(def, "config.json")); }
                catch (Exception ex) { Err(L("UnableCleanDefaultConfig", ex.Message)); return; }
            }
            await RelocateDataTo(def);
            return;
        }
        if (MessageBox.Show(this, L("MoveDataConfirm", def), L("ResetToDefault"),
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        await RelocateDataTo(def);
    }

    /// <summary>Move/adopt the data folder at <paramref name="folder"/>, re-point the live paths,
    /// reload + rebind, and notify the launcher. Shared by 更改… and 重置为默认.</summary>
    private async Task RelocateDataTo(string folder)
    {
        // If the target already has a config, switching ADOPTS that config — any unapplied staged
        // edits are dropped (like 取消). Only when we'll MOVE the current data do we apply staged
        // edits first, so they travel with the moved config instead of being orphaned in the old
        // folder. (This matches DataLocation.Relocate's usedExisting test.)
        var targetHasData = File.Exists(Path.Combine(folder, "config.json"));
        if (_dirty && !targetHasData && !await CommitAsync())
            return; // staged edits couldn't be saved — don't move a stale config (error already shown)

        bool changed;
        bool usedExisting;
        try
        {
            changed = DataLocation.Relocate(folder, _paths.LocalFolder, out usedExisting);
        }
        catch (Exception ex)
        {
            Err(L("ChangeDataFailed", ex.Message));
            return;
        }

        if (!changed)
            return; // same folder

        // Re-point the live data paths, reload config from the new location, rebind, and notify.
        if (_paths is MutableAppDataPaths mutablePaths)
            mutablePaths.SetRoot(folder);
        DataLocation.InvalidateFolderSizeCache(); // data root changed → drop the cached size for the old root
        await _config.LoadAsync();
        _working = _config.CloneConfig();
        LoadFromConfig();
        SetDirty(false);
        _events.RaiseSettingsChanged();

        var msg = usedExisting
            ? L("DataSwitchedExisting")
            : L("DataMoved");
        Info(msg + "\n\n" + L("EffectiveNow"));
    }

    private void OpenRoot_Click(object sender, RoutedEventArgs e)
    {
        var folder = _working.RootFolder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            Info(L("RootNotSet"));
            return;
        }
        _launch.OpenFolder(folder);
    }

    private void OpenData_Click(object sender, RoutedEventArgs e) =>
        _launch.OpenFolder(_paths.LocalFolder);

    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        _resolver.ClearCache();
        _menu.ClearIconCache();
        _events.RaiseSettingsChanged();   // rebuild the menu: re-resolve links + re-extract icons
        DataLocation.InvalidateFolderSizeCache(); // the link-cache file was deleted → recompute the size
        UpdateDataLocationInfo();          // immediately reflect the post-delete (smaller) size

        // Schedule the second label refresh BEFORE the modal, so the delay tracks the cache-clear rather
        // than when the user dismisses the dialog: the rebuild rewrites the link-cache file via an
        // 800ms-debounced timer, so the on-disk size grows back ~1s later and the label must re-read it.
        _ = RefreshSizeLabelAfterAsync(1800);

        Info(L("CacheCleared"));
    }

    /// <summary>Re-read the data-folder size label after a delay (fire-and-forget). Swallows faults —
    /// incl. a write to a since-closed window — so they don't escape to the dispatcher handler.</summary>
    private async Task RefreshSizeLabelAfterAsync(int delayMs)
    {
        try
        {
            await Task.Delay(delayMs);
            DataLocation.InvalidateFolderSizeCache();
            UpdateDataLocationInfo();
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "延迟刷新数据大小标签失败"); }
    }

    private async void BackupConfig_Click(object sender, RoutedEventArgs e)
    {
        // Backup uses the SAVED config; warn if there are unapplied staged edits so the user isn't
        // surprised that on-screen-but-not-applied changes aren't in the backup.
        if (_dirty && MessageBox.Show(this,
                L("BackupQuestion"), L("BackupConfig"),
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        var dlg = new SaveFileDialog
        {
            Title = L("BackupConfig"),
            Filter = L("JsonConfigFilter"),
            FileName = L("BackupFileName"),
        };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            await _config.BackupToAsync(dlg.FileName);
            Info(L("BackupSaved", dlg.FileName));
        }
        catch (Exception ex)
        {
            Err(L("BackupFailed", ex.Message));
        }
    }

    private async void RestoreConfig_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = L("RestoreConfig"),
            Filter = L("AllFilesFilter"),
        };
        if (dlg.ShowDialog() != true)
            return;
        if (MessageBox.Show(this, L("RestoreConfirm"),
                "TanMenu", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;

        if (await _config.RestoreFromAsync(dlg.FileName))
        {
            _working = _config.CloneConfig();
            LoadFromConfig();
            SetDirty(false);
            _events.RaiseSettingsChanged();
            Info(L("RestoreSucceeded"));
        }
        else
        {
            Err(L("RestoreInvalid"));
        }
    }

    /// <summary>Refresh the data-folder location label (默认/自定义 + size) and the reset button's
    /// enabled state. The size is summed off the UI thread since a custom data root could be large.</summary>
    private async void UpdateDataLocationInfo()
    {
        try
        {
            var root = _paths.LocalFolder; // the LIVE data root (survives a startup LocalAppData fallback)
            var isDefault = !_isPackaged && !_isPortable && DataLocation.IsDefaultLocation(root);
            ResetDataBtn.IsEnabled = !_isPackaged && !_isPortable && !isDefault;
            var kind = _isPortable
                ? L("DataKindPortable")
                : _isPackaged
                    ? L("DataKindApp")
                    : isDefault ? L("DataKindDefault") : L("DataKindCustom");
            DataInfoText.Text = L("CurrentDataPending", kind);
            var bytes = await Task.Run(() => DataLocation.GetFolderSizeCached(root));
            DataInfoText.Text = L("CurrentDataSize", kind, FormatSize(bytes));
        }
        catch (Exception ex)
        {
            // async void: keep any failure (or a write to a since-closed window) from escaping to the
            // global dispatcher handler. GetFolderSize already swallows its own I/O errors.
            Serilog.Log.Warning(ex, "刷新数据文件夹信息失败");
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024):0.#} MB";
    }

    private void Info(string message) =>
        MessageBox.Show(this, message, "TanMenu", MessageBoxButton.OK, MessageBoxImage.Information);

    private void Err(string message) =>
        MessageBox.Show(this, message, "TanMenu", MessageBoxButton.OK, MessageBoxImage.Error);

    private void NumberOnly(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);
}
