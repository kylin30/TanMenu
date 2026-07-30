using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace TanMenu.Core.Interop;

/// <summary>
/// Managed wrapper over the shell's ShellLink COM object (.lnk read/write) plus its property store.
///
/// This deliberately uses SOURCE-GENERATED COM (<c>[GeneratedComInterface]</c> + <c>ComWrappers</c>)
/// rather than the classic <c>[ComImport]</c> path: the runtime's built-in COM interop emits its
/// managed↔native stubs at run time, which NativeAOT cannot do (IL3052), so an AOT build of the old
/// code would fail to resolve any .lnk at all. The COM source generator does not support
/// <see cref="System.Text.StringBuilder"/> parameters either, so every string-out parameter below is
/// declared as a raw <c>char*</c> and marshalled here.
///
/// Instances are NOT thread-safe; create one per operation and dispose it.
/// </summary>
public sealed unsafe partial class ShellLink : IDisposable
{
    /// <summary>SLGP_RAWPATH — return the stored path without expanding environment variables.</summary>
    public const uint RawPath = 4;

    /// <summary>VT_LPWSTR — the PROPVARIANT type an AppUserModelID is stored as.</summary>
    private const ushort VtLpwstr = 31;

    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IidShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    private const uint ClsctxInprocServer = 1;

    private static readonly StrategyBasedComWrappers Wrappers = new();

    private object? _rcw;

    private ShellLink(object rcw) => _rcw = rcw;

    private object Rcw => _rcw ?? throw new ObjectDisposedException(nameof(ShellLink));

    /// <summary>Create a fresh in-process ShellLink COM object.</summary>
    public static ShellLink Create()
    {
        var hr = CoCreateInstance(
            in ClsidShellLink, IntPtr.Zero, ClsctxInprocServer, in IidShellLinkW, out var native);
        if (hr < 0 || native == IntPtr.Zero)
            Marshal.ThrowExceptionForHR(hr < 0 ? hr : unchecked((int)0x80004005));

        try
        {
            // UniqueInstance keeps this RCW out of the global COM object cache, so Dispose() actually
            // releases the underlying object instead of handing the same wrapper to the next caller.
            return new ShellLink(Wrappers.GetOrCreateObjectForComInstance(native, CreateObjectFlags.UniqueInstance));
        }
        finally
        {
            // GetOrCreateObjectForComInstance takes its own reference; drop the one CoCreateInstance gave us.
            Marshal.Release(native);
        }
    }

    /// <summary>Load an existing .lnk. <paramref name="mode"/> 0 = STGM_READ.</summary>
    public void Load(string path, uint mode = 0) => ((IPersistFile)Rcw).Load(path, mode);

    public void Save(string path, bool remember = true) => ((IPersistFile)Rcw).Save(path, remember);

    /// <summary>Read the link's stored target path. Returns an empty string when the link has none.</summary>
    /// <remarks>
    /// Deliberately does NOT call IShellLink::Resolve — that can show UI or hit the network for dead
    /// links, which would stall a folder scan.
    /// </remarks>
    public string GetPath(uint flags = 0, int capacity = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Span<char> buffer = capacity <= 512 ? stackalloc char[capacity] : new char[capacity];
        buffer.Clear(); // a failed GetPath leaves the buffer untouched — don't read stack garbage
        fixed (char* p = buffer)
        {
            ((IShellLinkW)Rcw).GetPath(p, capacity, IntPtr.Zero, flags);
            // The shell null-terminates; take everything up to the terminator.
            var end = buffer.IndexOf('\0');
            return new string(p, 0, end < 0 ? capacity : end);
        }
    }

    public void SetPath(string path) => ((IShellLinkW)Rcw).SetPath(path);
    public void SetWorkingDirectory(string dir) => ((IShellLinkW)Rcw).SetWorkingDirectory(dir);
    public void SetDescription(string description) => ((IShellLinkW)Rcw).SetDescription(description);
    public void SetIconLocation(string path, int index) => ((IShellLinkW)Rcw).SetIconLocation(path, index);

    /// <summary>Read the link's AppUserModelID property, or null when it carries none.</summary>
    public string? GetAppUserModelId()
    {
        var key = PropertyKey.AppUserModelId;
        var hr = ((IPropertyStore)Rcw).GetValue(ref key, out var value);
        if (hr < 0)
            return null;
        try
        {
            return value.ValueType == VtLpwstr && value.Pointer != IntPtr.Zero
                ? Marshal.PtrToStringUni(value.Pointer)
                : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    /// <summary>Stamp an AppUserModelID onto the link and commit the property store.</summary>
    public void SetAppUserModelId(string appId)
    {
        var key = PropertyKey.AppUserModelId;
        // InitPropVariantFromString is a header-only C++ helper, not a DLL export, so build the
        // equivalent VT_LPWSTR value by hand. PropVariantClear frees the CoTaskMem buffer.
        var value = new PropVariant
        {
            ValueType = VtLpwstr,
            Pointer = Marshal.StringToCoTaskMemUni(appId),
        };
        try
        {
            var store = (IPropertyStore)Rcw;
            Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value));
            Marshal.ThrowExceptionForHR(store.Commit());
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    public void Dispose()
    {
        if (_rcw is IDisposable disposable)
            disposable.Dispose();
        _rcw = null;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, in Guid riid, out IntPtr ppv);

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PropVariant propVariant);
}

[GeneratedComInterface]
[Guid("0000010c-0000-0000-C000-000000000046")]
internal partial interface IPersist
{
    void GetClassID(out Guid classId);
}

[GeneratedComInterface]
[Guid("0000010b-0000-0000-C000-000000000046")]
internal partial interface IPersistFile : IPersist
{
    [PreserveSig] int IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
    void Save([MarshalAs(UnmanagedType.LPWStr)] string? fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
}

/// <summary>Vtable order matters — every method must stay declared, in this exact order, even when unused.</summary>
[GeneratedComInterface]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal unsafe partial interface IShellLinkW
{
    void GetPath(char* file, int maxPath, IntPtr findData, uint flags);
    void GetIDList(out IntPtr idList);
    void SetIDList(IntPtr idList);
    void GetDescription(char* name, int maxName);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
    void GetWorkingDirectory(char* dir, int maxPath);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
    void GetArguments(char* args, int maxPath);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
    void GetHotkey(out short hotkey);
    void SetHotkey(short hotkey);
    void GetShowCmd(out int showCommand);
    void SetShowCmd(int showCommand);
    void GetIconLocation(char* iconPath, int iconPathLength, out int iconIndex);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
    void Resolve(IntPtr hwnd, uint flags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
}

[GeneratedComInterface]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal partial interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
    [PreserveSig] int Commit();
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;

    public static PropertyKey AppUserModelId => new()
    {
        FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        PropertyId = 5,
    };
}

/// <summary>PROPVARIANT's union is 16 bytes on 64-bit Windows; a VT_LPWSTR string lives in the
/// pointer at offset 8 and is released by PropVariantClear.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort ValueType;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public IntPtr Pointer;
    public int Value;
}
