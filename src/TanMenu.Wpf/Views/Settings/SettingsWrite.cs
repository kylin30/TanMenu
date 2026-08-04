using Avalonia;
using Avalonia.Controls;
using TanMenu.Core.Services;
using TanMenu.Wpf.Dialogs;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf.Views.Settings;

/// <summary>Persist a change immediately and announce it. On a failed write the caller's rollback
/// restores the previous value in memory and the caller re-populates from it, so the screen never
/// silently shows a value that is not on disk — a surface that lies about what was saved is worse than
/// the write error itself.</summary>
/// <remarks>
/// <b>This no longer governs the settings surface.</b> Round 8, Task 1 moved the six card groups to a
/// delayed commit — the Windows property-window model, PUBLISHED in <c>win-property-win</c>: "changes
/// take effect only when users click OK or Apply" — so their twelve <see cref="CommitAsync"/> call sites
/// and every <c>rollback:</c> lambda that went with them are gone, replaced by one
/// <see cref="SettingsCommit.ApplyAsync"/> per commit. There is exactly ONE caller left,
/// <c>AppPage</c>'s taskbar-pin InfoBar 不再提示, and immediate-apply is correct for it: dismissing a
/// notice is a launcher-page ACTION, not a property-window property, so there is no 确定 for it to wait
/// for and no 取消 that could take it back.
/// </remarks>
internal static class SettingsWrite
{
    public static async Task CommitAsync(
        ConfigService config,
        AppEvents events,
        IWindowHost host,
        Visual owner,
        Action rollback,
        Action reload)
    {
        try
        {
            await config.SaveAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to persist a settings change");
            rollback();
            reload();

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
            return;
        }

        events.RaiseSettingsChanged();
    }
}
