using System.Collections.Generic;
using System.Text.Json;
using TanMenu.Core.Models;
using TanMenu.Core.Serialization;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf.Views.Settings;

/// <summary>
/// The pending-changes buffer behind the settings surface's delayed commit (Round 8, Task 1). Every
/// property control on the surface reads and writes <see cref="Working"/> — a deep clone of the live
/// <see cref="ConfigService.Config"/> — and nothing reaches disk or the OS until
/// <see cref="SettingsCommit.ApplyAsync"/> runs. 取消 discards by re-seeding; there is nothing to undo,
/// because nothing was written.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the immediate-apply-with-rollback contract the six card groups used until 0.13
/// (<see cref="SettingsWrite.CommitAsync"/>, still live for <c>AppPage</c>'s 不再提示). The reason is
/// PUBLISHED: <c>win-property-win</c> — "All property window patterns except property inspectors use a
/// delayed commit, meaning that changes take effect only when users click OK or Apply."
/// </para>
/// <para>
/// <b><see cref="IsDirty"/> is COMPUTED, never flagged.</b> <see cref="Revalidate"/> walks the same
/// thirteen named properties <see cref="CopyOnto"/> writes and compares <see cref="Working"/> against the
/// live config, then ORs in the one non-config desire (<see cref="AutoStartDesired"/>). That is why the
/// cards call <see cref="Revalidate"/> rather than a <c>MarkDirty()</c>: flipping a check box and
/// flipping it back re-disables 应用, and there is no second source of truth to get out of step.
/// </para>
/// <para>
/// <b>The clone is a JSON round-trip through the source-generated context the file on disk goes
/// through</b> (<see cref="ConfigJsonContext"/>, the same <c>JsonTypeInfo&lt;AppConfig&gt;</c>
/// <see cref="ConfigService"/> serializes with). No reflection, so NativeAOT is unaffected, and a field
/// that survives a save survives a clone by construction — including every retired-but-still-round-tripped
/// field (<c>WindowWidth</c>, <c>NavRailExpanded</c>, <c>ButtonSize</c>, …). <c>ConfigService.CloneConfig()</c>
/// is deliberately NOT revived: its deletion note says a deep-clone helper on the live config service
/// "invites exactly the staged-edit workflow the redesign removed". Round 8 restores that workflow, but
/// the helper belongs to the settings surface, not to the config service.
/// </para>
/// <para>
/// <b>The object's identity is stable across a re-seed</b> — <see cref="Reseed"/> re-clones in place
/// rather than handing back a new instance. That is load-bearing, not a style choice: six card groups
/// and the commit row all hold this one draft, <see cref="ISettingsCard"/> has no member through which a
/// replacement could be pushed into them, and the commit row's 应用 button tracks
/// <see cref="DirtyChanged"/> — so a re-seed that replaced the object would orphan that subscription on
/// the very first 取消 and strand five cards on a stale clone.
/// </para>
/// </remarks>
internal sealed class SettingsDraft
{
    /// <summary>The live config service. Held rather than a snapshot of <c>Config</c>, because
    /// <c>LoadAsync</c>/<c>RestoreFromAsync</c> SWAP the instance (a data-folder change, a 恢复), and
    /// <see cref="Revalidate"/> must compare against whatever is live now.</summary>
    private readonly ConfigService _live;

    private readonly IAutoStartService _autoStart;

    /// <summary>The working copy every settings control reads and writes. Never handed to
    /// <see cref="ConfigService"/> as an object — see <see cref="CopyOnto"/>.</summary>
    public AppConfig Working { get; private set; }

    /// <summary>The pending 开机自启 desire, or null when the row has not been touched since the seed.
    /// This is the ONE field on the surface with no <see cref="AppConfig"/> property behind it: the OS
    /// (Run key, or the MSIX StartupTask) is the state. Written only by
    /// <c>BehaviorCard.OnAutoStartChanged</c>, read only by <c>BehaviorCard.ApplyAutoStartRow</c>, and
    /// cleared by <see cref="SettingsCommit.ApplyAutoStart"/>.</summary>
    public bool? AutoStartDesired { get; set; }

    /// <summary>What <see cref="IAutoStartService.IsEnabled"/> reported when this draft was seeded, or
    /// null if that read threw (locked hive, policy). <see cref="IsDirty"/> compares
    /// <see cref="AutoStartDesired"/> against this, so toggling the row and toggling it back re-disables
    /// 应用 exactly as it does for the twelve config-backed rows.</summary>
    public bool? AutoStartAtSeed { get; private set; }

