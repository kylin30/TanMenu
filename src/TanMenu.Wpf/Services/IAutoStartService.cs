using Microsoft.Win32;
using TanMenu.Core.Infrastructure;

namespace TanMenu.Wpf.Services;

/// <summary>Launch-at-login toggle. One of the three implementations below is selected at startup by
/// distribution mode: portable, packaged (MSIX), or plain unpackaged.</summary>
public interface IAutoStartService
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}

/// <summary>
/// Plain unpackaged autostart via the per-user Run key. (Packaged builds use
/// <see cref="StartupTaskAutoStartService"/>; portable builds use <see cref="PortableAutoStartService"/>.)
/// </summary>
public sealed class RegistryAutoStartService : IAutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // User-visible state that already exists on disk for released 0.9.x installs. Renaming it would
    // orphan every existing autostart entry: Windows would keep launching the old value at logon
    // while the settings checkbox read "off". It is also why the executable is still TanMenu.Wpf.exe
    // — IsEnabled compares the stored path against Environment.ProcessPath (see the csproj comment).
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

/// <summary>
/// Portable distributions intentionally never create or remove a Run-key value. Moving or deleting
/// the extracted folder must be sufficient to remove the application without leaving app-owned
/// startup state behind.
/// </summary>
public sealed class PortableAutoStartService : IAutoStartService
{
    public bool IsEnabled() => false;
    public void SetEnabled(bool enabled) { }
}

/// <summary>Packaged/MSIX autostart via the manifest-declared windows.startupTask extension.</summary>
/// <remarks>
/// The WinRT calls block on their <c>IAsyncOperation</c> (<c>.AsTask().GetAwaiter().GetResult()</c>)
/// and are reached from the UI thread. That is safe only because StartupTask completes without
/// needing to re-enter the UI thread; do not add work to these paths that would.
/// </remarks>
public sealed class StartupTaskAutoStartService : IAutoStartService
{
    /// <summary>Must stay equal to <c>desktop:StartupTask/@TaskId</c> in
    /// <c>packaging/msix/AppxManifest.xml.in</c>, or the packaged toggle silently does nothing.</summary>
    public const string TaskId = "TanMenuStartupTask";

    public bool IsEnabled()
    {
        if (!PackageRuntime.HasPackageIdentity)
            return false;

        try
        {
            var task = Windows.ApplicationModel.StartupTask.GetAsync(TaskId).AsTask().GetAwaiter().GetResult();
            return task.State == Windows.ApplicationModel.StartupTaskState.Enabled;
        }
        catch
        {
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (!PackageRuntime.HasPackageIdentity)
            return;

        try
        {
            var task = Windows.ApplicationModel.StartupTask.GetAsync(TaskId).AsTask().GetAwaiter().GetResult();
            if (enabled)
            {
                if (task.State != Windows.ApplicationModel.StartupTaskState.Enabled)
                    task.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
            }
            else if (task.State == Windows.ApplicationModel.StartupTaskState.Enabled)
            {
                task.Disable();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to update packaged startup task {TaskId}", TaskId);
        }
    }
}
