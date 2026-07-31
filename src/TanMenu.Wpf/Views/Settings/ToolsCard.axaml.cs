using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Models;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf.Views.Settings;

/// <summary>
/// 常用工具 card group: the master toggle for the built-in "常用工具" launcher category
/// (<see cref="GeneralConfig.ShowDefaultTools"/>), and which of its tools are included
/// (<see cref="GeneralConfig.DefaultTools"/>). Ported from <c>SettingsWindow</c>'s 常用工具 tab, minus
/// the staging: every handler here writes straight to <see cref="ConfigService.Config"/>, then hands
/// the save off to <see cref="SettingsWrite.CommitAsync"/>, which raises
/// <see cref="AppEvents.SettingsChanged"/> on success — <c>MenuService.BuildMenuAsync</c> rebuilds the
/// launcher's tools category from the newly-saved config — or rolls the field back and reports the
/// error on failure, instead of mutating a working clone for a later Apply.
/// </summary>
public partial class ToolsCard : UserControl, ISettingsCard
{
    private readonly ConfigService _config;
    private readonly AppEvents _events;
    private readonly IWindowHost _host;

    /// <summary>Guards <see cref="OnShowToolsChanged"/> against the value <see cref="ReloadFromConfig"/>
    /// assigns while it repopulates the toggle. Per-tool CheckBoxes need no such guard against
    /// IsCheckedChanged the way every other card's ToggleSwitch/ComboBox does: they are rebuilt from
    /// scratch on every reload (see <see cref="BuildToolCheckboxes"/>) rather than having a stable
    /// existing instance repopulated, and they wire OnToolChanged to Click, not IsCheckedChanged —
    /// Click never fires from a control's IsChecked being set programmatically, only from an actual
    /// user click, so a freshly constructed CheckBox cannot self-trigger a commit no matter when its
    /// initial IsChecked is set.</summary>
    private bool _loading;

    private string UiLanguage => _config.Config.General.Language;
    private string L(string key, params object?[] args) => AppLanguage.Format(key, UiLanguage, args);

    public ToolsCard()
    {
        InitializeComponent();
        _config = App.Services.GetRequiredService<ConfigService>();
        _events = App.Services.GetRequiredService<AppEvents>();
        _host = App.Services.GetRequiredService<IWindowHost>();
    }

    /// <summary>Populate every control from the live config. Sets a guard so
    /// <see cref="OnShowToolsChanged"/> does not write back the value it is being given, then rebuilds
    /// the per-tool checkbox list wholesale from <see cref="GeneralConfig.DefaultTools"/>.</summary>
    public void ReloadFromConfig()
    {
        _loading = true;
        try
        {
            var g = _config.Config.General;
            var lang = g.Language;

            GroupHead.Text = AppLanguage.Text("CommonTools", lang);

            ShowToolsTitle.Text = AppLanguage.Text("ShowCommonTools", lang);
            ShowToolsSub.Text = AppLanguage.Text("CommonToolsHint", lang);
            ShowToolsToggle.IsChecked = g.ShowDefaultTools;

            BuildToolCheckboxes(g.DefaultTools, lang);
            ToolsPanel.IsEnabled = g.ShowDefaultTools; // disabled, not cleared — see the XAML remark
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Rebuild the per-tool checkbox list from <paramref name="tools"/>, which
    /// <c>config.json</c> lets the user edit directly: it can be null (an old/hand-edited config with
    /// the key omitted), empty, contain a null element (<c>"defaultTools": [null, {...}]</c>), or an
    /// entry whose <see cref="DefaultTool.Command"/> is null/unresolvable — the source-generated JSON
    /// context does not enforce non-null on deserialization, so both a null element and a null
    /// <c>Command</c> are reachable here despite the non-nullable C# types. A null element or a null
    /// Command is skipped (logged once, not per-occurrence, to avoid flooding the log from one bad
    /// config) rather than rewriting the user's file — a missing checkbox for one malformed entry is
    /// recoverable; corrupting <c>config.json</c> underneath the user is not. An unresolved-but-non-null
    /// Command is still rendered and still a valid Show toggle to persist, exactly as before, even
    /// though MenuService can't launch it. Each CheckBox's Click handler is attached AFTER its initial
    /// IsChecked is set from config (ported verbatim from SettingsWindow.BuildToolCheckboxes) so
    /// construction can never look like a user click even in principle.</summary>
    private void BuildToolCheckboxes(List<DefaultTool>? tools, string? lang)
    {
        ToolsPanel.Children.Clear();
        var skipped = 0;
        foreach (var tool in tools ?? Enumerable.Empty<DefaultTool>())
        {
            if (tool is null || tool.Command is null)
            {
                skipped++;
                continue;
            }

            // Show the launch command (not just the name) so a tampered/roamed config that repoints an
            // innocuous-looking tool at an arbitrary command is visible before the user enables it.
            var displayName = AppLanguage.LocalizeToolName(tool.Command, tool.Name, lang);
            var cb = new CheckBox
            {
                Content = L("ToolCheckbox", displayName, tool.Command),
                IsChecked = tool.Show,
                Margin = new Thickness(0, 3, 0, 3),
                Tag = tool, // the LIVE DefaultTool instance this checkbox commits back to
            };
            ToolTip.SetTip(cb, L("ToolCommand", tool.Command));
            cb.Click += OnToolChanged;
            ToolsPanel.Children.Add(cb);
        }

        if (skipped > 0)
            Serilog.Log.Warning(
                "常用工具配置中有 {Count} 个条目缺少必要字段（null 元素或 command 为空），已跳过", skipped);
    }

    private void OnShowToolsChanged(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        if (_loading)
            return;

        var previous = _config.Config.General.ShowDefaultTools;
        var next = ShowToolsToggle.IsChecked == true;
        _config.Config.General.ShowDefaultTools = next;
        ToolsPanel.IsEnabled = next; // immediate UX feedback; ReloadFromConfig repeats this either way
        await SettingsWrite.CommitAsync(_config, _events, _host, this,
            rollback: () => _config.Config.General.ShowDefaultTools = previous,
            reload: ReloadFromConfig);
    });

    /// <summary>One tool's Show flag, committed on its own — never rebuilding or re-saving the whole
    /// DefaultTools list from a snapshot, so a concurrent edit to another entry (or to config.json
    /// itself, between saves) is never clobbered. The rollback below restores THIS tool's own previous
    /// value, captured in the closure, not a copy of the list taken at some earlier point.</summary>
    private void OnToolChanged(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        if (_loading)
            return;
        if (sender is not CheckBox { Tag: DefaultTool tool } cb)
            return;

        var previous = tool.Show;
        tool.Show = cb.IsChecked == true;
        await SettingsWrite.CommitAsync(_config, _events, _host, this,
            rollback: () => tool.Show = previous,
            reload: ReloadFromConfig);
    });

    /// <summary>Run an async change handler without letting a fault escape the Click/IsCheckedChanged
    /// event — same rationale as <c>SettingsWindow.RunSafely</c> / <c>BehaviorCard.RunSafely</c>:
    /// Avalonia has no WPF-style global DispatcherUnhandledException hook, so an escaping async-void
    /// fault would tear the process down, and logging is the floor.</summary>
    private static async void RunSafely(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Serilog.Log.Error(ex, "常用工具设置写入失败"); }
    }
}