    /// <summary>Whether anything on the surface differs from what is committed. Recomputed by
    /// <see cref="Revalidate"/>; never set directly.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Raised when <see cref="IsDirty"/> MOVES (not on every <see cref="Revalidate"/>). It
    /// exists because 应用 lives in a different visual tree from the cards that dirty the draft.</summary>
    public event Action? DirtyChanged;

    /// <summary>Raised after every settings edit, including edits made while the draft is already
    /// dirty. The single-window settings page uses this to debounce immediate persistence.</summary>
    public event Action? Changed;

    private SettingsDraft(ConfigService live, IAutoStartService autoStart)
    {
        _live = live;
        _autoStart = autoStart;
        Working = Clone(live.Config);
        AutoStartAtSeed = ReadAutoStart();
    }

    /// <summary>Seed a draft from the live config and the live OS autostart state.</summary>
    public static SettingsDraft From(ConfigService live, IAutoStartService autoStart) =>
        new(live, autoStart);

    /// <summary>
    /// Re-clone the live config, re-read the OS autostart state, drop any pending desire, and leave
    /// <see cref="IsDirty"/> false.
    /// </summary>
    /// <remarks>
    /// Four callers, and they are two different jobs. <b>Discard:</b> <c>OptionsWindow.OnClosed</c> —
    /// every close of the dialog, whichever button or × got there — and the three commands that rewrite
    /// <c>config.json</c> underneath the surface (更改数据文件夹, 重置为默认, 恢复), where a later 确定 would
    /// otherwise copy a stale draft over the file those commands just wrote. <b>Re-baseline:</b>
    /// <see cref="SettingsCommit.ApplyAsync"/>, after a successful save — a committed draft's seed has
    /// moved, and <see cref="AutoStartAtSeed"/> in particular must describe the OS state the commit just
    /// wrote, or the next toggle of 开机自启 in a dialog that stayed open compares against a value that
    /// is one commit out of date and never reads as dirty.
    /// </remarks>
    public void Reseed()
    {
        Working = Clone(_live.Config);
        AutoStartAtSeed = ReadAutoStart();
        AutoStartDesired = null;
        Revalidate();
    }

