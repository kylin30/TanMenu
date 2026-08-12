using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Models;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;
using TanMenu.Wpf.Views.Settings;
using Xunit;

namespace TanMenu.Core.Tests.Settings;

/// <summary>
/// Round 8, Task 1's delayed-commit model. The draft is the one piece of the settings surface that can
/// be exercised without a window, and it carries the whole config contract: which properties a commit
/// writes, which it must provably leave alone, and what 取消 restores.
/// </summary>
/// <remarks>
/// <see cref="SettingsCommit.ApplyAutoStart"/> is covered here too, and deliberately: it is the ONE
/// place in the app that writes the OS autostart state after this task, and the only way to verify that
/// round trip without touching the machine's real
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> value is against a fake
/// <see cref="IAutoStartService"/>. These tests never construct a real one.
/// </remarks>
public class SettingsDraftTests
{
    // ── harness ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Records every call instead of touching the registry or the MSIX StartupTask.</summary>
    private sealed class FakeAutoStart : IAutoStartService
    {
        public bool State;
        public int Reads;
        public int Writes;
        public Exception? ThrowOnWrite;

        public bool IsEnabled()
        {
            Reads++;
            return State;
        }

        public void SetEnabled(bool enabled)
        {
            Writes++;
            if (ThrowOnWrite is not null)
                throw ThrowOnWrite;
            State = enabled;
        }
    }

    /// <summary>A ConfigService over a throwaway data root, never loaded — the draft only ever reads
    /// <c>Config</c>, which the constructor already populates with defaults. Nothing here is created on
    /// disk until something calls <c>SaveAsync</c>, which one test below does; that one takes the
    /// <paramref name="dataRoot"/> overload and deletes the tree afterwards.</summary>
    private static ConfigService NewConfig(out string dataRoot)
    {
        dataRoot = Path.Combine(Path.GetTempPath(), "TanMenuDraft_" + Path.GetRandomFileName());
        var paths = new AppDataPaths(Path.Combine(dataRoot, "Local"), Path.Combine(dataRoot, "Cache"));
        return new ConfigService(paths, NullLogger<ConfigService>.Instance);
    }

    private static ConfigService NewConfig() => NewConfig(out _);

    // ── the clone ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Clone_IsIndependentOfTheLiveConfig()
    {
        var config = NewConfig();
        config.Config.RootFolder = @"C:\Live";
        config.Config.General.Language = AppLanguage.ZhHans;
        config.Config.General.ColButtonCount = 7;
        config.Config.Folders.Add(@"C:\Live\One");

        var draft = SettingsDraft.From(config, new FakeAutoStart());

        Assert.NotSame(config.Config, draft.Working);
        Assert.NotSame(config.Config.General, draft.Working.General);
        Assert.NotSame(config.Config.Folders, draft.Working.Folders);
        Assert.NotSame(config.Config.General.DefaultTools, draft.Working.General.DefaultTools);
        Assert.NotSame(config.Config.General.DefaultTools[0], draft.Working.General.DefaultTools[0]);

        // Writing the draft must not reach the live config…
        draft.Working.RootFolder = @"C:\Draft";
        draft.Working.General.ColButtonCount = 21;
        draft.Working.General.DefaultTools[0].Show = !draft.Working.General.DefaultTools[0].Show;
        Assert.Equal(@"C:\Live", config.Config.RootFolder);
        Assert.Equal(7, config.Config.General.ColButtonCount);

        // …and writing the live config must not reach the draft.
        config.Config.General.Language = AppLanguage.EnUs;
        Assert.Equal(AppLanguage.ZhHans, draft.Working.General.Language);
    }

