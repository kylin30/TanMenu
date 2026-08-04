using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TanMenu.Core.Tests.Themes;

/// <summary>
/// Guards the shell's geometry constants against the one failure mode nothing else in this repository
/// can see: a number that exists in two or three files at once, where changing one copy and forgetting
/// the others is silent.
/// </summary>
/// <remarks>
/// <para>
/// These are SOURCE tests, exactly like <see cref="Fluent2TokenTests"/>, and for the same reason: the
/// three failures below all render rather than throw. A caption height that disagrees with
/// <c>ExtendClientAreaTitleBarHeightHint</c> produces a drag band that does not line up with the caption
/// you can see (Win32's <c>HitTestNCA</c> derives the band from the hint, not from what is drawn); a
/// rail width that disagrees with <c>RailWidthDip</c> makes the fit-to-content arithmetic hand back the
/// wrong chrome and brings back the horizontal scrollbar the fit exists to remove; and a
/// <c>MinWidth</c> that disagrees with <c>MinWindowWidthDip</c> makes the placement clamp and the
/// platform's own minimum track size disagree.
/// </para>
/// <para>
/// Every one of the 23 <c>WindowPlacementTests</c> takes the chrome terms as PARAMETERS, so they keep
/// passing through any chrome change — which is precisely why this file exists.
/// </para>
/// </remarks>
public class ShellConstantTests
{
    private static string SrcDir => Path.Combine(RepoRoot(), "src", "TanMenu.Wpf");

    /// <summary>MainWindow's markup with every XML comment stripped. Load-bearing: this file's own
    /// subject matter — the caption height, the rail width, the 760 DIP floor — is discussed at length
    /// in that file's prose, so a raw read would match a number in a sentence as readily as the
    /// attribute it describes, and <see cref="Single"/> would fail on the count.</summary>
    private static string MainWindowXaml =>
        Regex.Replace(File.ReadAllText(Path.Combine(SrcDir, "MainWindow.axaml")),
            "<!--.*?-->", " ", RegexOptions.Singleline);

    private static string MainWindowCs => File.ReadAllText(Path.Combine(SrcDir, "MainWindow.axaml.cs"));
    private static string WindowHostCs =>
        File.ReadAllText(Path.Combine(SrcDir, "Services", "WindowHost.cs"));

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

    [Fact]
    public void CaptionHeight_IsTheSameNumberInAllThreePlaces()
    {
        var xaml = MainWindowXaml;

        var hint = Single(xaml, @"ExtendClientAreaTitleBarHeightHint=""(?<v>[0-9.]+)""",
            "MainWindow.axaml's ExtendClientAreaTitleBarHeightHint");

        // The root Grid — the FIRST RowDefinitions in the file, whose row 0 is the reserved spacer the
        // caption occupies. Anchored on "<Grid RowDefinitions=" so a nested grid cannot be picked up by
        // accident; the rail's own Grid uses "*,Auto" and would fail the numeric group anyway.
        var row0 = Single(xaml, @"<Grid RowDefinitions=""(?<v>[0-9.]+),\*""",
            "MainWindow.axaml's root Grid row 0");

        var constant = Single(WindowHostCs, @"TitleStripHeightDip\s*=\s*(?<v>[0-9.]+)\s*;",
            "WindowHost.TitleStripHeightDip");

        Assert.True(hint == row0 && row0 == constant,
            "The caption height is stated in three unlinked places and they disagree: " +
            $"ExtendClientAreaTitleBarHeightHint={hint}, root Grid row 0={row0}, " +
            $"WindowHost.TitleStripHeightDip={constant}.");
    }

