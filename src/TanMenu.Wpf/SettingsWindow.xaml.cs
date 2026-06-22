using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using TanMenu.Core.Infrastructure;
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

    // Suggested fonts (the combo is editable, so any installed family can also be typed).
    // "Pixel" (fusion-pixel) is bundled via app.css @font-face and covers Chinese; the other
    // entries are common system fonts. (Press Start 2P is omitted — it's Latin-only and would
    // leave the app's Chinese labels in a mismatched fallback.)
    private static readonly string[] FontPresets =
    {
        DefaultFontLabel,
        "Pixel",
        "Microsoft YaHei",
        "SimSun",
        "SimHei",
        "Segoe UI",
        "Tahoma",
    };

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

        FontCombo.ItemsSource = FontPresets;
        FontCombo.Text = string.IsNullOrWhiteSpace(g.FontFamily) ? DefaultFontLabel : g.FontFamily;

        ColCount.Text = g.ColButtonCount.ToString();
        AutoCloseCb.IsChecked = g.AutoClose;
        TopMostCb.IsChecked = g.TopMost;
        TaskbarCb.IsChecked = g.ShowInTaskbar;
        AutoStartCb.IsChecked = _autoStart.IsEnabled();

        _loaded = true;
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

    private void Font_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFont();
    private void Font_LostFocus(object sender, RoutedEventArgs e) => ApplyFont();

    private void ApplyFont()
    {
        if (!_loaded)
            return;
        var text = (FontCombo.Text ?? string.Empty).Trim();
        if (text == DefaultFontLabel)
            text = string.Empty;
        text = FontUtil.Sanitize(text); // user free-text → strip CSS-breaking characters
        if (text == _config.Config.General.FontFamily)
            return; // no change
        _config.Config.General.FontFamily = text;
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
