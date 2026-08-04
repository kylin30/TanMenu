using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Themes;

/// <summary>
/// Guards the five things the Options dialog has to be that nothing at run time checks. Three are
/// about the markup — a Windows property sheet's commit row, a property window that does not scroll,
/// and a tab strip of five whose labels come from <c>AppLanguage</c> rather than from literals — and
/// two are about the code-behind: where the draft's re-seed hangs, and when the single-instance field
/// is claimed. Both of those were added in Round 8, Task 2's fix round, each with a defect behind it.
/// </summary>
/// <remarks>
/// <para>
/// SOURCE tests, like <see cref="ShellConstantTests"/> and <see cref="SettingsRowTests"/>, and for the
/// same reason those are: every failure below RENDERS, or worse, does nothing at all. A commit row in
/// the wrong order renders. A <c>ScrollViewer</c> inside a tab renders, and looks fine until the page
/// is one row taller than the window. A <c>TabItem</c> with a literal <c>Header</c> renders — in one
/// language. A re-seed on the wrong method and a single-instance field claimed too early both throw
/// nothing and both surface a whole dialog session later.
/// </para>
/// <para>
/// The third is the one that had a date on it, and the date has passed. Round 8, Task 6 took the
/// group box off every tab whose controls are one homogeneous set, and each one it took deleted that
/// card's <c>x:Name="GroupHead"</c> <c>TextBlock</c> and the
/// <c>GroupHead.Text = AppLanguage.Text(…)</c> line behind it. The <c>TabItem</c> header is now the
/// ONLY statement of a group's name — 文件夹 excepted, whose four INNER boxes carry heads of their
/// own — so a header quietly given a literal, or dropped from <c>ApplyText</c>, would leave a tab
/// unnamed in the other language with nothing else left to notice it.
/// </para>
/// </remarks>
public class OptionsWindowTests
{
    private static string DialogPath =>
        Path.Combine(RepoRoot(), "src", "TanMenu.Wpf", "Dialogs", "OptionsWindow.axaml");

    private static string CodeBehindPath => DialogPath + ".cs";

    /// <summary>The markup with comments already gone — <see cref="XDocument"/> drops them — because
    /// this file's own prose quotes the published sentences these tests encode, "ScrollViewer" and
    /// "Header" included.</summary>
    private static XDocument Dialog() => XDocument.Load(DialogPath);

    private static readonly XName XamlName =
        XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