    [Fact]
    public void Clone_RoundTripsEveryRetiredField()
    {
        // The clone goes through the SAME source-generated JsonTypeInfo the file on disk goes through,
        // so "a field that survives a save survives a clone" is meant to hold by construction. These
        // are the fields AppConfig keeps deserializable-but-unread (see each one's own remarks); a
        // clone that dropped one would silently rewrite the user's file on the next commit.
        var config = NewConfig();
        var g = config.Config.General;
        g.WindowWidth = 1234.5;
        g.WindowHeight = 678.25;
        g.WindowLeft = -1920;
        g.WindowTop = 42;
        g.NavRailExpanded = true;
        g.ShowInTaskbar = true;
        g.ButtonSize = "Large";
        g.ThemeName = AppConfig.CanonicalTheme;
        g.SchemaVersion = AppConfig.CurrentSchemaVersion;
        g.WindowMaximized = true;
        g.TaskbarPinPromptDismissed = true;

        var w = SettingsDraft.From(config, new FakeAutoStart()).Working.General;

        Assert.Equal(1234.5, w.WindowWidth);
        Assert.Equal(678.25, w.WindowHeight);
        Assert.Equal(-1920, w.WindowLeft);
        Assert.Equal(42, w.WindowTop);
        Assert.True(w.NavRailExpanded);
        Assert.True(w.ShowInTaskbar);
        Assert.Equal("Large", w.ButtonSize);
        Assert.Equal(AppConfig.CanonicalTheme, w.ThemeName);
        Assert.Equal(AppConfig.CurrentSchemaVersion, w.SchemaVersion);
        Assert.True(w.WindowMaximized);
        Assert.True(w.TaskbarPinPromptDismissed);
    }

    // ── the commit list ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CopyOnto_TouchesOnlyTheNamedProperties()
    {
        var config = NewConfig();
        var live = config.Config;
        live.General.WindowMaximized = true;
        live.General.TaskbarPinPromptDismissed = true;
        live.General.SchemaVersion = AppConfig.CurrentSchemaVersion;
        live.General.NavRailExpanded = true;
        live.General.ShowInTaskbar = true;
        live.General.ButtonSize = "Large";
        live.General.WindowWidth = 1111;
        live.General.WindowHeight = 999;
        live.General.WindowLeft = -7;
        live.General.WindowTop = -8;
        live.General.ThemeName = AppConfig.CanonicalTheme;
        live.Folders.Add(@"C:\Live\Group");

        var draft = SettingsDraft.From(config, new FakeAutoStart());

        // Move every un-named field on the WORKING copy to a different value. A wholesale object swap,
        // or a CopyOnto that walked the object generically, would drag all of these across.
        var wg = draft.Working.General;
        wg.WindowMaximized = false;
        wg.TaskbarPinPromptDismissed = false;
        wg.SchemaVersion = 99;
        wg.NavRailExpanded = false;
        wg.ShowInTaskbar = false;
        wg.ButtonSize = "Small";
        wg.WindowWidth = 1;
        wg.WindowHeight = 2;
        wg.WindowLeft = 3;
        wg.WindowTop = 4;
        wg.ThemeName = "SomethingElse";
        draft.Working.Folders.Clear();

        // …and move every NAMED one, so the test also proves the thirteen do land.
        wg.Language = AppLanguage.EnUs;
        wg.Appearance = AppAppearance.Dark;
        wg.FontFamily = "Consolas";
        wg.ColButtonCount = 17;
        wg.AutoClose = true;
        wg.TopMost = true;
        wg.HoverSoundEnabled = false;
        wg.ClickSoundEnabled = false;
        wg.GlobalHotkeyEnabled = true;
        wg.GlobalHotkey = "Ctrl+Alt+T";
        wg.ShowDefaultTools = false;
        wg.DefaultTools[0].Show = false;
        draft.Working.RootFolder = @"C:\Draft\Root";

        draft.CopyOnto(live);

        // The thirteen.
        Assert.Equal(AppLanguage.EnUs, live.General.Language);
        Assert.Equal(AppAppearance.Dark, live.General.Appearance);
        Assert.Equal("Consolas", live.General.FontFamily);
        Assert.Equal(17, live.General.ColButtonCount);
        Assert.True(live.General.AutoClose);
        Assert.True(live.General.TopMost);
        Assert.False(live.General.HoverSoundEnabled);
        Assert.False(live.General.ClickSoundEnabled);
        Assert.True(live.General.GlobalHotkeyEnabled);
        Assert.Equal("Ctrl+Alt+T", live.General.GlobalHotkey);
        Assert.False(live.General.ShowDefaultTools);
        Assert.False(live.General.DefaultTools[0].Show);
        Assert.Equal(@"C:\Draft\Root", live.RootFolder);

        // Everything else, untouched. WindowMaximized is the one with a live concurrent writer —
        // WindowHost.SavePlacement — which is why a wholesale swap would be a real defect.
        Assert.True(live.General.WindowMaximized);
        Assert.True(live.General.TaskbarPinPromptDismissed);
        Assert.Equal(AppConfig.CurrentSchemaVersion, live.General.SchemaVersion);
        Assert.True(live.General.NavRailExpanded);
        Assert.True(live.General.ShowInTaskbar);
        Assert.Equal("Large", live.General.ButtonSize);
        Assert.Equal(1111, live.General.WindowWidth);
        Assert.Equal(999, live.General.WindowHeight);
        Assert.Equal(-7, live.General.WindowLeft);
        Assert.Equal(-8, live.General.WindowTop);
        Assert.Equal(AppConfig.CanonicalTheme, live.General.ThemeName);
        Assert.Equal(new[] { @"C:\Live\Group" }, live.Folders);
    }

