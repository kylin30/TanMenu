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
    private bool _loaded;

    public SettingsWindow()
    {
        InitializeComponent();
        _config = App.Services.GetRequiredService<ConfigService>();
        _autoStart = App.Services.GetRequiredService<IAutoStartService>();
        _events = App.Services.GetRequiredService<AppEvents>();

        try
        {
            var ico = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(ico))
                Icon = BitmapFrame.Create(new Uri(ico));
        }
        catch { /* icon optional */ }

        LoadFromConfig();

        // Position above the launcher once measured (SizeToContent=Height), then reveal — so the
        // bottom-center, top-most launcher never covers the settings content.
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
                top = launcher.Top - h - 12;                      // directly above it
            }
            else
            {
                left = wa.Left + (wa.Width - w) / 2;              // fallback: upper-center
                top = wa.Top + 40;
            }

            Left = Math.Max(wa.Left, Math.Min(left, wa.Right - w));
            Top = Math.Max(wa.Top, Math.Min(top, wa.Bottom - h));
        }
        catch { /* keep default position */ }
        finally { Opacity = 1; }
    }

    private void LoadFromConfig()
    {
        _loaded = false; // suppress change handlers while (re)populating controls
        var g = _config.Config.General;
        RootBox.Text = _config.Config.RootFolder;
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
        AutoStartCb.IsChecked = _autoStart.IsEnabled();

        ShowToolsCb.IsChecked = g.ShowDefaultTools;
        BuildToolCheckboxes();

        ApplyWindowFont(); // render this window in the configured app font, too

        _loaded = true;
    }

    // ---- Apply the configured UI font to this native WPF window ----

    private static readonly Dictionary<string, string> IntegratedFontFaces = new(StringComparer.OrdinalIgnoreCase)
    {
        // CSS family name -> the TTF's internal family name (bundled under <exe>\fonts).
        ["Alibaba PuHuiTi"] = "阿里巴巴普惠体 2.0 55 Regular",
        ["Pixel"] = "Fusion Pixel 12px Monospaced zh",
        ["Press Start 2P"] = "Press Start 2P",
    };

    private void ApplyWindowFont() => FontFamily = ResolveWpfFont(_config.Config.General.FontFamily);

    private static System.Windows.Media.FontFamily ResolveWpfFont(string? configFont)
    {
        var name = string.IsNullOrWhiteSpace(configFont) ? "Alibaba PuHuiTi" : configFont.Trim();
        try
        {
            if (IntegratedFontFaces.TryGetValue(name, out var face))
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "fonts") + Path.DirectorySeparatorChar;
                return new System.Windows.Media.FontFamily(new Uri(dir), "./#" + face);
            }
            return new System.Windows.Media.FontFamily(name); // installed system font
        }
        catch
        {
            return new System.Windows.Media.FontFamily("Segoe UI");
        }
    }

    // ---- "常用工具" customization ----

    private void BuildToolCheckboxes()
    {
        ToolsPanel.Children.Clear();
        foreach (var tool in _config.Config.General.DefaultTools)
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
        ToolsPanel.IsEnabled = _config.Config.General.ShowDefaultTools;
    }

    private void ShowTools_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;
        _config.Config.General.ShowDefaultTools = ShowToolsCb.IsChecked == true;
        ToolsPanel.IsEnabled = _config.Config.General.ShowDefaultTools;
        Persist();
    }

    private void Tool_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;
        if (sender is CheckBox { Tag: DefaultTool tool } cb)
        {
            tool.Show = cb.IsChecked == true;
            Persist();
        }
    }

    private async void Persist()
    {
        if (!_loaded)
            return;
        await _config.SaveAsync();
        _events.RaiseSettingsChanged();
    }

    private void PickRoot_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "选择主文件夹" };
        if (!string.IsNullOrEmpty(_config.Config.RootFolder) && Directory.Exists(_config.Config.RootFolder))
            dlg.InitialDirectory = _config.Config.RootFolder;
        if (dlg.ShowDialog() == true)
        {
            _config.Config.RootFolder = dlg.FolderName;
            RootBox.Text = dlg.FolderName;
            Persist();
        }
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || ThemeCombo.SelectedIndex < 0)
            return;
        _config.Config.General.ThemeName = Themes[ThemeCombo.SelectedIndex].Key;
        Persist();
    }

    private void Font_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || FontCombo.SelectedItem is not FontItem item)
            return;
        var fam = FontUtil.Sanitize(item.Family); // "" for the theme-default entry
        if (fam == _config.Config.General.FontFamily)
            return;
        _config.Config.General.FontFamily = fam;
        ApplyWindowFont(); // re-font this window live
        Persist();
    }

    private void Col_Changed(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(ColCount.Text, out var n))
        {
            _config.Config.General.ColButtonCount = Math.Clamp(n, 1, 30);
            ColCount.Text = _config.Config.General.ColButtonCount.ToString();
            Persist();
        }
    }

    private void Opt_Changed(object sender, RoutedEventArgs e)
    {
        var g = _config.Config.General;
        g.AutoClose = AutoCloseCb.IsChecked == true;
        g.TopMost = TopMostCb.IsChecked == true;
        g.ShowInTaskbar = TaskbarCb.IsChecked == true;
        Persist();
    }

    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        _autoStart.SetEnabled(AutoStartCb.IsChecked == true);
        AutoStartCb.IsChecked = _autoStart.IsEnabled(); // reflect actual state
    }

    private async void ChangeData_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "选择数据文件夹" };
        if (dlg.ShowDialog() != true)
            return;

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

        // Apply immediately (no restart): re-point the live data paths, reload config from the new
        // location, refresh this window's controls, and notify the launcher to reload + re-theme.
        if (App.Services.GetRequiredService<IAppDataPaths>() is MutableAppDataPaths paths)
            paths.SetRoot(dlg.FolderName);
        await _config.LoadAsync();
        LoadFromConfig();
        _events.RaiseSettingsChanged();

        var msg = usedExisting
            ? "已切换到目标文件夹中的现有数据。"
            : "已将当前数据移动到新文件夹。";
        MessageBox.Show(this, msg + "\n\n已立即生效。", "TanMenu", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void NumberOnly(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
