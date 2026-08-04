using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

/// <summary>
/// The Round 8 review's HIGH defect, pinned: <see cref="WindowHost.Hide"/> must do NOTHING while a
/// <see cref="WindowHost.SuppressHide"/> is in force.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the defect was.</b> Hiding the launcher does not close the modal dialog it owns — it takes
/// the dialog off screen with it. MEASURED on the live app: with the 选项 dialog up, one hide left
/// BOTH HWNDs alive and BOTH invisible, and the dialog then never reached <c>OnClosed</c>, so
/// <c>OptionsWindow.Current</c> went on pointing at it and every later 设置 activated a window nobody
/// could see, silently, for the rest of the session. All three of <c>Hide</c>'s callers —
/// <c>MainWindow.OnClosing</c>, <c>LauncherViewModel.HandleSearchEscape</c> and
/// <c>LauncherViewModel.LaunchItem</c> — are launcher gestures that cannot be meant while a dialog we
/// own is up, and none of them tested the count. The one caller that did test it, the
/// <c>Deactivated</c> handler, was the one that could not cause the defect.
/// </para>
/// <para>
/// <b>Why it is testable with no window at all.</b> <see cref="WindowHost.Hide"/>'s observable effect
/// on a detached host is its placement FLUSH: <c>SavePlacement</c> returns early without a window, but
/// <c>FlushPlacement</c> calls <c>ConfigService.SaveAsync</c> unconditionally, which writes
/// <c>config.json</c>. So "did the hide run at all" reads off the file system, which is exactly the
/// question the guard decides. A test that needed a real <c>Window</c> would need a running Avalonia
/// application, and this assembly has none.
/// </para>
/// </remarks>
public class WindowHostHideTests
{
    private static (WindowHost Host, string ConfigPath, string Root) NewHost()
    {
        var root = Path.Combine(Path.GetTempPath(), "TanMenuHide_" + Path.GetRandomFileName());
        var paths = new AppDataPaths(Path.Combine(root, "Local"), Path.Combine(root, "Cache"));
        paths.EnsureCreated();
        var config = new ConfigService(paths, NullLogger<ConfigService>.Instance);
        return (new WindowHost(config, new AppEvents()), paths.ConfigFilePath, root);
    }

    /// <summary>Wait for the fire-and-forget write <c>FlushPlacement</c> starts, or give up. A hide
    /// that was suppressed never starts one, so the negative case pays this wait in full — it is
    /// short on purpose.</summary>
    private static async Task<bool> WroteConfigAsync(string configPath)
    {
        for (var i = 0; i < 40; i++)
        {
            if (File.Exists(configPath))
                return true;
            await Task.Delay(25);
        }
        return false;
    }

    [Fact]
    public async Task Hide_WithNoSuppression_FlushesThePlacement()
    {
        var (host, configPath, root) = NewHost();
        try
        {
            host.Hide();
            Assert.True(await WroteConfigAsync(configPath),
                "An unsuppressed Hide() must still flush the placement — this is the control case, " +
                "and without it the suppressed case below would pass on a host that never worked.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Hide_WhileSuppressed_DoesNothing()
    {
        var (host, configPath, root) = NewHost();
        try
        {
            host.SuppressHide(true);
            host.Hide();

            Assert.False(await WroteConfigAsync(configPath),
                "Hide() ran while a SuppressHide was in force. SuppressHide means \"a modal we own has " +
                "the user's attention\", and the launcher is that modal's OWNER: hiding it does not " +
                "close the dialog, it strands it off screen with OptionsWindow.Current still pointing " +
                "at it, which kills 设置 for the rest of the session.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The count is a count, not a flag: an inner suppression released does not release the
    /// outer one. The 选项 dialog nests one of these for every message box and folder picker it
    /// raises, so this is the shape the real path takes rather than a hypothetical.</summary>
    [Fact]
    public async Task Hide_AfterNestedSuppressionsBalanceOut_RunsAgain()
    {
        var (host, configPath, root) = NewHost();
        try
        {
            host.SuppressHide(true);   // the dialog
            host.SuppressHide(true);   // a message box raised from inside it
            host.SuppressHide(false);  // the message box closes
            host.Hide();
            Assert.False(await WroteConfigAsync(configPath),
                "The outer suppression is still held, so Hide() must still be a no-op.");

            host.SuppressHide(false);  // the dialog closes
            host.Hide();
            Assert.True(await WroteConfigAsync(configPath),
                "With every suppression released, Hide() must work again.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
