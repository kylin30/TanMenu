using Avalonia.Threading;
using TanMenu.Core.Services;
using TanMenu.Wpf.Dialogs;

namespace TanMenu.Wpf.Services;

/// <summary>Native confirmations used by the launcher before downloading or applying an update.
/// Keeping these outside the view provides a real owner window and preserves the launcher's
/// hide-on-blur behavior while the modal dialog owns focus.</summary>
public interface IUpdatePromptService
{
    Task<bool> ConfirmDownloadAsync(string version, string? language);
    Task<bool> ConfirmRestartAsync(string version, string? language);
}

public sealed class UpdatePromptService(IWindowHost host) : IUpdatePromptService
{
    public Task<bool> ConfirmDownloadAsync(string version, string? language) => ConfirmAsync(
        AppLanguage.Format("UpdateDownloadPrompt", language, version),
        language,
        MessageBoxIcon.Question);

    public Task<bool> ConfirmRestartAsync(string version, string? language) => ConfirmAsync(
        AppLanguage.Format("UpdateRestartPrompt", language, version),
        language,
        MessageBoxIcon.Info);

    private Task<bool> ConfirmAsync(string message, string? language, MessageBoxIcon icon)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return ConfirmCoreAsync(message, language, icon);

        var marshalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                marshalled.TrySetResult(await ConfirmCoreAsync(message, language, icon));
            }
            catch (Exception ex)
            {
                marshalled.TrySetException(ex);
            }
        });
        return marshalled.Task;
    }

    private async Task<bool> ConfirmCoreAsync(string message, string? language, MessageBoxIcon icon)
    {
        // The owner temporarily deactivates while the confirmation has focus. Prevent auto-hide until
        // it closes, otherwise the dialog would appear detached from the menu.
        host.SuppressHide(true);
        try
        {
            var result = await MessageBox.ShowAsync(host.Window, message,
                AppLanguage.Text("UpdateTitle", language), MessageBoxButtons.YesNo, icon);
            return result == MessageBoxResult.Yes;
        }
        finally
        {
            host.SuppressHide(false);
        }
    }
}
