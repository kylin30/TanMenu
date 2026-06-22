using System.IO;
using System.Linq;
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
    private static readonly (string Key, string Label)[] Themes =
    {
        ("Win98", "Windows 98"),
        ("WinXP", "Windows XP"),
        ("Win7", "Windows 7"),
    };

    private const string DefaultFontLabel = "默认（主题字体）";
    private const string GroupBuiltIn = "内置字体";
    private const string GroupSystem = "系统字体";

    /// <summary>One entry in the font dropdown. <see cref="Family"/> is the CSS value ("" = the
    /// theme's own font); <see cref="Group"/> drives the 内置字体 / 系统字体 grouping.</summary>
    private sealed record FontItem(string Name, string Family, string Group);

    /// <summary>App-bundled fonts first (default sentinel + the three @font-face families), then
    /// every installed system font family, sorted.</summary>
    private static List<FontItem> BuildFontItems()
    {
        var items = new List<FontItem>
        {
            new(DefaultFontLabel, "", GroupBuiltIn),
            new("阿里巴巴普惠体", "Alibaba PuHuiTi", GroupBuiltIn),
            new("Fusion Pixel 像素", "Pixel", GroupBuiltIn),
            new("Press Start 2P", "Press Start 2P", GroupBuiltIn),
        };
        foreach (var fam in System.Windows.Media.Fonts.SystemFontFamilies
                     .Select(f => f.Source)
                     .Where(s => !string.IsNullOrWhiteSpace(s))
                     .Distinct()
                     .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
        {
            items.Add(new FontItem(fam, fam, GroupSystem));
        }
        return items;
    }

    private readonly ConfigService _config;
    private readonly IAutoStartService _autoStart;
    private readonly AppEvents _events;

    // Edit-then-apply: all changes go to this working copy; nothing takes effect until 应用/确定.
    private AppConfig _working;
    private bool _pendingAutoStart;
    private bool _dirty;
    private bool _loaded;

    public SettingsWindow()
    {
        InitializeComponent();
        _config = App.Services.GetRequiredService<ConfigService>();
        _autoStart = App.Services.GetRequiredService<IAutoStartService>();
        _events = App.Services.GetRequiredService<AppEvents>();
        _working = _config.CloneConfig();

        try
        {
            var ico = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(ico))
                Icon = BitmapFrame.Create(new Uri(ico));
        }
        catch { /* icon optional */ }

        LoadFromConfig();

        // Position above the launcher once measured, then reveal — so the bottom-center, top-most
        // launcher never covers the settings content.
        Loaded += (_, _) => PositionAboveLauncher();
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
        DataBox.Text = DataLocation.GetDataRoot();

        ThemeCombo.ItemsSource = Themes.Select(t => t.Label).ToList();
        var idx = Array.FindIndex(Themes, t => t.Key == g.ThemeName);
        ThemeCombo.SelectedIndex = idx < 0 ? 0 : idx;

        var fontItems = BuildFontItems();
        var fontView = new System.Windows.Data.ListCollectionView(fontItems);
        fontView.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(FontItem.Group)));
        FontCombo.ItemsSource = fontView;
        FontCombo.SelectedItem = fontItems.FirstOrDefault(i =>
            string.Equals(i.Family, g.FontFamily ?? string.Empty, StringComparison.OrdinalIgnoreCase));

        ColCount.Text = g.ColButtonCount.ToString();
        AutoCloseCb.IsChecked = g.AutoClose;
        TopMostCb.IsChecked = g.TopMost;
        TaskbarCb.IsChecked = g.ShowInTaskbar;

        _pendingAutoStart = _autoStart.IsEnabled();
        AutoStartCb.IsChecked = _pendingAutoStart;

        ShowToolsCb.IsChecked = g.ShowDefaultTools;
        BuildToolCheckboxes();

        ApplyWindowFont(); // render this window in the CURRENTLY-applied font

        _loaded = true;
    }

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
            var cb = new CheckBox
            {
                Content = tool.Name,
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
        var dlg = new OpenFolderDialog { Title = "选择主文件夹" };
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
        _working.General.ThemeName = Themes[ThemeCombo.SelectedIndex].Key;
        SetDirty(true);
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
        g.ShowInTaskbar = TaskbarCb.IsChecked == true;
        SetDirty(true);
    }

    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;
        _pendingAutoStart = AutoStartCb.IsChecked == true;
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
        if (_dirty)
            await CommitAsync();
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close(); // discard the working copy

    /// <summary>Apply the working copy to the live config + autostart, save, and notify the launcher.</summary>
    private async Task CommitAsync()
    {
        _config.Config.General = _working.General;
        _config.Config.RootFolder = _working.RootFolder;
        await _config.SaveAsync();
        _autoStart.SetEnabled(_pendingAutoStart);
        _events.RaiseSettingsChanged();

        _working = _config.CloneConfig(); // re-baseline (fresh copy, not aliased to the live config)
        LoadFromConfig();                 // rebind controls (incl. tool checkbox Tags) to the new copy
        SetDirty(false);
    }

    private async void ChangeData_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "选择数据文件夹" };
        if (dlg.ShowDialog() != true)
            return;

        // If the target already has a config, switching ADOPTS that config — any unapplied staged
        // edits are dropped (like 取消). Only when we'll MOVE the current data do we apply staged
        // edits first, so they travel with the moved config instead of being orphaned in the old
        // folder. (This matches DataLocation.Relocate's usedExisting test.)
        var targetHasData = File.Exists(Path.Combine(dlg.FolderName, "config.json"));
        if (_dirty && !targetHasData)
            await CommitAsync();

        bool changed;
        bool usedExisting;
        try
        {
            changed = DataLocation.Relocate(dlg.FolderName, out usedExisting);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "更改数据文件夹失败：" + ex.Message, "TanMenu", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!changed)
            return; // same folder

        // Re-point the live data paths, reload config from the new location, rebind, and notify.
        if (App.Services.GetRequiredService<IAppDataPaths>() is MutableAppDataPaths paths)
            paths.SetRoot(dlg.FolderName);
        await _config.LoadAsync();
        _working = _config.CloneConfig();
        LoadFromConfig();
        SetDirty(false);
        _events.RaiseSettingsChanged();

        var msg = usedExisting
            ? "已切换到目标文件夹中的现有数据。"
            : "已将当前数据移动到新文件夹。";
        MessageBox.Show(this, msg + "\n\n已立即生效。", "TanMenu", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void NumberOnly(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);
}
