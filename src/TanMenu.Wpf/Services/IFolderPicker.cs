using Microsoft.Win32;

namespace TanMenu.Wpf.Services;

/// <summary>Pick a folder via the native WPF dialog (suppresses hide-on-blur while open).</summary>
public interface IFolderPicker
{
    string? PickFolder();
}

public sealed class WpfFolderPicker : IFolderPicker
{
    private readonly IWindowHost _host;

    public WpfFolderPicker(IWindowHost host) => _host = host;

    public string? PickFolder()
    {
        // Opening a modal dialog deactivates the launcher; suppress hide-on-blur meanwhile.
        _host.SuppressHide(true);
        try
        {
            var dlg = new OpenFolderDialog { Title = "选择文件夹" };
            return dlg.ShowDialog() == true ? dlg.FolderName : null;
        }
        finally
        {
            _host.SuppressHide(false);
        }
    }
}
