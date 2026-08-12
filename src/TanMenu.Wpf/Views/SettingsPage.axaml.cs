using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;
using TanMenu.Wpf.Views.Settings;

namespace TanMenu.Wpf.Views;

/// <summary>
/// Full-window Windows 11 settings page. Changes are persisted shortly after each interaction; there
/// is deliberately no OK, Cancel, or Apply command row.
/// </summary>
public partial class SettingsPage : UserControl, IDisposable
{
    private readonly SettingsDraft _draft;
    private readonly ConfigService _config;
    private readonly AppEvents _events;
    private readonly IAutoStartService _autoStart;
    private readonly IWindowHost _host;
    private readonly DispatcherTimer _saveTimer;
    private bool _isCommitting;
    private bool _disposed;

    public SettingsPage()
    {
        InitializeComponent();

        _draft = App.Services.GetRequiredService<SettingsDraft>();
        _config = App.Services.GetRequiredService<ConfigService>();
        _events = App.Services.GetRequiredService<AppEvents>();
        _autoStart = App.Services.GetRequiredService<IAutoStartService>();
        _host = App.Services.GetRequiredService<IWindowHost>();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveTimer.Tick += OnSaveTimerTick;
        _draft.Changed += OnDraftChanged;
        _events.SettingsChanged += ApplyText;
        ApplyText();
    }

    public async Task FlushAsync()
    {
        _saveTimer.Stop();
        if (_draft.IsDirty)
            await CommitAsync();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _saveTimer.Stop();
        _saveTimer.Tick -= OnSaveTimerTick;
        _draft.Changed -= OnDraftChanged;
        _events.SettingsChanged -= ApplyText;
    }

    private void ApplyText()
    {
        var lang = _config.Config.General.Language;
        AppearanceHeading.Text = AppLanguage.Text("Appearance", lang);
        BehaviorHeading.Text = AppLanguage.Text("Behavior", lang);
        FoldersHeading.Text = AppLanguage.Text("Folders", lang);
        ToolsHeading.Text = AppLanguage.Text("CommonTools", lang);
        UpdatesHeading.Text = AppLanguage.Text("Updates", lang);
        AboutHeading.Text = AppLanguage.Text("About", lang);

        AppearanceSettings.ReloadFromConfig();
        BehaviorSettings.ReloadFromConfig();
        FolderSettings.ReloadFromConfig();
        ToolSettings.ReloadFromConfig();
        UpdateSettings.ReloadFromConfig();
        AboutSettings.ReloadFromConfig();
    }

    private void OnDraftChanged()
    {
        if (_disposed || _isCommitting || !_draft.IsDirty)
            return;

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private async void OnSaveTimerTick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        try
        {
            await CommitAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "自动保存设置失败");
        }
    }

    private async Task CommitAsync()
    {
        if (_isCommitting || !_draft.IsDirty)
            return;

        _isCommitting = true;
        IsEnabled = false;
        try
        {
            await SettingsCommit.ApplyAsync(_draft, _config, _events, _autoStart, _host, this);
        }
        finally
        {
            IsEnabled = true;
            _isCommitting = false;
        }
    }
}
