using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf;

public partial class SettingsWindow : Window
{
    private static readonly (string Key, string Label)[] Themes =
    {
        ("Win31", "Windows 3.1"),
        ("Win7", "Windows 7"),
        ("ModernRetro", "Modern Retro"),
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
            var ico = Path.Combine(AppContext.BaseDirectory, "wwwroot", "app.ico");
            if (File.Exists(ico))
                Icon = BitmapFrame.Create(new Uri(ico));
        }
        catch { /* icon optional */ }

        LoadFromConfig();
    }

    private void LoadFromConfig()
    {
        var g = _config.Config.General;
        RefreshFolders();

        ThemeCombo.ItemsSource = Themes.Select(t => t.Label).ToList();
        var idx = Array.FindIndex(Themes, t => t.Key == g.ThemeName);
        ThemeCombo.SelectedIndex = idx < 0 ? 0 : idx;

        ColCount.Text = g.ColButtonCount.ToString();
        AutoCloseCb.IsChecked = g.AutoClose;
        TopMostCb.IsChecked = g.TopMost;
        TaskbarCb.IsChecked = g.ShowInTaskbar;
        AutoStartCb.IsChecked = _autoStart.IsEnabled();

        _loaded = true;
    }

    private void RefreshFolders()
    {
        FolderList.ItemsSource = null;
        FolderList.ItemsSource = _config.Config.Folders.ToList();
    }

    private async void Persist()
    {
        if (!_loaded)
            return;
        await _config.SaveAsync();
        _events.RaiseSettingsChanged();
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "选择文件夹" };
        if (dlg.ShowDialog() == true && !_config.Config.Folders.Contains(dlg.FolderName))
        {
            _config.Config.Folders.Add(dlg.FolderName);
            RefreshFolders();
            Persist();
        }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FolderList.SelectedItem is string f)
        {
            _config.Config.Folders.Remove(f);
            RefreshFolders();
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

    private void NumberOnly(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
