using Microsoft.Win32;

namespace TanMenu.Wpf.Services;

/// <summary>Launch-at-login toggle. Abstracted so a packaged build can swap in a StartupTask impl.</summary>
public interface IAutoStartService
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}

/// <summary>
/// Unpackaged autostart via the per-user Run key. (For a future MSIX build, replace with a
/// <c>windows.startupTask</c>-backed implementation.)
/// </summary>
public sealed class RegistryAutoStartService : IAutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TanMenuWpf";

    private static string ExePath => Environment.ProcessPath ?? "";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string s
            && string.Equals(s.Trim('"'), ExePath, StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key!.SetValue(ValueName, $"\"{ExePath}\"");
        else
            key!.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
