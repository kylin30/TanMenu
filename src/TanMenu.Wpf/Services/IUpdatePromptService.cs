using System.Windows;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Services;

/// <summary>Native confirmations used by the Blazor launcher before downloading or applying an
/// update. Keeping these outside the component provides a real owner window and preserves the
/// launcher's hide-on-blur behavior while the modal dialog owns focus.</summary>
public interface IUpdatePromptService
{
    bool ConfirmDownload(string version, string? language);
    bool ConfirmRestart(string version, string? language);
}

public sealed class WpfUpdatePromptService(IWindowHost host) : IUpdatePromptService
{
    public bool ConfirmDownload(string version, string? language) => Confirm(
        AppLanguage.Format("UpdateDownloadPrompt", language, version),
        language,
        MessageBoxImage.Question);

    public bool ConfirmRestart(string version, string? language) => Confirm(
        AppLanguage.Format("UpdateRestartPrompt", language, version),
        language,
        MessageBoxImage.Information);

    private bool Confirm(string message, string? language, MessageBoxImage image)
    {
        var confirmed = false;
        Application.Current.Dispatcher.Invoke(() =>
        {
            // The owner temporarily deactivates while the native confirmation has focus. Prevent
            // auto-hide until it closes, otherwise the dialog would appear detached from the menu.
            host.SuppressHide(true);
            try
            {
                var owner = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                var result = owner is null
                    ? MessageBox.Show(
                        message,
                        AppLanguage.Text("UpdateTitle", language),
                        MessageBoxButton.YesNo,
                        image)
                    : MessageBox.Show(
                        owner,
                        message,
                        AppLanguage.Text("UpdateTitle", language),
                        MessageBoxButton.YesNo,
                        image);
                confirmed = result == MessageBoxResult.Yes;
            }
            finally
            {
                host.SuppressHide(false);
            }
        });
        return confirmed;
    }
}