    /// <summary>Walk up from the test assembly until the solution file appears.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TanMenu.slnx")))
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not locate the repository root (TanMenu.slnx) from " +
                                     AppContext.BaseDirectory);
        return dir!.FullName;
    }

    /// <summary>
    /// PUBLISHED, stated identically in three UX Guide articles including
    /// <c>https://learn.microsoft.com/en-us/windows/win32/uxguide/win-property-win</c>: "Right-align
    /// the buttons and use this order (from left to right): OK, Cancel, and Apply." Sizes and spacing
    /// are PUBLISHED in <c>vis-layout</c>: "Command buttons — 75x23", "Between buttons — 4 / 7",
    /// "Between unrelated controls — 7 / 11".
    /// </summary>
    [Fact]
    public void CommitRow_IsOkCancelApplyRightAligned()
    {
        var row = Dialog().Descendants()
            .Single(e => (string?)e.Attribute(XamlName) == "CommitRow");

        Assert.Equal("Right", (string?)row.Attribute("HorizontalAlignment"));
        Assert.Equal("Horizontal", (string?)row.Attribute("Orientation"));
        Assert.Equal("7", (string?)row.Attribute("Spacing"));
        Assert.Equal("0,11,0,0", (string?)row.Attribute("Margin"));
        // Attached properties come through XDocument with the owner in the LOCAL name, unprefixed —
        // the attribute reads "DockPanel.Dock", not a namespaced "Dock".
        Assert.Equal("Bottom", row.Attributes()
            .FirstOrDefault(a => a.Name.LocalName == "DockPanel.Dock")?.Value);

        var buttons = row.Elements().Where(e => e.Name.LocalName == "Button").ToList();
        Assert.Equal(
            new[] { "OkBtn", "CancelBtn", "ApplyBtn" },
            buttons.Select(b => (string?)b.Attribute(XamlName)).ToArray());

        foreach (var b in buttons)
        {
            Assert.Equal("75", (string?)b.Attribute("Width"));
            Assert.Equal("23", (string?)b.Attribute("Height"));
        }

        // The two roles that wire Enter and Esc, and the one button that opens disabled. PUBLISHED,
        // win-property-win: only Apply is "initially disabled" and "enabled when users make a change".
        Assert.Equal("True", (string?)buttons[0].Attribute("IsDefault"));
        Assert.Equal("True", (string?)buttons[1].Attribute("IsCancel"));
        Assert.Equal("False", (string?)buttons[2].Attribute("IsEnabled"));

        // 取消 is never disabled, and neither is 确定.
        Assert.Null((string?)buttons[0].Attribute("IsEnabled"));
        Assert.Null((string?)buttons[1].Attribute("IsEnabled"));
    }

    /// <summary>
    /// PUBLISHED, <c>win-property-win</c>: "Don't scroll property pages. Instead of using scrollbars,
    /// make the property pages larger and lay out the pages efficiently." The page this dialog
    /// replaced WAS a <c>ScrollViewer</c>, so this is a ratchet against the easiest possible
    /// regression: giving the dialog one back the first time a tab does not fit.
    /// </summary>
    /// <remarks>
    /// Scoped to this file, deliberately. <c>ToolsCard</c> has a <c>ScrollViewer</c> of its own around
    /// the per-tool check-box LIST — a control's scroller, ported verbatim from the pre-0.12
    /// <c>SettingsWindow</c>'s 常用工具 tab, with its own <c>MaxHeight</c> and its own measurement in
    /// that file's remarks. "Don't scroll property pages" is about the PAGE; a list box that scrolls
    /// inside a page is a list box.
    /// </remarks>
    [Fact]
    public void NoTabHostsAScrollViewer()
    {
        var scrollers = Dialog().Descendants()
            .Where(e => e.Name.LocalName is "ScrollViewer")
            .ToList();

        Assert.True(scrollers.Count == 0,
            "Dialogs/OptionsWindow.axaml declares " + scrollers.Count + " ScrollViewer(s). PUBLISHED, " +
            "win-property-win: \"Don't scroll property pages. Instead of using scrollbars, make the " +
            "property pages larger and lay out the pages efficiently.\" Make the dialog taller, or " +
            "split the tab — the round's own settled ruling for a tab that will not fit.");
    }

    /// <summary>
    /// Every tab is labelled from <c>AppLanguage</c> by <c>OptionsWindow.ApplyText</c>, and no
    /// <c>TabItem</c> carries a literal <c>Header</c>.
    /// </summary>
    [Fact]
    public void EveryTabHeaderComesFromAppLanguage()
    {
        var tabs = Dialog().Descendants()
            .Where(e => e.Name.LocalName == "TabItem")
            .ToList();

        Assert.True(tabs.Count == 5,
            $"Expected five TabItems in the Options dialog, found {tabs.Count}. It was six until Round " +
            "8, Task 4 took 关于 out into Dialogs/AboutWindow — PUBLISHED, win-property-win: \"Is the " +
            "content solely an application's attributes? If so, use an About box.\" Five is also what " +
            "keeps the strip inside \"Use horizontal tabs if the property window has seven or fewer " +
            "tabs\" with room to spare, and AboutWindowTests holds the other half of that move.");

        var literals = tabs
            .Where(t => t.Attribute("Header") is not null
                        || t.Elements().Any(c => c.Name.LocalName == "TabItem.Header"))
            .Select(t => (string?)t.Attribute(XamlName) ?? "<unnamed>")
            .ToList();

        Assert.True(literals.Count == 0,
            "These TabItems carry a literal Header: " + string.Join(", ", literals) + ". A literal is " +
            "a label in one language. Every header is written by OptionsWindow.ApplyText from an " +
            "AppLanguage key, on the constructor and again on every SettingsChanged, which is what " +
            "makes a 语言 commit re-label the strip without closing the dialog.");

        // Each tab must be named, and ApplyText must actually assign to that name. The pairing is what
        // stops a tab being added to the markup and never labelled.
        var code = File.ReadAllText(CodeBehindPath);
        foreach (var tab in tabs)
        {
            var name = (string?)tab.Attribute(XamlName);
            Assert.False(string.IsNullOrEmpty(name),
                "Every TabItem needs an x:Name so ApplyText can write its Header.");

            var assignment = Regex.Match(
                code, @"\b" + Regex.Escape(name!) + @"\.Header\s*=\s*AppLanguage\.Text\(\s*""(?<k>[^""]+)""");
            Assert.True(assignment.Success,
                $"OptionsWindow.ApplyText does not assign {name}.Header from an AppLanguage key.");

            var key = assignment.Groups["k"].Value;
            var zh = AppLanguage.Text(key, AppLanguage.ZhHans);
            var en = AppLanguage.Text(key, AppLanguage.EnUs);

            // AppLanguage.Text returns the KEY ITSELF when the key is unknown, so "did it resolve" is
            // "is the answer something other than the key". THE CHINESE HALF IS THE ONE THAT CAN ASK
            // THAT and the English half is not: four of these five keys are named after their own
            // English string ("Appearance", "Behavior", "Folders", "Updates"), so an English
            // answer equal to the key is the CORRECT answer and testing it fails a passing build.
            // No Chinese label in this table is an ASCII key name.
            Assert.True(zh != key,
                $"{name}: \"{key}\" is not a key AppLanguage defines, so the tab would be labelled " +
                "with the key text in both languages.");
            Assert.False(string.IsNullOrWhiteSpace(en),
                $"{name}: \"{key}\" has no English text, so the tab would be unlabelled in en-US.");
        }

        // The window's own title comes from the same place, for the same reason.
        Assert.Matches(@"Title\s*=\s*AppLanguage\.Text\(\s*""Options""", code);
    }

    /// <summary>The code-behind with every <c>//</c> comment replaced by a space, so these two tests
    /// read the CODE and not the prose that describes it — both of the invariants below are discussed at
    /// length in remarks that name the very identifiers being matched.</summary>
    private static string CodeWithoutComments() =>
        Regex.Replace(File.ReadAllText(CodeBehindPath), @"//[^\r\n]*", " ");

    /// <summary>
    /// The draft is re-seeded on EVERY close of the dialog, which means the re-seed belongs to
    /// <c>OnClosed</c> and to nothing else.
    /// </summary>
    /// <remarks>
    /// Round 8, Task 2 shipped two <c>Reseed()</c> calls in <c>OnClosing</c>, one per discard branch,
    /// under a comment claiming every close was covered. It was not: 确定 sets <c>_closeApproved</c>
    /// before calling <c>Close()</c>, so the close it triggers returns from <c>OnClosing</c>'s first
    /// branch above both of them and the DI-singleton draft kept a seed one commit out of date into the
    /// next open — <c>AutoStartAtSeed</c> in particular, which no config comparison can catch. This is a
    /// SOURCE test for the usual reason: nothing throws, and the symptom is a disabled 应用 button two
    /// dialog sessions later. <c>SettingsDraftTests</c> owns the behaviour of the re-seed itself; this
    /// owns which method it hangs off.
    /// </remarks>
    [Fact]
    public void TheDraftIsReseededFromOnClosedAndFromNowhereElse()
    {
        var code = CodeWithoutComments();

        var seeds = Regex.Matches(code, @"_draft\.Reseed\(\)");
        Assert.True(seeds.Count == 1,
            $"OptionsWindow.axaml.cs calls _draft.Reseed() {seeds.Count} times. Exactly one is right, " +
            "in OnClosed: it is the one method every close reaches — 确定, 取消, the title-bar ×, the " +
            "discard prompt's second pass and the application exit OnClosing deliberately does not " +
            "prompt. A second copy in OnClosing is how the 确定 path came to be skipped.");

        var onClosed = code.IndexOf("protected override void OnClosed(", StringComparison.Ordinal);
        Assert.True(onClosed >= 0, "OptionsWindow.axaml.cs no longer overrides OnClosed.");
        Assert.True(seeds[0].Index > onClosed,
            "The one _draft.Reseed() call is not inside OnClosed. OnClosing cannot hold it: 确定 " +
            "approves the close before OnClosing runs, so both of its discard branches are skipped.");
    }

    /// <summary>
    /// <c>OptionsWindow.Current</c> is claimed only once the constructor has survived everything that
    /// can throw.
    /// </summary>
    /// <remarks>
    /// The field is the single-instance guard, and it is cleared in <c>OnClosed</c> — which a window
    /// whose constructor threw never reaches. Claimed early, one fault out of the XAML load or the six
    /// <c>ReloadFromConfig</c>s inside <c>ApplyText</c> would latch it at a dead window and
    /// <c>SettingsLauncher.OpenCoreAsync</c> would <c>Activate()</c> that window — silently, doing
    /// nothing — for every 设置 click, tray entry and empty-state button for the rest of the session.
    /// </remarks>
    [Fact]
    public void CurrentIsClaimedAfterTheConstructorsThrowProneWork()
    {
        var code = CodeWithoutComments();

        var claims = Regex.Matches(code, @"Current\s*=\s*this\s*;");
        Assert.True(claims.Count == 1,
            $"OptionsWindow.axaml.cs assigns Current = this {claims.Count} times; it must be exactly " +
            "once, as the constructor's last statement.");

        var applyText = code.IndexOf("ApplyText();", StringComparison.Ordinal);
        Assert.True(applyText >= 0,
            "The constructor no longer calls ApplyText(), so this test cannot locate the throw-prone " +
            "work it is guarding.");
        Assert.True(claims[0].Index > applyText,
            "Current = this is assigned BEFORE ApplyText(), which is the throw-prone part of the " +
            "constructor. A constructor that faults after claiming the field leaves it pointing at a " +
            "window that never opens and never reaches OnClosed, latching 设置 shut for the session.");
    }
}