    [Fact]
    public void CopyOnto_MatchesToolsByCommandNotByIndex()
    {
        var config = NewConfig();
        var live = config.Config;
        live.General.DefaultTools = new List<DefaultTool>
        {
            new() { Name = "计算器", Command = "calc.exe", Show = true },
            new() { Name = "记事本", Command = "notepad.exe", Show = true },
            new() { Name = "任务管理器", Command = "taskmgr.exe", Show = true },
        };

        var draft = SettingsDraft.From(config, new FakeAutoStart());

        // The user turns 计算器 off on the surface…
        var draftCalc = draft.Working.General.DefaultTools.Single(t => t.Command == "calc.exe");
        draftCalc.Show = false;

        // …and, meanwhile, config.json is edited underneath (a reorder plus a new first entry). The
        // whole reason ToolsCard commits one flag at a time is that this must not clobber anything.
        live.General.DefaultTools.Reverse();
        live.General.DefaultTools.Insert(0, new DefaultTool { Name = "画图", Command = "mspaint.exe", Show = true });

        draft.CopyOnto(live);

        Assert.False(live.General.DefaultTools.Single(t => t.Command == "calc.exe").Show);
        Assert.True(live.General.DefaultTools.Single(t => t.Command == "notepad.exe").Show);
        Assert.True(live.General.DefaultTools.Single(t => t.Command == "taskmgr.exe").Show);
        // An entry the draft never saw is left alone rather than dropped or defaulted.
        Assert.True(live.General.DefaultTools.Single(t => t.Command == "mspaint.exe").Show);
        Assert.Equal(4, live.General.DefaultTools.Count);
    }