    [Fact]
    public void RailWidth_MatchesTheRailPaneWidth()
    {
        // NavRailPane's own literal Width is the single source of truth; RailWidthDip mirrors it as the
        // fit's chrome term.
        var pane = Single(MainWindowXaml,
            @"x:Name=""NavRailPane""\s*\r?\n\s*Width=""(?<v>[0-9.]+)""",
            "MainWindow.axaml's NavRailPane Width");

        var constant = Single(MainWindowCs, @"RailWidthDip\s*=\s*(?<v>[0-9.]+)\s*;",
            "MainWindow.RailWidthDip");

        Assert.True(pane == constant,
            $"NavRailPane Width={pane} but MainWindow.RailWidthDip={constant}. The fit's chrome width " +
            "would be wrong by the difference, with no error anywhere.");
    }

    [Fact]
    public void MinWindowWidth_IsTheSameNumberInBothPlaces()
    {
        var declared = Single(MainWindowXaml, @"MinWidth=""(?<v>[0-9.]+)""",
            "MainWindow.axaml's MinWidth");

        var constant = Single(WindowHostCs, @"MinWindowWidthDip\s*=\s*(?<v>[0-9.]+)\s*;",
            "WindowHost.MinWindowWidthDip");

        Assert.True(declared == constant,
            $"MainWindow.axaml declares MinWidth={declared} but WindowHost.MinWindowWidthDip={constant}. " +
            "The platform's minimum track size and the placement clamp would disagree.");
    }

    /// <summary>
    /// Every <c>/template/ Type#Name</c> selector in the tree must name a part that actually exists.
    /// </summary>
    /// <remarks>
    /// AN AVALONIA SELECTOR THAT MATCHES NOTHING DOES NOT ERROR, throws nothing and fails no other test.
    /// It is the single most dangerous silent failure class in a theme conversion: a re-templated
    /// control keeps its behaviour and quietly loses every rule that reached into the old template's
    /// parts. Round 7, Task 1 deleted five such rules that had just become dead (they named Fluent's
    /// <c>WindowDrawnDecorations</c> parts, and the template they belonged to was replaced wholesale);
    /// this test is what makes the next occurrence loud.
    /// <para>
    /// A part name passes if it is declared as <c>x:Name=</c> or <c>Name=</c> INSIDE A TEMPLATE BODY
    /// somewhere under <c>src/</c>, or if it is on the allow-list below — names owned by an Avalonia
    /// control template that this app deliberately reaches into. Adding a foreign target therefore
    /// requires editing this test, which is the point.
    /// </para>
    /// <para>
    /// ROUND 7, TASK 8 TIGHTENED "DECLARED" TO MEAN "DECLARED IN A TEMPLATE". Until then any
    /// <c>x:Name=</c> anywhere under <c>src/</c> counted, which let a selector name a PAGE-LEVEL element
    /// and pass — <c>/template/ Border#PagePane</c> would have been accepted on the strength of
    /// <c>MainWindow.axaml</c>'s own <c>x:Name="PagePane"</c>, and it matches nothing, because
    /// <c>/template/</c> only ever descends into a templated control's own name scope. The boundary is
    /// NOT removed, only narrowed to the two elements that actually open a template body in this tree:
    /// <c>&lt;ControlTemplate&gt;</c> and <c>&lt;WindowDrawnDecorationsTemplate&gt;</c> (the latter is
    /// what Task 1's replaced window caption is written in). Both are matched with a depth count, so a
    /// template nested inside a template is inside exactly once.
    /// </para>
    /// <para>
    /// This tightening bites immediately: Task 8 gave every navigation-pane row an
    /// <c>x:Name</c>d label <c>TextBlock</c>, five page-level names that the old rule would have
    /// silently licensed as legal <c>/template/</c> targets.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoTemplatePartSelector_NamesAPartOutsideTheAllowList()
    {
        // Parts belonging to Avalonia's OWN control templates that this app targets on purpose.
        //
        // THE LIST IS EMPTY, and that is the strongest state it can be in: every /template/ selector
        // in the tree now names a part declared by a template the tree itself contains. It held one
        // entry until Round 7 Task 4 — PART_BorderElement, Fluent 12.1's TextBox border
        // (Controls/TextBox.xaml), which the search field's focus suppression and the hotkey box's
        // four state rules reached into. Task 4 vendored Aero's TextBox template, whose border is
        // named BgElement, and re-aimed those rules; nothing targets a foreign name any more. Adding
        // one back means adding it here, deliberately, which is the point of the guard.
        var allowed = new HashSet<string>(StringComparer.Ordinal);

        var files = Directory.GetFiles(SrcDir, "*.axaml", SearchOption.AllDirectories);

        var declared = new HashSet<string>(StringComparer.Ordinal);
        var bodies = new List<(string File, string Body)>();
        foreach (var file in files)
        {
            // Comments are prose, not selectors: this file's own subject matter gets discussed in them.
            var body = Regex.Replace(File.ReadAllText(file), "<!--.*?-->", " ", RegexOptions.Singleline);
            bodies.Add((file, body));

            foreach (var name in TemplatePartNames(body))
                declared.Add(name);
        }

        Assert.True(declared.Count > 0,
            "No template part name was found anywhere under src/, which means the template-body scan " +
            "below stopped working rather than that the tree stopped declaring parts. Fix the scan.");

        var dead = new List<string>();
        foreach (var (file, body) in bodies)
        {
            foreach (Match m in Regex.Matches(body, @"/template/\s*[A-Za-z0-9_:]+#(?<n>[A-Za-z0-9_]+)"))
            {
                var name = m.Groups["n"].Value;
                if (!declared.Contains(name) && !allowed.Contains(name))
                    dead.Add($"{Path.GetFileName(file)}: #{name}");
            }
        }

        Assert.True(dead.Count == 0,
            "These /template/ selectors name a part that no ControlTemplate or " +
            "WindowDrawnDecorationsTemplate under src/ declares, and that is not on the Avalonia-owned " +
            "allow-list, so they match nothing and silently do nothing: " +
            string.Join("; ", dead.Distinct().OrderBy(s => s, StringComparer.Ordinal)));
    }

