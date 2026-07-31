namespace TanMenu.Wpf.Services;

public enum ShellPage
{
    App,
    Settings,
}

/// <summary>Page switching for the shell's two-entry rail. Exists so the tray menu and the app page's
/// overflow menu can reach the settings page without holding a reference to the window.</summary>
public interface IShellNavigator
{
    ShellPage Current { get; }
    void Navigate(ShellPage page);
}

public sealed class ShellNavigator : IShellNavigator
{
    private MainWindow? _window;

    public ShellPage Current { get; private set; } = ShellPage.App;

    /// <summary>Called once from MainWindow's constructor. Navigation before this is remembered and
    /// applied when the window attaches, so a tray click during startup is not lost.</summary>
    public void Attach(MainWindow window)
    {
        _window = window;
        window.ShowPage(Current);
    }

    public void Navigate(ShellPage page)
    {
        Current = page;
        _window?.ShowPage(page);
    }
}
