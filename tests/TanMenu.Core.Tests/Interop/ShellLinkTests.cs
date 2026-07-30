using System;
using System.IO;
using TanMenu.Core.Interop;
using Xunit;

namespace TanMenu.Core.Tests.Interop;

/// <summary>
/// Covers the source-generated COM shell-link wrapper that replaced the built-in <c>[ComImport]</c>
/// interop (built-in COM emits its stubs at run time and therefore cannot work under NativeAOT).
/// Resolving <c>.lnk</c> files is the app's core function, so a regression here empties the launcher.
/// </summary>
public class ShellLinkTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "TanMenuLnk_" + Path.GetRandomFileName());

    public ShellLinkTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp dir */ }
        GC.SuppressFinalize(this);
    }

    private string Lnk(string name) => Path.Combine(_dir, name + ".lnk");

    [Fact]
    public void SaveThenLoad_RoundTripsTargetPath()
    {
        var target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var path = Lnk("roundtrip");

        using (var link = ShellLink.Create())
        {
            link.SetPath(target);
            link.SetWorkingDirectory(Environment.SystemDirectory);
            link.SetDescription("TanMenu test");
            link.Save(path);
        }

        Assert.True(File.Exists(path));

        using var read = ShellLink.Create();
        read.Load(path);
        Assert.Equal(target, read.GetPath(), ignoreCase: true);
    }

    [Fact]
    public void GetPath_OnALinkWithNoTarget_ReturnsEmptyRatherThanGarbage()
    {
        var path = Lnk("empty");
        using (var link = ShellLink.Create())
        {
            // A link saved without SetPath has no stored target; GetPath must not surface
            // uninitialized buffer contents (the wrapper clears the buffer before the call).
            link.SetDescription("no target");
            link.Save(path);
        }

        using var read = ShellLink.Create();
        read.Load(path);
        Assert.Equal(string.Empty, read.GetPath());
    }

    [Fact]
    public void AppUserModelId_RoundTripsThroughThePropertyStore()
    {
        var target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var path = Lnk("aumid");
        const string appId = "TanSoft.TanMenu.Test";

        using (var link = ShellLink.Create())
        {
            link.SetPath(target);
            link.SetAppUserModelId(appId);
            link.Save(path);
        }

        using var read = ShellLink.Create();
        read.Load(path);
        Assert.Equal(appId, read.GetAppUserModelId());
    }

    [Fact]
    public void AppUserModelId_IsNullWhenTheLinkCarriesNone()
    {
        var path = Lnk("noaumid");
        using (var link = ShellLink.Create())
        {
            link.SetPath(Path.Combine(Environment.SystemDirectory, "notepad.exe"));
            link.Save(path);
        }

        using var read = ShellLink.Create();
        read.Load(path);
        Assert.Null(read.GetAppUserModelId());
    }

    [Fact]
    public void GetPath_WithRawPathFlag_SkipsEnvironmentExpansion()
    {
        // TaskbarPinService compares pinned shortcuts using SLGP_RAWPATH; make sure the flag is
        // actually plumbed through and a normal read still succeeds.
        var target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var path = Lnk("rawpath");
        using (var link = ShellLink.Create())
        {
            link.SetPath(target);
            link.Save(path);
        }

        using var read = ShellLink.Create();
        read.Load(path);
        Assert.Equal(target, read.GetPath(ShellLink.RawPath, capacity: 32768), ignoreCase: true);
    }

    [Fact]
    public void UseAfterDispose_Throws()
    {
        var link = ShellLink.Create();
        link.Dispose();
        Assert.Throws<ObjectDisposedException>(() => link.SetPath("x"));
    }
}
