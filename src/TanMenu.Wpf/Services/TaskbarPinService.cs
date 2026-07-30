using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Interop;
using Windows.UI.Shell;

namespace TanMenu.Wpf.Services;

public enum TaskbarPinStatus
{
    Available,
    Pinned,
    NotAllowed,
    Unsupported,
    Failed,
}

public sealed record TaskbarPinState(TaskbarPinStatus Status, string? Error = null);

/// <summary>
/// Creates the Start-menu identity required by Windows and requests a user-approved permanent
/// taskbar pin. The portable shortcut targets Velopack's stable root launcher, not the replaceable
/// <c>current</c> application directory, so in-place updates do not break it.
/// </summary>
/// <remarks>
/// All .lnk work goes through <see cref="ShellLink"/> (source-generated COM in TanMenu.Core). The
/// old <c>Type.GetTypeFromCLSID</c> + <c>Activator.CreateInstance</c> + <c>[ComImport]</c> path is
/// unusable here: NativeAOT has no built-in COM, and it fails at RUN time with no build warning —
/// every pinned shortcut would have looked "not ours" and the pin prompt would have reappeared on
/// every single reveal.
///
/// The shell COM below runs on the caller's thread, which is the UI thread for every call site
/// (the pin prompt and the settings window). That thread is STA, which the shell requires; a
/// ComWrappers RCW performs raw vtable calls with NO apartment check, so calling any of this from a
/// pool thread would be undefined behaviour rather than a clean RPC_E_WRONG_THREAD.
/// </remarks>
public sealed partial class TaskbarPinService
{
    public const string AppUserModelId = "TanSoft.TanMenu";

    /// <summary>Buffer size for reading a shortcut's stored target — MAX_PATH is not enough for a
    /// long path, and the shell writes up to 32767 characters plus the terminator.</summary>
    private const int ShortcutPathCapacity = 32768;

    private readonly bool _isPackaged;
    private readonly Func<string> _launcherPath;

    public TaskbarPinService(bool isPackaged, Func<string> launcherPath)
    {
        _isPackaged = isPackaged;
        _launcherPath = launcherPath;
    }

    /// <summary>
    /// An unpackaged process and its Start-menu shortcut must share a stable AUMID so Windows can
    /// associate the running application with the pinned entry. Call before creating any windows.
    /// </summary>
    public static void InitializeProcessIdentity()
    {
        if (PackageRuntime.HasPackageIdentity)
            return;

        var hr = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    public async Task<TaskbarPinState> GetStateAsync()
    {
        try
        {
            // Windows 10 can expose TaskbarManager without allowing an unpackaged desktop app to
            // query it. A manually-created pin is still represented by a traditional .lnk, so
            // inspect that read-only as a compatibility fallback before calling the modern API.
            if (!_isPackaged && IsLegacyTaskbarShortcutPinned())
                return new(TaskbarPinStatus.Pinned);

            var manager = TaskbarManager.GetDefault();
            if (manager is null || !manager.IsSupported)
                return new(TaskbarPinStatus.Unsupported);

            if (await manager.IsCurrentAppPinnedAsync().AsTask())
                return new(TaskbarPinStatus.Pinned);

            // An unpackaged portable app intentionally does not create its Start entry until the
            // user presses the pin button. IsPinningAllowed can be false before that entry exists,
            // so keep the explicit action available and perform the definitive check afterwards.
            return !_isPackaged || manager.IsPinningAllowed
                ? new(TaskbarPinStatus.Available)
                : new(TaskbarPinStatus.NotAllowed);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not query taskbar pin state");
            return new(TaskbarPinStatus.Unsupported, ex.Message);
        }
    }

    /// <summary>
    /// Must be called from a foreground UI interaction. Windows displays its own confirmation; a
    /// successful return means the user approved it or the app was already pinned.
    /// </summary>
    public async Task<TaskbarPinState> RequestPinAsync()
    {
        try
        {
            if (!_isPackaged)
            {
                EnsureStartMenuShortcut();
                // Let Explorer observe the newly-created app-list entry before the pin request.
                await Task.Delay(150);
            }

            var manager = TaskbarManager.GetDefault();
            if (manager is null || !manager.IsSupported)
                return new(TaskbarPinStatus.Unsupported);

            if (await manager.IsCurrentAppPinnedAsync().AsTask())
                return new(TaskbarPinStatus.Pinned);

            if (!manager.IsPinningAllowed)
                return new(TaskbarPinStatus.NotAllowed);

            return await manager.RequestPinCurrentAppAsync().AsTask()
                ? new(TaskbarPinStatus.Pinned)
                : new(TaskbarPinStatus.Available);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not pin TanMenu to the taskbar");
            return new(TaskbarPinStatus.Failed, ex.Message);
        }
    }

    /// <summary>
    /// Windows 10 does not consistently allow unpackaged desktop apps to request a pin. Create the
    /// same Start-menu identity used by the modern API, then show it in Explorer so the user can
    /// use the supported right-click "Pin to taskbar" command. No taskbar files are modified here.
    /// </summary>
    public bool OpenManualPinLocation()
    {
        try
        {
            if (_isPackaged)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "shell:AppsFolder",
                    UseShellExecute = true,
                });
                return true;
            }