    [Fact]
    public void CopyOnto_UndoActionsRestoreEveryNamedProperty()
    {
        // The failed-save path: SettingsCommit runs these when SaveAsync throws, so the live config
        // ends up exactly as it was while the draft keeps holding what the user asked for.
        var config = NewConfig();
        var live = config.Config;
        live.RootFolder = @"C:\Before";
        live.General.Language = AppLanguage.ZhHans;
        live.General.ColButtonCount = 10;
        live.General.GlobalHotkey = "Ctrl+Space";
        live.General.HoverSoundEnabled = true;
        live.General.ClickSoundEnabled = true;
        live.General.DefaultTools[0].Show = true;

        var draft = SettingsDraft.From(config, new FakeAutoStart());
        draft.Working.RootFolder = @"C:\After";
        draft.Working.General.Language = AppLanguage.EnUs;
        draft.Working.General.ColButtonCount = 3;
        draft.Working.General.GlobalHotkey = "";
        draft.Working.General.HoverSoundEnabled = false;
        draft.Working.General.ClickSoundEnabled = false;
        draft.Working.General.DefaultTools[0].Show = false;

        var undo = draft.CopyOnto(live);
        Assert.Equal(@"C:\After", live.RootFolder);

        foreach (var restore in undo)
            restore();

        Assert.Equal(@"C:\Before", live.RootFolder);
        Assert.Equal(AppLanguage.ZhHans, live.General.Language);
        Assert.Equal(10, live.General.ColButtonCount);
        Assert.Equal("Ctrl+Space", live.General.GlobalHotkey);
        Assert.True(live.General.HoverSoundEnabled);
        Assert.True(live.General.ClickSoundEnabled);
        Assert.True(live.General.DefaultTools[0].Show);

        // The draft is untouched by the undo — 应用 stays enabled and the user can retry.
        Assert.Equal(@"C:\After", draft.Working.RootFolder);
    }

