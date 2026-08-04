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
/// Guards the three things <c>Dialogs/OptionsWindow.axaml</c> has to be that nothing at run time
/// checks: a Windows property sheet's commit row, a property window that does not scroll, and a tab
/// strip whose labels come from <c>AppLanguage</c> rather than from literals in the markup.
/// </summary>
/// <remarks>
/// <para>
/// SOURCE tests, like <see cref="ShellConstantTests"/> and <see cref="SettingsRowTests"/>, and for the
/// same reason those are: every failure below RENDERS. A commit row in the wrong order renders. A
/// <c>ScrollViewer</c> inside a tab renders, and looks fine until the page is one row taller than the
/// window. A <c>TabItem</c> with a literal <c>Header</c> renders — in one language.
/// </para>
/// <para>
/// The third is the one with a date on it. Round 8, Task 6 deletes five of the six settings group
/// boxes, which deletes the five <c>x:Name="GroupHead"</c> <c>TextBlock</c>s and the five
/// <c>GroupHead.Text = AppLanguage.Text(…)</c> lines behind them. After that the <c>TabItem</c> header
/// is the ONLY statement of a group's name, so a header that had quietly been given a literal — or
/// dropped from <c>ApplyText</c> — would leave a tab unnamed in the other language with nothing else
/// left to notice it.
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

        Assert.True(tabs.Count == 6,
            $"Expected six TabItems in the Options dialog, found {tabs.Count}. Round 8, Task 4 takes " +
            "关于 out into an About box and this becomes five — update the count here in that commit.");

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
            // THAT and the English half is not: five of these six keys are named after their own
            // English string ("Appearance", "Behavior", "Folders", "Updates", "About"), so an English
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
}