            var shortcutPath = EnsureStartMenuShortcut();
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{shortcutPath}\"",
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not open the TanMenu Start-menu shortcut");
            return false;
        }
    }

    /// <summary>Stable executable used by the Start-menu and taskbar shortcuts.</summary>
    public static string ResolveLauncherPath()
    {
        if (App.IsPortable)
        {
            var portableLauncher = Path.Combine(App.PortableDataRoot, "TanMenu.exe");
            if (File.Exists(portableLauncher))
                return portableLauncher;
        }

        return Environment.ProcessPath
               ?? Process.GetCurrentProcess().MainModule?.FileName
               ?? throw new InvalidOperationException("Unable to determine the TanMenu executable path.");
    }

    private string EnsureStartMenuShortcut()
    {
        var target = Path.GetFullPath(_launcherPath());
        if (!File.Exists(target))
            throw new FileNotFoundException("TanMenu launcher not found.", target);

        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (string.IsNullOrWhiteSpace(programs))
            throw new InvalidOperationException("Windows Start-menu folder is unavailable.");

        Directory.CreateDirectory(programs);
        var shortcutPath = Path.Combine(programs, "TanMenu.lnk");
        CreateShortcut(shortcutPath, target);

        // TaskbarManager requires the entry to be visible in the Start app list. Notify Explorer
        // immediately instead of waiting for its filesystem watcher to notice the new/updated link.
        SHChangeNotify(ShellChangeNotifyEventId.UpdateItem, ShellChangeNotifyFlags.PathW,
            shortcutPath, IntPtr.Zero);
        return shortcutPath;
    }

    private bool IsLegacyTaskbarShortcutPinned()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            return false;

        var pinnedFolder = Path.Combine(appData, "Microsoft", "Internet Explorer", "Quick Launch",
            "User Pinned", "TaskBar");
        if (!Directory.Exists(pinnedFolder))
            return false;

        var target = Path.GetFullPath(_launcherPath());
        foreach (var shortcutPath in Directory.EnumerateFiles(pinnedFolder, "*.lnk"))
        {
            try
            {
                if (ShortcutMatchesCurrentApp(shortcutPath, target))
                    return true;
            }
            catch (Exception ex)
            {
                // One stale or inaccessible pin must not prevent checking the remaining entries.
                Serilog.Log.Debug(ex, "Could not inspect pinned shortcut {Shortcut}", shortcutPath);
            }
        }

        return false;
    }

    private static bool ShortcutMatchesCurrentApp(string shortcutPath, string expectedTarget)
    {
        // using: the RCW is created with CreateObjectFlags.UniqueInstance, so disposing it actually
        // releases the underlying COM object — the AOT-safe replacement for FinalReleaseComObject.
        // It must run on the exception path too: a desktop with hundreds of pins is scanned per
        // reveal, and a leak per stale .lnk would accumulate for the life of the tray process.
        using var link = ShellLink.Create();
        link.Load(shortcutPath, 0); // STGM_READ

        var shortcutAppId = link.GetAppUserModelId();

        // Pins created from older TanMenu shortcuts may not carry an AUMID. Only accept an
        // exact executable target match; the filename alone could point to an obsolete copy.
        var path = link.GetPath(ShellLink.RawPath, ShortcutPathCapacity);
        if (path.Length == 0)
            return false;

        var targetMatches = string.Equals(Path.GetFullPath(path), expectedTarget,
            StringComparison.OrdinalIgnoreCase);
        var identityMatches = string.IsNullOrEmpty(shortcutAppId) ||
                              string.Equals(shortcutAppId, AppUserModelId,
                                  StringComparison.Ordinal);
        return targetMatches && identityMatches;
    }

    private static void CreateShortcut(string shortcutPath, string target)
    {
        using var link = ShellLink.Create();
        link.SetPath(target);
        link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
        link.SetDescription("TanMenu");
        link.SetIconLocation(target, 0);
        // Stamps the AUMID into the link's property store and commits it. Must match the AUMID the
        // process sets in InitializeProcessIdentity, or TaskbarManager cannot associate the two.
        link.SetAppUserModelId(AppUserModelId);
        link.Save(shortcutPath, true);
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string appId);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial void SHChangeNotify(ShellChangeNotifyEventId eventId,
        ShellChangeNotifyFlags flags, string item1, IntPtr item2);

    private enum ShellChangeNotifyEventId : uint
    {
        UpdateItem = 0x00002000,
    }

    [Flags]
    private enum ShellChangeNotifyFlags : uint
    {
        PathW = 0x0005,
    }
}
