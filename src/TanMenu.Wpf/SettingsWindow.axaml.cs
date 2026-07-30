using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Models;
using TanMenu.Core.Services;
using TanMenu.Wpf.Dialogs;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf;

/// <summary>
/// The settings dialog. Edits happen on a private deep clone of the config (<c>_working</c>); nothing
/// reaches the live config until 应用/确定 (<see cref="CommitAsync"/>) — with the deliberate exceptions
/// listed on the 文件夹 tab (data-folder move/reset, 清理缓存 and 恢复 act immediately).
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>
    /// TanMenu now has exactly one look (Themes/Retro.axaml — Windows 2000), so the dropdown has a
    /// single entry. The persisted id is <see cref="AppConfig.CanonicalTheme"/>, which
    /// <c>ConfigService.Normalize</c> already coerces every historical value to on load — this must
    /// not hardcode a different id, or the settings window and the loader would disagree about what
    /// is stored. The key is opaque to the user; the label says what it really is.
    /// </summary>
    private const string SingleThemeKey = AppConfig.CanonicalTheme;
    private const string SingleThemeLabel = "Windows 2000";

    private static readonly string[] ButtonSizes = { "Small", "Medium", "Large" };
    private static readonly string[] LanguageSettings = { AppLanguage.Auto, AppLanguage.ZhHans, AppLanguage.EnUs };

    /// <summary>One row of the font dropdown. <see cref="Family"/> is the value written to config
    /// ("" = the app's own default font); <see cref="IsHeader"/> marks the 内置字体 / 系统字体 group
    /// captions, which are rendered as rows because Avalonia's ComboBox has no GroupStyle.</summary>
    private sealed record FontItem(string Name, string Family, bool IsHeader);

    /// <summary>#777777 — the group-header grey the WPF GroupStyle header template used.</summary>
    private static readonly IBrush GroupHeaderBrush = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));

    /// <summary>App-bundled fonts first (default sentinel + the three embedded families), then every
    /// installed system font family, sorted. Each group is preceded by its header row.</summary>
    private static List<FontItem> BuildFontItems(string language)
    {
        var items = new List<FontItem>
        {
            new(AppLanguage.Text("BuiltInFonts", language), "", true),
            new(AppLanguage.Text("DefaultFont", language), "", false),
            new("阿里巴巴普惠体", "Alibaba PuHuiTi", false),
            new("Fusion Pixel 像素", "Pixel", false),
            new("Press Start 2P", "Press Start 2P", false),
            new(AppLanguage.Text("SystemFonts", language), "", true),
        };
        foreach (var fam in FontManager.Current.SystemFonts
                     .Select(f => f.Name)
                     .Where(s => !string.IsNullOrWhiteSpace(s))
                     .Distinct()
                     .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
        {
            items.Add(new FontItem(fam, fam, false));
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

    /// <summary>The font row currently selected, so a click on a group header can be bounced back to it.</summary>
    private ComboBoxItem? _selectedFontRow;
    private bool _fontSelectionGuard;
    private bool _colTextGuard;

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
        _updates = App.Services.GetRequiredService<IAppUpdateService>();
        _taskbarPin = App.Services.GetRequiredService<TaskbarPinService>();
        _isPackaged = PackageRuntime.HasPackageIdentity;
        _isPortable = App.IsPortable;
        _working = _config.CloneConfig();

        try
        {
            using var ico = AssetLoader.Open(new Uri("avares://TanMenu.Wpf/wwwroot/app.ico"));
            Icon = new WindowIcon(ico);
        }
        catch { /* icon optional: it must never fail window construction */ }

        // Tunneling, so the read-only TextBox never sees the key first. Avalonia has no Key.System /
        // e.SystemKey indirection — an Alt combo arrives as the real key plus KeyModifiers.Alt.
        HotkeyBox.AddHandler(InputElement.KeyDownEvent, Hotkey_KeyDown, RoutingStrategies.Tunnel);

        LoadFromConfig();

        _updates.StateChanged += Updates_StateChanged;
        // The updater is a singleton that outlives this window: not unsubscribing would leak the
        // window and write to dead controls.
        Closed += (_, _) => _updates.StateChanged -= Updates_StateChanged;

        // Place the window BEFORE it is mapped, so the bottom-center, top-most launcher never covers
        // the settings content and there is no visible jump from the default position.
        PositionAboveLauncher();

        Opened += (_, _) => RunSafely(RefreshTaskbarPinStateAsync);
    }

    /// <summary>Centre the window over the launcher (or upper-centre of the work area) and clamp it to
    /// that screen. The WPF build did this in Loaded and hid the intermediate frame behind
    /// <c>Opacity=0</c>; Avalonia lets a window be moved before it is shown, so the reveal trick is
    /// unnecessary. Sizes are the declared 540x540 rather than a measured size — the window is not
    /// SizeToContent, so waiting for a layout pass would buy nothing but the flash.</summary>
    /// <summary>Re-place once the native frame exists: the constructor pass could only reserve the
    /// client size, so this is what actually lands the window 12px above the launcher instead of
    /// overlapping it. Runs before the first frame is composited, so there is no visible jump.</summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PositionAboveLauncher();
    }

    private void PositionAboveLauncher()
    {
        try
        {
            var launcher = App.Services.GetService<IWindowHost>()?.Window;
            var screen = (launcher is not null ? Screens.ScreenFromWindow(launcher) : null) ?? Screens.Primary;
            if (screen is null)
                return;

            var wa = screen.WorkingArea;                 // physical pixels
            var scaling = screen.Scaling;
            // Width/Height are the CLIENT size; the placement has to reserve the title bar and borders
            // too, or the window lands roughly one title bar too low and the top-most launcher ends up
            // sitting over the 确定/取消/应用 row. FrameSize only exists once the window is realized,
            // so the constructor pass uses the client size and Opened re-runs this with the real frame.
            var frame = FrameSize;
            var widthDip = frame?.Width ?? (double.IsNaN(Width) ? ClientSize.Width : Width);
            var heightDip = frame?.Height ?? (double.IsNaN(Height) ? ClientSize.Height : Height);
            var w = (int)Math.Round(widthDip * scaling);
            var h = (int)Math.Round(heightDip * scaling);

            int left, top;
            Size launcherSize = launcher is null ? default : launcher.FrameSize ?? launcher.ClientSize;
            if (launcher is { IsVisible: true } && launcherSize.Width > 0)
            {
                var lw = (int)Math.Round(launcherSize.Width * launcher.DesktopScaling);
                left = launcher.Position.X + (lw - w) / 2;                      // centred over the launcher
                top = launcher.Position.Y - h - (int)Math.Round(12 * scaling);  // directly above it
            }
            else
            {
                left = wa.X + (wa.Width - w) / 2;                                // fallback: upper-centre
                top = wa.Y + (int)Math.Round(40 * scaling);
            }

            Position = new PixelPoint(
                Math.Max(wa.X, Math.Min(left, wa.Right - w)),
                Math.Max(wa.Y, Math.Min(top, wa.Bottom - h)));
        }
        catch { /* keep the default position */ }
    }

    /// <summary>Run an async click handler without letting a fault escape. Avalonia has no WPF-style
    /// global DispatcherUnhandledException hook, so an escaping async-void fault would tear the process
    /// down; every user-visible failure already has its own dialog, so logging is the right floor.</summary>
    private static async void RunSafely(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Serilog.Log.Error(ex, "设置窗口操作失败"); }
    }

    /// <summary>(Re)populate the controls from the working copy. Suppresses change handlers.</summary>
    private void LoadFromConfig()
    {
        _loaded = false;
        var g = _working.General;

        // The CSS theme system is gone. A stored WinXP/Windows11/Pixel/user:<name>/junk value is
        // canonicalized on the WORKING copy only, so it is rewritten on the next Apply without
        // discarding anything else the user has configured. This is a defensive UI-side fallback:
        // the migration itself belongs in ConfigService.Normalize (TanMenu.Core), which still carries
        // the old Fluent2/Win98/Win31/Win2000 renames.
        if (!string.Equals(g.ThemeName, SingleThemeKey, StringComparison.Ordinal))
            g.ThemeName = SingleThemeKey;

        RootBox.Text = _working.RootFolder;
        DataBox.Text = _paths.LocalFolder; // the LIVE data root (may differ from the registry pointer)
        ApplyStaticText();
        ConfigureDataLocationControls();
        UpdateDataLocationInfo();

        LanguageCombo.ItemsSource = LanguageSettings.Select(s => AppLanguage.LanguageLabel(s, UiLanguage)).ToList();
        var langIdx = Array.FindIndex(LanguageSettings,
            s => string.Equals(s, AppLanguage.NormalizeSetting(g.Language), StringComparison.OrdinalIgnoreCase));
        LanguageCombo.SelectedIndex = langIdx < 0 ? 0 : langIdx;

        ThemeCombo.ItemsSource = new List<string> { SingleThemeLabel };
        ThemeCombo.SelectedIndex = 0;

        PopulateFontCombo(g.FontFamily ?? string.Empty);

        SetColumnCountText(g.ColButtonCount.ToString());

        SizeCombo.ItemsSource = ButtonSizes.Select(SizeLabel).ToList();
        var sizeIdx = Array.FindIndex(ButtonSizes, s => s == g.ButtonSize);
        SizeCombo.SelectedIndex = sizeIdx < 0 ? 0 : sizeIdx; // default = 小 (Small)

        AutoCloseCb.IsChecked = g.AutoClose;
        TopMostCb.IsChecked = g.TopMost;

        // Autostart is not in AppConfig — read the real OS state (Run key / MSIX StartupTask).
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
        FontLabel.Text = L("Font");
        ColumnCountLabel.Text = L("ColumnCount");
        ButtonSizeLabel.Text = L("ButtonSize");
        AutoCloseCb.Content = L("AutoClose");
        TopMostCb.Content = L("TopMost");
        ApplyTaskbarPinState(_taskbarPinState);
        AutoStartCb.Content = L("AutoStart");
        HotkeyEnabledCb.Content = L("EnableHotkey");
        HotkeyLabel.Text = L("Hotkey");
        ToolTip.SetTip(HotkeyBox, L("HotkeyTooltip"));
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

    // ---- 更新 tab ----

    private void Updates_StateChanged(object? sender, EventArgs e)
    {
        // Velopack's download-progress callback runs on a thread-pool thread.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RenderUpdateState);
            return;
        }

        RenderUpdateState();
    }

    private void RenderUpdateState()
    {
        // Defensive: a state event can arrive around control availability / after Close.
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

        UpdateProgress.IsVisible = state.Status == AppUpdateStatus.Downloading;
        UpdateProgress.Value = state.Progress;

        CheckUpdateBtn.Content = L("CheckForUpdates");
        CheckUpdateBtn.IsEnabled = state.Status is AppUpdateStatus.Idle
            or AppUpdateStatus.UpToDate
            or AppUpdateStatus.Available
            or AppUpdateStatus.Failed;

        UpdateActionBtn.IsVisible = state.Status is AppUpdateStatus.Available or AppUpdateStatus.ReadyToRestart;
        UpdateActionBtn.Content = state.Status == AppUpdateStatus.ReadyToRestart
            ? L("RestartToUpdate")
            : L("DownloadUpdate");
    }

    // The service catches internally and reports Failed, so there is no try/catch here.
    private void CheckUpdate_Click(object? sender, RoutedEventArgs e) =>
        RunSafely(() => _updates.CheckForUpdatesAsync());

    private void UpdateAction_Click(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        if (_updates.State.Status == AppUpdateStatus.ReadyToRestart)
            _updates.ApplyAndRestart();
        else if (_updates.State.Status == AppUpdateStatus.Available)
            await _updates.DownloadUpdateAsync();
    });

    // ---- 外观 tab ----

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

    /// <summary>Rebuild the font dropdown and restore the selection for <paramref name="selectedFamily"/>.
    /// No match (an uninstalled configured font) deliberately leaves the combo blank without touching
    /// the stored value.</summary>
    private void PopulateFontCombo(string selectedFamily)
    {
        FontCombo.Items.Clear();
        _selectedFontRow = null;

        ComboBoxItem? match = null;
        foreach (var item in BuildFontItems(UiLanguage))
        {
            var row = new ComboBoxItem { Content = item.Name, Tag = item };
            if (item.IsHeader)
            {
                // Avalonia's ComboBox has no GroupStyle, so group captions are ordinary rows made
                // non-selectable: disabled here, and bounced back in Font_SelectionChanged in case a
                // keyboard navigation ever lands on one anyway.
                row.IsEnabled = false;
                row.FontWeight = FontWeight.Bold;
                row.Foreground = GroupHeaderBrush;
                row.Margin = new Thickness(0, 3, 0, 0);
            }
            else if (match is null && string.Equals(item.Family, selectedFamily, StringComparison.OrdinalIgnoreCase))
            {
                match = row;
            }
            FontCombo.Items.Add(row);
        }

        FontCombo.SelectedItem = match;
        _selectedFontRow = match;
    }

    private void Font_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_fontSelectionGuard)
            return;

        var row = FontCombo.SelectedItem as ComboBoxItem;
        if (row?.Tag is FontItem { IsHeader: true })
        {
            // A group caption is a label, never a choice.
            _fontSelectionGuard = true;
            try { FontCombo.SelectedItem = _selectedFontRow; }
            finally { _fontSelectionGuard = false; }
            return;
        }

        _selectedFontRow = row;
        if (!_loaded || row?.Tag is not FontItem item)
            return;

        var fam = FontUtil.Sanitize(item.Family); // "" for the default-font entry
        if (fam == _working.General.FontFamily)
            return;
        _working.General.FontFamily = fam;
        SetDirty(true);
    }

    private void Language_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || LanguageCombo.SelectedIndex < 0)
            return;

        _working.General.Language = LanguageSettings[LanguageCombo.SelectedIndex];
        SetDirty(true);
        RefreshLocalizedControls();
    }

    /// <summary>Re-label the whole window in the PENDING language, before Apply. Uses a save/restore of
    /// <c>_loaded</c> rather than a plain false/true pair because it is called from inside a change
    /// handler and may therefore nest.</summary>
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

        PopulateFontCombo(selectedFont);

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

    private void Size_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || SizeCombo.SelectedIndex < 0)
            return;
        _working.General.ButtonSize = ButtonSizes[SizeCombo.SelectedIndex];
        SetDirty(true);
    }

    private void SetColumnCountText(string text)
    {
        _colTextGuard = true;
        try { ColCount.Text = text; }
        finally { _colTextGuard = false; }
    }

    /// <summary>Keep the box digits-only. WPF filtered typed characters with PreviewTextInput; Avalonia's
    /// TextInput event cannot be intercepted ahead of the TextBox, so the text is scrubbed after the fact
    /// — which also covers paste and IME, unlike the WPF version.</summary>
    private void ColCount_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_colTextGuard)
            return;
        var text = ColCount.Text;
        if (string.IsNullOrEmpty(text))
            return;
        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (digits == text)
            return;
        _colTextGuard = true;
        try
        {
            ColCount.Text = digits;
            ColCount.CaretIndex = digits.Length;
        }
        finally { _colTextGuard = false; }
    }

    private void Col_Changed(object? sender, RoutedEventArgs e) => CommitColumnCount();

    /// <summary>Commit the column-count box (clamped to 1..30, unparsable text reverted). Called on
    /// LostFocus, and again from 应用/确定 so the last-typed value can never be lost if the button click
    /// somehow does not move focus first.</summary>
    private void CommitColumnCount()
    {
        if (!_loaded)
            return;
        if (int.TryParse(ColCount.Text, out var n))
        {
            var clamped = Math.Clamp(n, 1, 30);
            SetColumnCountText(clamped.ToString());
            if (clamped != _working.General.ColButtonCount)
            {
                _working.General.ColButtonCount = clamped;
                SetDirty(true);
            }
        }
        else
        {
            SetColumnCountText(_working.General.ColButtonCount.ToString());
        }
    }

    // ---- Apply the configured UI font to this window ----

    // Use the live (applied) font, not the pending one — the settings window only re-fonts on Apply.
    // Resolution lives in AppFonts so this window and the launcher cannot disagree about what a
    // configured name means (they did: one looked the bundled families up by a hardcoded, and
    // locale-dependent, internal name while the other used the bare config string).
    private void ApplyWindowFont() => FontFamily = AppFonts.Resolve(_config.Config.General.FontFamily);

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
                IsChecked = tool.Show,
                Margin = new Thickness(0, 3, 0, 3),
                Tag = tool, // the LIVE _working DefaultTool instance
            };
            ToolTip.SetTip(cb, L("ToolCommand", tool.Command));
            cb.Click += Tool_Changed;
            ToolsPanel.Children.Add(cb);
        }
        ToolsPanel.IsEnabled = _working.General.ShowDefaultTools;
    }

    private void ShowTools_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;
        _working.General.ShowDefaultTools = ShowToolsCb.IsChecked == true;
        // Unchecking only disables the list; the individual states stay and are still persisted.
        ToolsPanel.IsEnabled = _working.General.ShowDefaultTools;
        SetDirty(true);
    }

    private void Tool_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;
        if (sender is CheckBox { Tag: DefaultTool tool } cb)
        {
            tool.Show = cb.IsChecked == true;
            SetDirty(true);
        }
    }

    // ---- 行为 tab ----

    private void Opt_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;
        var g = _working.General;
        g.AutoClose = AutoCloseCb.IsChecked == true;
        g.TopMost = TopMostCb.IsChecked == true;
        SetDirty(true);
    }

    private void PinTaskbar_Click(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        PinTaskbarBtn.IsEnabled = false;
        PinTaskbarStatusText.Text = L("PinToTaskbarWorking");

        if (_taskbarPinState.Status is TaskbarPinStatus.Unsupported or TaskbarPinStatus.NotAllowed or
            TaskbarPinStatus.Failed)
        {
            await OpenManualTaskbarPinLocationAsync(_taskbarPinState);
            return;
        }

        var state = await _taskbarPin.RequestPinAsync();
        if (state.Status is TaskbarPinStatus.Unsupported or TaskbarPinStatus.NotAllowed or
            TaskbarPinStatus.Failed)
        {
            await OpenManualTaskbarPinLocationAsync(state);
            return;
        }

        // Pinned, or Available again because the user declined the Windows consent dialog.
        ApplyTaskbarPinState(state);
    });

    private async Task OpenManualTaskbarPinLocationAsync(TaskbarPinState state)
    {
        ApplyTaskbarPinState(state); // re-enables the button and sets the state message
        if (_taskbarPin.OpenManualPinLocation())
        {
            PinTaskbarStatusText.Text = L("PinToTaskbarManualOpened");
            return;
        }

        PinTaskbarStatusText.Text = L("PinToTaskbarManualOpenFailed");
        await ErrAsync(L("PinToTaskbarManualOpenFailed"));
    }

    private async Task RefreshTaskbarPinStateAsync()
    {
        PinTaskbarBtn.IsEnabled = false; // dead while the async query runs
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

    private void AutoStart_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_loaded || _isPortable)
            return;
        _pendingAutoStart = AutoStartCb.IsChecked == true;
        SetDirty(true);
    }

    private void ConfigureDataLocationControls()
    {
        AutoStartCb.IsEnabled = !_isPortable;
        if (_isPortable)
        {
            ToolTip.SetTip(AutoStartCb, L("AutoStartPortableHelp"));
            // The checkbox is disabled in portable builds, so the tooltip must survive that.
            ToolTip.SetShowOnDisabled(AutoStartCb, true);
        }
        else
        {
            ToolTip.SetTip(AutoStartCb, null);
        }

        if (_isPortable)
        {
            DataHelpText.Text = L("DataFolderPortableHelp");
            ChangeDataBtn.IsEnabled = false;
            ResetDataBtn.IsEnabled = false;
            return;
        }

        if (!_isPackaged)
            return; // plain desktop build: keep the defaults set by ApplyStaticText

        DataHelpText.Text = L("DataFolderPackagedHelp");
        ChangeDataBtn.IsEnabled = false;
        ResetDataBtn.IsEnabled = false;
    }

    // ---- Global hotkey (capture combo into the working copy) ----

    // 清除 stays enabled even when the feature is off — that is deliberate.
    private void UpdateHotkeyEnabledState() => HotkeyBox.IsEnabled = HotkeyEnabledCb.IsChecked == true;

    private void Hotkey_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;
        _working.General.GlobalHotkeyEnabled = HotkeyEnabledCb.IsChecked == true;
        UpdateHotkeyEnabledState();
        SetDirty(true);
    }

    private void Hotkey_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!_loaded)
            return;
        e.Handled = true; // capture the combo; never type into the read-only box
        var combo = HotkeyParser.FromKeyEvent(e.Key, e.KeyModifiers);
        if (combo == null)
            return; // still composing (modifier-only) or no modifier held — keep waiting
        _working.General.GlobalHotkey = combo;
        HotkeyBox.Text = combo;
        SetDirty(true);
    }

    private void HotkeyClear_Click(object? sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;
        _working.General.GlobalHotkey = "";
        HotkeyBox.Text = L("None");
        SetDirty(true);
    }

    // ---- 确定 / 取消 / 应用 ----

    private void Apply_Click(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        CommitColumnCount();
        if (_dirty)
            await CommitAsync();
    });

    private void Ok_Click(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        CommitColumnCount();
        if (_dirty && !await CommitAsync())
            return; // save failed (error already shown) — keep the window open so the user can retry
        Close();
    });

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(); // discard the working copy

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
            // error rather than letting the throw disappear into a logged async-void fault. _dirty stays
            // true so the user can retry; the rollback also un-aliases _config.Config.General from
            // _working.General.
            _config.Config.General = prevGeneral;
            _config.Config.RootFolder = prevRoot;
            await ErrAsync(L("SaveFailed", ex.Message));
            return false;
        }
        _autoStart.SetEnabled(_pendingAutoStart);
        _events.RaiseSettingsChanged();

        _working = _config.CloneConfig(); // re-baseline (fresh copy, not aliased to the live config)
        LoadFromConfig();                 // rebind controls (incl. tool checkbox Tags) to the new copy
        SetDirty(false);
        return true;
    }

    // ---- 文件夹 tab ----

    private void PickRoot_Click(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        var options = new FolderPickerOpenOptions { Title = L("SelectRootFolder"), AllowMultiple = false };
        if (!string.IsNullOrEmpty(_working.RootFolder) && Directory.Exists(_working.RootFolder))
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(_working.RootFolder);

        var picked = await PickFolderAsync(options);
        if (picked is null)
            return;
        // Staged — applied only on 应用/确定.
        _working.RootFolder = picked;
        RootBox.Text = picked;
        SetDirty(true);
    });

    private void OpenRoot_Click(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        var folder = _working.RootFolder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            await InfoAsync(L("RootNotSet"));
            return;
        }
        _launch.OpenFolder(folder);
    });

    private void OpenData_Click(object? sender, RoutedEventArgs e) =>
        _launch.OpenFolder(_paths.LocalFolder);

    private void ChangeData_Click(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        // Defence in depth: these buttons are already disabled in portable/packaged builds.
        if (_isPortable)
        {
            await InfoAsync(L("PortableDataCannotChange"));
            return;
        }

        if (_isPackaged)
        {
            await InfoAsync(L("PackagedDataCannotChange"));
            return;
        }

        var picked = await PickFolderAsync(new FolderPickerOpenOptions
        {
            Title = L("SelectDataFolder"),
            AllowMultiple = false,
        });
        if (picked is null)
            return;
        await RelocateDataTo(picked);
    });

    private void ResetData_Click(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        if (_isPortable)
        {
            await InfoAsync(L("PortableDataCannotChange"));
            return;
        }

        if (_isPackaged)
        {
            await InfoAsync(L("PackagedDataCannotReset"));
            return;
        }

        if (DataLocation.IsDefaultLocation(_paths.LocalFolder))
        {
            await InfoAsync(L("DataAlreadyDefault"));
            return;
        }
        var def = DataLocation.DefaultRoot;
        if (File.Exists(Path.Combine(def, "config.json")))
        {
            // The default location already has a (leftover) config — don't silently adopt it; let the
            // user choose between using it and overwriting it with the current data.
            var r = await MessageBox.ShowAsync(this, L("DefaultDataExists", def), L("ResetToDefault"),
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == MessageBoxResult.Cancel)
                return;
            if (r == MessageBoxResult.No)
            {
                // Overwrite: remove the leftover config so RelocateDataTo MOVES the current data in.
                try { File.Delete(Path.Combine(def, "config.json")); }
                catch (Exception ex)
                {
                    await ErrAsync(L("UnableCleanDefaultConfig", ex.Message));
                    return;
                }
            }
            await RelocateDataTo(def);
            return;
        }
        if (await MessageBox.ShowAsync(this, L("MoveDataConfirm", def), L("ResetToDefault"),
                MessageBoxButtons.OkCancel, MessageBoxIcon.Question) != MessageBoxResult.Ok)
            return;
        await RelocateDataTo(def);
    });

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
            await ErrAsync(L("ChangeDataFailed", ex.Message));
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
        await InfoAsync(msg + "\n\n" + L("EffectiveNow"));
    }

    private void ClearCache_Click(object? sender, RoutedEventArgs e) => RunSafely(async () =>
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

        await InfoAsync(L("CacheCleared"));
    });

    /// <summary>Re-read the data-folder size label after a delay (fire-and-forget). Swallows faults —
    /// incl. a write to a since-closed window — so they never surface as an unobserved task fault.</summary>
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

    private void BackupConfig_Click(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        // Backup uses the SAVED config; warn if there are unapplied staged edits so the user isn't
        // surprised that on-screen-but-not-applied changes aren't in the backup.
        if (_dirty && await MessageBox.ShowAsync(this, L("BackupQuestion"), L("BackupConfig"),
                MessageBoxButtons.OkCancel, MessageBoxIcon.Warning) != MessageBoxResult.Ok)
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L("BackupConfig"),
            SuggestedFileName = L("BackupFileName"),
            DefaultExtension = "json",
            FileTypeChoices = ParseFilter(L("JsonConfigFilter")),
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
            return;
        try
        {
            await _config.BackupToAsync(path);
            await InfoAsync(L("BackupSaved", path));
        }
        catch (Exception ex)
        {
            await ErrAsync(L("BackupFailed", ex.Message));
        }
    });

    private void RestoreConfig_Click(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L("RestoreConfig"),
            AllowMultiple = false,
            FileTypeFilter = ParseFilter(L("AllFilesFilter")),
        });
        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (string.IsNullOrEmpty(path))
            return;
        // The confirm dialog's title is the literal product name, not the picker's title.
        if (await MessageBox.ShowAsync(this, L("RestoreConfirm"), "TanMenu",
                MessageBoxButtons.OkCancel, MessageBoxIcon.Warning) != MessageBoxResult.Ok)
            return;

        if (await _config.RestoreFromAsync(path))
        {
            _working = _config.CloneConfig();
            LoadFromConfig();
            SetDirty(false);
            _events.RaiseSettingsChanged();
            await InfoAsync(L("RestoreSucceeded"));
        }
        else
        {
            // An IO failure and an invalid file deliberately produce the same message.
            await ErrAsync(L("RestoreInvalid"));
        }
    });

    /// <summary>Refresh the data-folder location label (默认/自定义 + size) and the reset button's
    /// enabled state. The size is summed off the UI thread since a custom data root could be large.</summary>
    private void UpdateDataLocationInfo() => _ = UpdateDataLocationInfoAsync();

    private async Task UpdateDataLocationInfoAsync()
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
            // Fire-and-forget: keep any failure (or a write to a since-closed window) from surfacing as
            // an unobserved task fault. GetFolderSize already swallows its own I/O errors.
            Serilog.Log.Warning(ex, "刷新数据文件夹信息失败");
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024):0.#} MB";
    }

    // ---- Shared plumbing ----

    /// <summary>Run a folder picker and return the chosen local path, or null if cancelled / the chosen
    /// item has no filesystem path (a shell location such as "This PC").</summary>
    private async Task<string?> PickFolderAsync(FolderPickerOpenOptions options)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(options);
        if (folders.Count == 0)
            return null;
        var path = folders[0].TryGetLocalPath();
        return string.IsNullOrEmpty(path) ? null : path;
    }

    /// <summary>Turn a legacy Win32 filter resource ("名称 (*.json)|*.json|所有文件 (*.*)|*.*") into
    /// Avalonia's structured picker types, so the localized filter strings keep working verbatim.</summary>
    private static List<FilePickerFileType> ParseFilter(string filter)
    {
        var parts = filter.Split('|');
        var types = new List<FilePickerFileType>();
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            types.Add(new FilePickerFileType(parts[i].Trim())
            {
                Patterns = parts[i + 1].Split(';',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            });
        }
        return types;
    }

    private Task<MessageBoxResult> InfoAsync(string message) =>
        MessageBox.ShowAsync(this, message, "TanMenu", MessageBoxButtons.Ok, MessageBoxIcon.Info);

    private Task<MessageBoxResult> ErrAsync(string message) =>
        MessageBox.ShowAsync(this, message, "TanMenu", MessageBoxButtons.Ok, MessageBoxIcon.Error);
}
