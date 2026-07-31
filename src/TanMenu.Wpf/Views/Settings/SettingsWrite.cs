using Avalonia;
using Avalonia.Controls;
using TanMenu.Core.Services;
using TanMenu.Wpf.Dialogs;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf.Views.Settings;

/// <summary>Persist a settings change and announce it. On a failed write the caller's rollback
/// restores the previous value in memory and the card re-populates from it, so the screen never
/// silently shows a value that is not on disk — a settings page that lies about what was saved is
/// worse than the write error itself.</summary>
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
