using Avalonia;
using Avalonia.Controls;
using TanMenu.Core.Services;
using TanMenu.Wpf.Dialogs;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf.Views.Settings;

/// <summary>
/// 确定 / 应用 — the one place a settings change reaches disk or the OS under Round 8's delayed-commit
/// model. Everything before this ran against <see cref="SettingsDraft.Working"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The config write is atomic; the OS effects are not.</b> <c>ConfigService.SaveAsync</c> is one
/// serialize plus one <c>File.Replace</c> swap, so all eleven properties land together or none of them
/// do. Nothing downstream has that property: the autostart write, the global-hotkey registration and the
/// theme repaint are separate calls that can each fail on their own. <b>The config is the record and the
/// OS effects are consequences of it</b> — once <see cref="ConfigService.SaveAsync"/> returns, the commit
/// SUCCEEDED, 应用 goes disabled, and a later OS effect failing produces a notification rather than a
/// rollback. <c>GlobalHotkeyService</c> already refuses to roll config back in its own words ("a
/// transient conflict at startup must never permanently disable a working hotkey"), and three fields
/// committed with the fourth's OS effect failing is a committed state with a warning, not a half-commit.
/// </para>
/// <para>
/// Of the SettingsChanged subscribers only <c>GlobalHotkeyService.Apply</c> can both fail and want a
/// dialog; it raises <c>RegistrationFailed</c> → <c>App.WarnHotkeyRegistrationFailedAsync</c>, which is
/// <c>Dispatcher.UIThread.Post</c>ed and therefore lands after this method returns. The autostart write
/// below is logged and never dialogued; <c>AppTheme.Apply</c>, <c>WindowHost.ApplyTopmost</c>,
/// <c>TrayService.ApplyMenuText</c> and the view-model rebuild have no failure path that wants one.
/// </para>
/// </remarks>
internal static class SettingsCommit
{
    /// <summary>
    /// Commit the draft: copy the eleven named properties onto the live config, save once, apply the
    /// OS-side effects, then re-verify the whole surface against what actually landed.
    /// </summary>
    /// <returns>true if the config write succeeded (the OS effects may still have failed, and did not
    /// roll it back — see the class remarks).</returns>
    public static async Task<bool> ApplyAsync(
        SettingsDraft draft,
        ConfigService config,
        AppEvents events,
        IAutoStartService autoStart,
        IWindowHost host,
        Visual owner)
    {
        // 1. The eleven named properties, with the pre-copy values in hand.
        var undo = draft.CopyOnto(config.Config);

        // 2. One save for the whole surface, instead of one per control.
        try
        {
            await config.SaveAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to persist the settings commit");

            // Put the live config back exactly as it was. The DRAFT is deliberately left alone: it
            // still holds what the user asked for, the surface still shows it, and IsDirty is still
            // true — so 应用 stays enabled and the user can retry. That is how the invariant
            // SettingsWrite's comment defends ("the screen never silently shows a value that is not on
            // disk") survives the move to delayed commit: under immediate-apply the screen had to be
            // walked back to disk, whereas here the screen is a PENDING edit and says so.
            foreach (var restore in undo)
                restore();
            draft.Revalidate();

            // The dialog takes focus; keep the window alive for users who have 失焦自动隐藏 on.
            host.SuppressHide(true);
            try
            {
                await MessageBox.ShowAsync(TopLevel.GetTopLevel(owner) as Window,
                    AppLanguage.Format("SaveFailed", config.Config.General.Language, ex.Message),
                    "TanMenu", MessageBoxButtons.Ok, MessageBoxIcon.Error);
            }
            finally
            {
                host.SuppressHide(false);
            }
            return false;
        }

        // 3. The one OS effect that is not a config field.
        ApplyAutoStart(draft, autoStart);

        // 4. The light/dark repaint, moved here from AppearanceCard: delayed commit forbids the live
        //    preview that handler used to do ("changes take effect only when users click OK or Apply").
        if (Application.Current is { } app)
            AppTheme.Apply(app, draft.Working.General.Appearance);

        // 5. One SettingsChanged for the whole commit instead of one per control. The settings surface
        //    subscribes to it and re-populates every ISettingsCard from the draft — which now equals
        //    what is on disk — and, because step 3 cleared AutoStartDesired, 开机自启 re-reads the OS
        //    rather than re-displaying the click. Revalidate then takes 应用 to disabled.
        events.RaiseSettingsChanged();
        draft.Revalidate();
        return true;
    }

    /// <summary>
    /// The single place <see cref="IAutoStartService.SetEnabled"/> is called anywhere in the app after
    /// Round 8, Task 1. Separated from <see cref="ApplyAsync"/> so it can be exercised against a fake
    /// <see cref="IAutoStartService"/> without a window, a dispatcher, or the machine's real Run key.
    /// </summary>
    /// <remarks>
    /// The reason this is a try/catch that LOGS and never dialogues is inherited verbatim from the
    /// handler it moved out of: a failed Run-key write is not a settings-save failure and must not be
    /// reported as one. Clearing <see cref="SettingsDraft.AutoStartDesired"/> back to null on BOTH paths
    /// is what makes the caller's step 5 an unconditional re-verification against the OS instead of a
    /// re-display of the click — which is the only way to catch the packaged
    /// <c>StartupTaskAutoStartService</c> silently no-op-ing, since it swallows every exception
    /// internally and never inspects the <c>StartupTaskState</c> its own WinRT call returns, so a silent
    /// policy refusal looks exactly like success.
    /// </remarks>
    public static void ApplyAutoStart(SettingsDraft draft, IAutoStartService autoStart)
    {
        if (draft.AutoStartDesired is not { } desired)
            return; // the row was never touched — do not write the OS to re-state what it already says

        try
        {
            if (desired != autoStart.IsEnabled())
                autoStart.SetEnabled(desired);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "开机自启注册表/计划任务写入失败");
        }
        finally
        {
            draft.AutoStartDesired = null;
        }
    }
}
