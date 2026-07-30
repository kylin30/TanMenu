using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Dialogs;

public enum MessageBoxButtons
{
    Ok,
    OkCancel,
    YesNo,
    YesNoCancel,
}

public enum MessageBoxIcon
{
    None,
    Info,
    Question,
    Warning,
    Error,
}

public enum MessageBoxResult
{
    Ok,
    Cancel,
    Yes,
    No,
}

/// <summary>
/// The app-wide replacement for WPF's <c>MessageBox</c>, which Avalonia does not provide. Every call
/// is asynchronous because an Avalonia modal cannot block the calling thread — callers that used to
/// read a result inline are async now.
/// </summary>
/// <remarks>
/// Callers are responsible for the hide-suppression dance: wrap every prompt in
/// <c>IWindowHost.SuppressHide(true)</c> … <c>finally SuppressHide(false)</c>, or the launcher hides
/// itself the moment the dialog takes focus and the dialog appears detached from the menu.
/// </remarks>
public static class MessageBox
{
    public static Task<MessageBoxResult> ShowAsync(Window? owner, string message, string title,
        MessageBoxButtons buttons = MessageBoxButtons.Ok, MessageBoxIcon icon = MessageBoxIcon.None)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return ShowCoreAsync(owner, message, title, buttons, icon);

        // Callable from any thread, like the WPF MessageBox it replaces.
        var marshalled = new TaskCompletionSource<MessageBoxResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                marshalled.TrySetResult(await ShowCoreAsync(owner, message, title, buttons, icon));
            }
            catch (Exception ex)
            {
                marshalled.TrySetException(ex);
            }
        });
        return marshalled.Task;
    }

    private static async Task<MessageBoxResult> ShowCoreAsync(Window? owner, string message, string title,
        MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        var window = new MessageBoxWindow();
        window.Configure(message, title, buttons, icon, ResolveLanguage());

        // A HIDDEN owner is treated as no owner. Avalonia's ShowDialog throws
        // InvalidOperationException("Cannot show window with non-visible owner."), and the launcher's
        // resting state IS hidden (hide-on-blur, General.AutoClose defaults true). Callers routinely
        // pass IWindowHost.Window — e.g. the hotkey-registration warning raised from the settings
        // window opened via the tray, when the launcher was never shown. WPF's owner overload
        // tolerated a hidden owner, so without this the warning silently disappeared into a discarded
        // Task and the user was left believing a rejected hotkey had bound.
        var effectiveOwner = owner is { IsVisible: true } ? owner : null;

        window.WindowStartupLocation = effectiveOwner is null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.CenterOwner;

        if (effectiveOwner is not null)
            return await window.ShowDialog<MessageBoxResult>(effectiveOwner);

        // No usable owner: either the fatal-startup path (before the launcher exists) or a hidden
        // launcher. There is nothing to be modal to, so show it standalone and wait for it to close.
        var closed = new TaskCompletionSource<MessageBoxResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult(window.Result);
        window.Show();
        return await closed.Task;
    }

    /// <summary>Dialog language follows the same config value everything else does. Resolved
    /// defensively: this is also used from the fatal-startup path, where the container may not exist
    /// yet — a null language makes AppLanguage fall back to the UI culture.</summary>
    private static string? ResolveLanguage()
    {
        try
        {
            return App.Services?.GetService<ConfigService>()?.Config.General.Language;
        }
        catch
        {
            return null;
        }
    }
}