    /// <summary>Recompute <see cref="IsDirty"/> from scratch and raise <see cref="DirtyChanged"/> if it
    /// moved.</summary>
    public void Revalidate()
    {
        var dirty = DiffersFrom(_live.Config)
                    || (AutoStartDesired is { } desired && desired != AutoStartAtSeed);
        if (dirty != IsDirty)
        {
            IsDirty = dirty;
            DirtyChanged?.Invoke();
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Copy the thirteen named properties onto <paramref name="live"/> and return the undo actions that
    /// put the previous values back, in case the save that follows throws.
    /// </summary>
    /// <remarks>
    /// <b>A NAMED LIST, never a wholesale object swap, and that is a correctness requirement rather than
    /// a style preference.</b> <c>WindowHost.SavePlacement</c> writes <c>General.WindowMaximized</c> on
    /// the live config and debounces its own save, so swapping in a clone taken before that write would
    /// silently revert it; <c>General.TaskbarPinPromptDismissed</c> is written by <c>AppPage</c>'s
    /// 不再提示 on the same live object; and <c>SchemaVersion</c>, <c>ThemeName</c> and every retired
    /// field are <c>ConfigService.Normalize</c>'s to own, not this surface's. The tools list is matched
    /// per entry BY <c>Command</c> for the same reason <c>ToolsCard</c> already states — "never
    /// rebuilding or re-saving the whole DefaultTools list from a snapshot".
    /// </remarks>
    public IReadOnlyList<Action> CopyOnto(AppConfig live)
    {
        var undo = new List<Action>();
        var lg = live.General;
        var wg = Working.General;

        Capture(undo, lg.Language, wg.Language, v => lg.Language = v);
        Capture(undo, lg.Appearance, wg.Appearance, v => lg.Appearance = v);
        Capture(undo, lg.FontFamily, wg.FontFamily, v => lg.FontFamily = v);
        Capture(undo, lg.ColButtonCount, wg.ColButtonCount, v => lg.ColButtonCount = v);
        Capture(undo, lg.AutoClose, wg.AutoClose, v => lg.AutoClose = v);
        Capture(undo, lg.TopMost, wg.TopMost, v => lg.TopMost = v);
        Capture(undo, lg.HoverSoundEnabled, wg.HoverSoundEnabled, v => lg.HoverSoundEnabled = v);
        Capture(undo, lg.ClickSoundEnabled, wg.ClickSoundEnabled, v => lg.ClickSoundEnabled = v);
        Capture(undo, lg.GlobalHotkeyEnabled, wg.GlobalHotkeyEnabled, v => lg.GlobalHotkeyEnabled = v);
        Capture(undo, lg.GlobalHotkey, wg.GlobalHotkey, v => lg.GlobalHotkey = v);
        Capture(undo, lg.ShowDefaultTools, wg.ShowDefaultTools, v => lg.ShowDefaultTools = v);
        Capture(undo, live.RootFolder, Working.RootFolder, v => live.RootFolder = v);

        foreach (var (workingTool, liveTool) in PairTools(lg.DefaultTools))
        {
            var previous = liveTool.Show;
            liveTool.Show = workingTool.Show;
            undo.Add(() => liveTool.Show = previous);
        }

        return undo;
    }

    /// <summary>Assign <paramref name="next"/> and record the action that puts
    /// <paramref name="previous"/> back.</summary>
    private static void Capture<T>(List<Action> undo, T previous, T next, Action<T> assign)
    {
        assign(next);
        undo.Add(() => assign(previous));
    }

    /// <summary>The thirteen-property comparison <see cref="Revalidate"/> runs, in the same order and over
    /// the same properties <see cref="CopyOnto"/> writes.</summary>
    private bool DiffersFrom(AppConfig live)
    {
        var lg = live.General;
        var wg = Working.General;

        if (!string.Equals(lg.Language, wg.Language, StringComparison.Ordinal) ||
            !string.Equals(lg.Appearance, wg.Appearance, StringComparison.Ordinal) ||
            !string.Equals(lg.FontFamily, wg.FontFamily, StringComparison.Ordinal) ||
            lg.ColButtonCount != wg.ColButtonCount ||
            lg.AutoClose != wg.AutoClose ||
            lg.TopMost != wg.TopMost ||
            lg.HoverSoundEnabled != wg.HoverSoundEnabled ||
            lg.ClickSoundEnabled != wg.ClickSoundEnabled ||
            lg.GlobalHotkeyEnabled != wg.GlobalHotkeyEnabled ||
            !string.Equals(lg.GlobalHotkey, wg.GlobalHotkey, StringComparison.Ordinal) ||
            lg.ShowDefaultTools != wg.ShowDefaultTools ||
            !string.Equals(live.RootFolder, Working.RootFolder, StringComparison.Ordinal))
            return true;

        foreach (var (workingTool, liveTool) in PairTools(lg.DefaultTools))
        {
            if (workingTool.Show != liveTool.Show)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Pair each working tool with the live tool that carries the same <see cref="DefaultTool.Command"/>.
    /// Entries with no counterpart are skipped rather than created — the tools LIST itself is the user's
    /// (config.json is hand-editable), and this surface only ever commits <see cref="DefaultTool.Show"/>.
    /// A live entry is consumed once, so a config carrying the same command twice pairs one-for-one
    /// instead of collapsing onto the first match.
    /// </summary>
    private IEnumerable<(DefaultTool Working, DefaultTool Live)> PairTools(List<DefaultTool>? liveTools)
    {
        if (liveTools is null || Working.General.DefaultTools is not { } workingTools)
            yield break;

        var consumed = new bool[liveTools.Count];
        foreach (var workingTool in workingTools)
        {
            if (workingTool?.Command is not { } command)
                continue; // a null element or a null command — ToolsCard skips these too

            for (var i = 0; i < liveTools.Count; i++)
            {
                if (consumed[i] || liveTools[i]?.Command is not { } liveCommand)
                    continue;
                if (!string.Equals(liveCommand, command, StringComparison.Ordinal))
                    continue;

                consumed[i] = true;
                yield return (workingTool, liveTools[i]);
                break;
            }
        }
    }

    /// <summary>Read the OS autostart state, or null if the read throws. A locked hive or a policy
    /// refusal must not take the settings surface down, and null is honest about not knowing — see
    /// <c>BehaviorCard.ApplyAutoStartRow</c>, which leaves the row showing what it last showed.</summary>
    private bool? ReadAutoStart()
    {
        try
        {
            return _autoStart.IsEnabled();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "读取开机自启状态失败");
            return null;
        }
    }

    /// <summary>Deep-clone through the on-disk serializer. Source-generated, so NativeAOT-safe.</summary>
    private static AppConfig Clone(AppConfig source)
    {
        var type = ConfigJsonContext.Default.AppConfig;
        var clone = JsonSerializer.Deserialize(JsonSerializer.Serialize(source, type), type)!;

        // The live config is Normalize()d, so these are already non-null on it; the clone re-asserts
        // them only because System.Text.Json can leave an omitted key at its initializer and a
        // hand-edited file can carry an explicit null.
        clone.General ??= new GeneralConfig();
        clone.Folders ??= [];
        clone.General.DefaultTools ??= [];
        return clone;
    }
}
