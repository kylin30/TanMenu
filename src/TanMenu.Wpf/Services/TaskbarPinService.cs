using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using TanMenu.Core.Infrastructure;
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
public sealed class TaskbarPinService
{
    public const string AppUserModelId = "TanSoft.TanMenu";

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
        IShellLinkW? link = null;
        try
        {
            var shellLinkType = Type.GetTypeFromCLSID(
                new Guid("00021401-0000-0000-C000-000000000046"), throwOnError: true)!;
            link = (IShellLinkW)Activator.CreateInstance(shellLinkType)!;
            ((IPersistFile)link).Load(shortcutPath, 0); // STGM_READ

            string? shortcutAppId = null;
            var key = PropertyKey.AppUserModelId;
            var hr = ((IPropertyStore)link).GetValue(ref key, out var value);
            if (hr >= 0)
            {
                try
                {
                    if (value.ValueType == 31 && value.Pointer != IntPtr.Zero)
                        shortcutAppId = Marshal.PtrToStringUni(value.Pointer);
                }
                finally
                {
                    PropVariantClear(ref value);
                }
            }

            // Pins created from older TanMenu shortcuts may not carry an AUMID. Only accept an
            // exact executable target match; the filename alone could point to an obsolete copy.
            var path = new StringBuilder(32768);
            link.GetPath(path, path.Capacity, IntPtr.Zero, 4); // SLGP_RAWPATH
            if (path.Length == 0)
                return false;

            var targetMatches = string.Equals(Path.GetFullPath(path.ToString()), expectedTarget,
                StringComparison.OrdinalIgnoreCase);
            var identityMatches = string.IsNullOrEmpty(shortcutAppId) ||
                                  string.Equals(shortcutAppId, AppUserModelId,
                                      StringComparison.Ordinal);
            return targetMatches && identityMatches;
        }
        finally
        {
            if (link is not null && Marshal.IsComObject(link))
                Marshal.FinalReleaseComObject(link);
        }
    }

    private static void CreateShortcut(string shortcutPath, string target)
    {
        IShellLinkW? link = null;
        try
        {
            var shellLinkType = Type.GetTypeFromCLSID(
                new Guid("00021401-0000-0000-C000-000000000046"), throwOnError: true)!;
            link = (IShellLinkW)Activator.CreateInstance(shellLinkType)!;
            link.SetPath(target);
            link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
            link.SetDescription("TanMenu");
            link.SetIconLocation(target, 0);

            var key = PropertyKey.AppUserModelId;
            // InitPropVariantFromString is a C++ header-only helper, not a DLL export. Build the
            // equivalent VT_LPWSTR value here; PropVariantClear releases the CoTaskMem buffer.
            var value = new PropVariant
            {
                ValueType = 31, // VT_LPWSTR
                Pointer = Marshal.StringToCoTaskMemUni(AppUserModelId),
            };

            try
            {
                Marshal.ThrowExceptionForHR(((IPropertyStore)link).SetValue(ref key, ref value));
                Marshal.ThrowExceptionForHR(((IPropertyStore)link).Commit());
            }
            finally
            {
                PropVariantClear(ref value);
            }

            ((IPersistFile)link).Save(shortcutPath, true);
        }
        finally
        {
            if (link is not null && Marshal.IsComObject(link))
                Marshal.FinalReleaseComObject(link);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(ShellChangeNotifyEventId eventId,
        ShellChangeNotifyFlags flags, string item1, IntPtr item2);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant propVariant);

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file,
            int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder args, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder iconPath,
            int iconPathLength, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;

        public static PropertyKey AppUserModelId => new()
        {
            FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            PropertyId = 5,
        };
    }

    // PROPVARIANT's union is 16 bytes on 64-bit Windows; the string initialized by
    // InitPropVariantFromString occupies the pointer at offset 8 and is released by PropVariantClear.
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort ValueType;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public IntPtr Pointer;
        public int Value;
    }

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