    // ── IsDirty ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsDirty_IsComputedAndFlipsBackWhenAChangeIsUndone()
    {
        var config = NewConfig();
        config.Config.General.ColButtonCount = 10;
        var draft = SettingsDraft.From(config, new FakeAutoStart());
        var raised = 0;
        draft.DirtyChanged += () => raised++;

        draft.Revalidate();
        Assert.False(draft.IsDirty);
        Assert.Equal(0, raised); // it did not move, so nothing was announced

        draft.Working.General.ColButtonCount = 4;
        draft.Revalidate();
        Assert.True(draft.IsDirty);
        Assert.Equal(1, raised);

        draft.Revalidate();
        Assert.Equal(1, raised); // still dirty; DirtyChanged fires on the EDGE, not on every call

        draft.Working.General.ColButtonCount = 10;
        draft.Revalidate();
        Assert.False(draft.IsDirty);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void SoundSwitches_AreDraftedCommittedAndCanReturnToClean()
    {
        var config = NewConfig();
        var draft = SettingsDraft.From(config, new FakeAutoStart());

        draft.Working.General.HoverSoundEnabled = false;
        draft.Working.General.ClickSoundEnabled = false;
        draft.Revalidate();
        Assert.True(draft.IsDirty);

        draft.CopyOnto(config.Config);
        Assert.False(config.Config.General.HoverSoundEnabled);
        Assert.False(config.Config.General.ClickSoundEnabled);

        draft.Reseed();
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void AutoStartDesired_IsDirtyOnlyWhenItDiffersFromTheSeed()
    {
        var config = NewConfig();
        var autoStart = new FakeAutoStart { State = false };
        var draft = SettingsDraft.From(config, autoStart);
        Assert.False(draft.AutoStartAtSeed);

        draft.AutoStartDesired = false; // re-stating the seed is not a change
        draft.Revalidate();
        Assert.False(draft.IsDirty);

        draft.AutoStartDesired = true;
        draft.Revalidate();
        Assert.True(draft.IsDirty);

        draft.AutoStartDesired = false; // flipped back — exactly as for the twelve config-backed rows
        draft.Revalidate();
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void Reseed_DropsPendingChangesAndTheAutoStartDesire()
    {
        var config = NewConfig();
        config.Config.General.Language = AppLanguage.ZhHans;
        var autoStart = new FakeAutoStart { State = false };
        var draft = SettingsDraft.From(config, autoStart);

        draft.Working.General.Language = AppLanguage.EnUs;
        draft.AutoStartDesired = true;
        draft.Revalidate();
        Assert.True(draft.IsDirty);

        // 取消, and the three commands that rewrite config.json underneath the surface.
        autoStart.State = true; // …and the OS moved while the draft was open
        draft.Reseed();

        Assert.Equal(AppLanguage.ZhHans, draft.Working.General.Language);
        Assert.Null(draft.AutoStartDesired);
        Assert.True(draft.AutoStartAtSeed); // re-read, not remembered
        Assert.False(draft.IsDirty);
        Assert.Equal(0, autoStart.Writes); // 取消 has nothing to undo, because nothing was written
    }

    [Fact]
    public void Reseed_IsWhatMakesTheAutoStartSeedSurviveACommit()
    {
        // Round 8, Task 2's fix round. AutoStartAtSeed is the ONLY piece of the draft a commit moves
        // without touching the config: SettingsCommit.ApplyAutoStart writes the OS and clears the
        // desire, and nothing in the draft learns that the OS is now somewhere else. Left that way, the
        // NEXT toggle of 开机自启 compares against a seed one commit out of date and reads as clean —
        // 应用 disabled over a real pending edit, and 取消 discarding it without a prompt.
        var config = NewConfig();
        var autoStart = new FakeAutoStart { State = false };
        var draft = SettingsDraft.From(config, autoStart);
        Assert.False(draft.AutoStartAtSeed);

        draft.AutoStartDesired = true;
        draft.Revalidate();
        Assert.True(draft.IsDirty);

        SettingsCommit.ApplyAutoStart(draft, autoStart);
        Assert.True(autoStart.State); // the OS moved…
        Assert.False(draft.AutoStartAtSeed); // …and the seed did not

        // The defect, stated rather than implied: with the seed stale, turning the row back off is
        // invisible to IsDirty.
        draft.AutoStartDesired = false;
        draft.Revalidate();
        Assert.False(draft.IsDirty);

        // Re-seeding is the whole fix, and it is what SettingsCommit.ApplyAsync does after a successful
        // save (covering 应用, which leaves the dialog open) and what OptionsWindow.OnClosed does on
        // every close (covering 确定, 取消 and the title-bar ×).
        draft.Reseed();
        Assert.True(draft.AutoStartAtSeed);
        Assert.Null(draft.AutoStartDesired);
        Assert.False(draft.IsDirty);

        draft.AutoStartDesired = false;
        draft.Revalidate();
        Assert.True(draft.IsDirty);
    }

    // ── the one non-config field ─────────────────────────────────────────────────────────────────

    [Fact]
    public void CopyOnto_NeverWritesTheAutoStartState()
    {
        var config = NewConfig();
        var autoStart = new FakeAutoStart { State = false };

        var draft = SettingsDraft.From(config, autoStart);
        draft.AutoStartDesired = true;
        draft.Revalidate();
        draft.CopyOnto(config.Config);

        Assert.Equal(0, autoStart.Writes);
        Assert.False(autoStart.State);
        Assert.True(draft.AutoStartDesired); // still pending — the write belongs to SettingsCommit
    }

    [Fact]
    public void ApplyAutoStart_WritesTheDesireOnceAndThenClearsIt()
    {
        // The round trip the design's verification step 6 asks for, against a fake rather than the
        // machine's real Run key: buffered on the click, written on the commit, and re-verified against
        // the OS afterwards because the desire is gone.
        var config = NewConfig();
        var autoStart = new FakeAutoStart { State = false };
        var draft = SettingsDraft.From(config, autoStart);

        draft.AutoStartDesired = true;
        draft.Revalidate();
        Assert.True(draft.IsDirty);

        SettingsCommit.ApplyAutoStart(draft, autoStart);

        Assert.Equal(1, autoStart.Writes);
        Assert.True(autoStart.State);
        Assert.Null(draft.AutoStartDesired); // ApplyAutoStartRow now falls through to the OS
        draft.Revalidate();
        Assert.False(draft.IsDirty);

        // A second commit with nothing pending must not re-write the OS.
        SettingsCommit.ApplyAutoStart(draft, autoStart);
        Assert.Equal(1, autoStart.Writes);
    }

    [Fact]
    public void ApplyAutoStart_DoesNotWriteWhenTheOsAlreadyAgrees()
    {
        var config = NewConfig();
        var autoStart = new FakeAutoStart { State = true };
        var draft = SettingsDraft.From(config, autoStart);

        // Toggled off and back on before the commit: the desire is non-null but matches the OS.
        draft.AutoStartDesired = true;
        SettingsCommit.ApplyAutoStart(draft, autoStart);

        Assert.Equal(0, autoStart.Writes);
        Assert.True(autoStart.State);
        Assert.Null(draft.AutoStartDesired);
    }

    /// <summary>
    /// The whole of <see cref="SettingsCommit.ApplyAsync"/>'s success path, driven for real, because the
    /// re-seed it grew in Round 8, Task 2's fix round is not observable from any of the pieces on their
    /// own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>host</c> and <c>owner</c> are passed null DELIBERATELY: both are touched only inside the
    /// <c>catch</c> around <c>SaveAsync</c> — <c>host.SuppressHide</c> and
    /// <c>TopLevel.GetTopLevel(owner)</c> bracket the failure message box — and the save here cannot
    /// fail. A null reaching either of them would therefore be a real regression in the failure path's
    /// bracketing, which is worth catching. <c>Application.Current</c> is null in a test process, so
    /// step 4's <c>AppTheme.Apply</c> is skipped by its own guard.
    /// </para>
    /// <para>
    /// This is the ONE test in this file that writes to disk. It writes only inside its own randomly
    /// named directory under the temp path and deletes the tree in a finally.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ApplyAsync_ReseedsTheDraftOntoWhatItJustCommitted()
    {
        var config = NewConfig(out var dataRoot);
        try
        {
            config.Config.General.ColButtonCount = 10;
            var autoStart = new FakeAutoStart { State = false };
            var draft = SettingsDraft.From(config, autoStart);
            var events = new AppEvents();
            var raised = 0;
            events.SettingsChanged += () => raised++;

            draft.Working.General.ColButtonCount = 4;
            draft.AutoStartDesired = true;
            draft.Revalidate();
            Assert.True(draft.IsDirty);

            var committed = await SettingsCommit.ApplyAsync(draft, config, events, autoStart, null!, null!);

            Assert.True(committed);
            Assert.Equal(1, raised);
            Assert.Equal(4, config.Config.General.ColButtonCount);
            Assert.True(File.Exists(Path.Combine(dataRoot, "Local", "config.json")));

            // The draft is re-seeded, not merely re-validated: Working is a NEW clone of what landed…
            Assert.Equal(4, draft.Working.General.ColButtonCount);
            Assert.NotSame(config.Config, draft.Working);
            Assert.False(draft.IsDirty);

            // …and the autostart seed describes the OS the commit just wrote, which is the half that was
            // wrong. Toggling the row back must now read as a change — before the re-seed the seed still
            // said false, so this comparison said "no change" and 应用 stayed disabled.
            Assert.True(draft.AutoStartAtSeed);
            Assert.Null(draft.AutoStartDesired);
            draft.AutoStartDesired = false;
            draft.Revalidate();
            Assert.True(draft.IsDirty);
        }
        finally
        {
            if (Directory.Exists(dataRoot))
                Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public void ApplyAutoStart_ClearsTheDesireEvenWhenTheOsWriteThrows()
    {
        // A failing OS effect is logged and never dialogued, and it must NOT leave the desire pending —
        // otherwise ApplyAutoStartRow would keep re-displaying the click instead of the OS truth, which
        // is exactly the re-verification the packaged StartupTaskAutoStartService needs.
        var config = NewConfig();
        var autoStart = new FakeAutoStart { State = false, ThrowOnWrite = new InvalidOperationException("policy") };
        var draft = SettingsDraft.From(config, autoStart);

        draft.AutoStartDesired = true;
        SettingsCommit.ApplyAutoStart(draft, autoStart);

        Assert.Equal(1, autoStart.Writes);
        Assert.False(autoStart.State); // the write did not take
        Assert.Null(draft.AutoStartDesired);

        draft.Revalidate();
        Assert.False(draft.IsDirty); // the config committed; the OS effect failing is not a half-commit
    }
}