    /// <summary>
    /// Every <c>x:Name=</c>/<c>Name=</c> declared inside a template body in one comment-stripped markup
    /// file — and nothing declared outside one.
    /// </summary>
    /// <remarks>
    /// A <c>/template/</c> selector descends into a templated control's own name scope and nowhere else,
    /// so a name declared on a page, in a window's content, or as a plain resource is not reachable by
    /// one and must not count as a legal target. The two openers are <c>&lt;ControlTemplate&gt;</c> and
    /// <c>&lt;WindowDrawnDecorationsTemplate&gt;</c>; depth is counted so a nested template contributes
    /// its names once, and a self-closing tag opens nothing.
    /// </remarks>
    private static IEnumerable<string> TemplatePartNames(string body)
    {
        var openers = new Regex(
            @"<(?<close>/)?(?:ControlTemplate|WindowDrawnDecorationsTemplate)\b(?<attrs>[^>]*)>",
            RegexOptions.Singleline);

        var depth = 0;
        var start = -1;

        foreach (Match m in openers.Matches(body))
        {
            if (m.Groups["close"].Success)
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    foreach (Match n in Regex.Matches(body[start..m.Index], @"(?:x:)?Name=""(?<n>[^""]+)"""))
                        yield return n.Groups["n"].Value;
                    start = -1;
                }
                continue;
            }

            // "<ControlTemplate ... />" declares an empty template and opens no body.
            if (m.Groups["attrs"].Value.TrimEnd().EndsWith('/'))
                continue;

            if (depth == 0)
                start = m.Index;
            depth++;
        }
    }

    /// <summary>Match exactly one occurrence of <paramref name="pattern"/>'s "v" group, and fail with a
    /// readable message if the anchor has drifted rather than returning a wrong number.</summary>
    private static string Single(string text, string pattern, string what)
    {
        var matches = Regex.Matches(text, pattern);
        Assert.True(matches.Count == 1,
            $"Expected exactly one occurrence of {what}; found {matches.Count}. The anchor this test " +
            "reads has moved and the test — not the code — needs updating.");
        return matches[0].Groups["v"].Value;
    }
}
