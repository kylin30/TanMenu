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
/// Guards the parts of the settings row grammar that <c>Themes/Shell/Launcher.axaml</c> cannot carry.
/// </summary>
/// <remarks>
/// <para>
/// <b>ROUND 8, TASK 6 REPLACED THE GRAMMAR AND TOOK TWO OF THESE FOUR FACTS WITH IT.</b> Round 5,
/// item 3 built a row as a <c>Border Classes="tm-card"</c> wrapping an <c>Auto,*,Auto</c> Grid: a
/// 16 DIP icon, a header of exactly one title line and one subtitle line, and the control trailing.
/// That is Windows 11 Settings' row card. A Windows 7 property page has no such object — a row is a
/// LABEL AND A CONTROL on <c>vis-layout</c>'s ladder — so Task 6 deleted the class and its nine
/// dependants, and with them:
/// </para>
/// <list type="bullet">
///   <item><c>EveryCardBorder_WrapsTheStandardColumnGrid</c>, which matched
///   <c>Classes="tm-card"</c> exactly. There is nothing left for it to guard.</item>
///   <item><c>EveryCardHeader_IsOneTitleLineAndOneSubtitleLine</c>, same reason: there are no
///   headers and no subtitles. The four check boxes on 行为 and the master on 常用工具 carry their
///   own labels as <c>Content</c> now (PUBLISHED, <c>ctrl-check-boxes</c>), and the descriptive
///   second lines that merely restated their labels were deleted from <c>AppLanguage</c>.</item>
/// </list>
/// <para>
/// <b>THREE FACTS SHIP HERE, NOT TWO.</b> Two of the four survived the rewrite and were re-aimed; a
/// third was ADDED, because the invariant it guards outlived the class the deleted test matched on.
/// </para>
/// <list type="number">
///   <item><b>Every constant the THEME owns.</b> A style cannot delete a local value, and a local
///   value outranks a style in Avalonia, so a file that writes one of them silently opts out of the
///   rule that owns it. Widened from the six card files to every settings <c>*.axaml</c> AND the
///   Options dialog, because Task 6 moved the spacing out of the deleted classes and into the
///   markup, where it is now the ONLY statement of the ladder.</item>
///   <item><b>No string that occupies a ROW of the grid may contain a hard line break.</b> Re-aimed
///   from the deleted <c>tm-card-sub</c> to the grid, which is where the constraint has teeth: a
///   <c>TextBlock</c> renders an explicit break at every width, so an extra line makes its own Grid
///   row taller and pushes every row below it down, on a dialog whose <c>Height</c> is a literal.</item>
///   <item><b>Every Grid that declares columns declares the same two.</b> Grid has no
///   <c>ColumnDefinitionsProperty</c>, so no <c>Setter</c> can carry the template and only a source
///   test can. The deleted <c>EveryCardBorder_WrapsTheStandardColumnGrid</c> asserted this THROUGH
///   <c>Classes="tm-card"</c>; the class is gone and the invariant is not, so it is asserted
///   directly now.</item>
/// </list>
/// <para>
/// Fact 2's SCOPE was widened again by the review of Task 6, and the reason is worth recording
/// because the first cut looked complete and was not. Re-aimed at "row labels" it collected a named
/// <c>TextBlock</c> in column 0 and every named <c>CheckBox</c> — and excluded anything carrying
/// <c>Grid.ColumnSpan</c>, which is exactly how <c>BehaviorCard</c>'s <c>PinTaskbarStatusText</c> is
/// written. So <c>AppLanguage.PinToTaskbarFailed</c>, the one string in the tree that has ACTUALLY
/// shipped a hard break, ended up with no assertion on it at all while this file's own comment said
/// it was still guarded. The full-width lines are in scope now: they own a Grid row of their own and
/// a break in one costs the same line as a break in a label.
/// </para>
/// <para>
/// Like <see cref="ShellTokenTests"/> these are source tests, and for the same reason: every failure
/// they catch is silent at run time. A rung off the ladder still renders; it is just a number no
/// Microsoft page states. A label with a break still renders; it just moves everything under it.
/// </para>
/// <para>
/// <b>The two <c>Pending…</c> arrays are a ratchet, not an exemption.</b> Round 5 task A shipped the
/// style classes and listed, in these arrays, the exact set of files that still violated each
/// invariant; task B emptied them. Each test asserts the violation set MATCHES its list, so a NEW
/// violation fails, and so does a listed one that has been fixed without being struck off. Do not add
/// an entry to buy time; add one only to record a state the very next commit removes.
/// </para>
/// </remarks>
public class SettingsRowTests
{
    /// <summary>File + constant pairs still written locally instead of taken from the theme. Empty
    /// since Round 5 task B, and re-verified against the widened scope in Round 8 task 6.</summary>
    private static readonly string[] PendingLocalConstants = Array.Empty<string>();

    /// <summary>Row strings whose wording still carries a hard line break. Empty since the review of
    /// Round 5 task B, which found the shipped <c>PinToTaskbarFailed</c> doing exactly that, and
    /// re-verified against the widened scope in the review of Round 8 task 6 — which is the run that
    /// put <c>PinToTaskbarFailed</c> itself back inside the scan.</summary>
    private static readonly string[] PendingHardBreaks = Array.Empty<string>();

    /// <summary>The column template every settings row repeats because no style can carry it: Grid has
    /// no <c>ColumnDefinitionsProperty</c> (reflection over the shipped <c>Avalonia.Controls.dll</c>
    /// 12.1.0 finds <c>ShowGridLines</c>/<c>RowSpacing</c>/<c>ColumnSpacing</c>/<c>Column</c>/
    /// <c>Row</c>/<c>ColumnSpan</c>/<c>RowSpan</c>/<c>IsSharedSizeScope</c> and nothing else), and a
    /// <c>Setter</c> needs one. It was <c>"Auto,*,Auto"</c> under the card grammar — icon, header,
    /// trailing control — and is <c>"Auto,*"</c> under the DLU grid: label, control.</summary>
    private const string StandardColumns = "Auto,*";

    /// <summary>Aero's PUBLISHED spacing ladder, plus 0.</summary>
    /// <remarks>
    /// <para>
    /// <c>https://learn.microsoft.com/en-us/windows/win32/uxguide/vis-layout</c>, verbatim: "the
    /// smallest space between a non-interactive control and any other control" is <b>3</b>; "target
    /// regions … should either be touching or have at least 3 DLUs (5 relative pixels) of space
    /// between them" and "label to control" give <b>5</b>; "Between related controls — 4 / 7. Between
    /// buttons — 4 / 7" give <b>7</b>; "From the left edge of a group box" gives <b>9</b>; "Dialog box
    /// margins — 7 DLU / 11 relative pixels on all sides", "Between unrelated controls — 7 / 11" and
    /// "Between paragraphs — 11" give <b>11</b>; "First control in a group box" gives <b>16</b>.
    /// </para>
    /// <para>
    /// <b>ROUND 8, TASK 6 EXTENDED THE LIST FROM {0,5,7,11,22} TO THIS</b>, and every rung it added —
    /// 3, 9, 16 — is a cell of that same published table, quoted above. It also dropped the reason the
    /// old list gave for <b>22</b>, which used to read "the 2 x Space3 the theme states". 22 is kept
    /// as "Between paragraphs — 11" doubled, stated here rather than pointed at, and one consumer
    /// keeps it alive.
    /// </para>
    /// <para>
    /// <b>THERE IS NO LONGER A <c>Space3</c> TO POINT AT.</b> Round 8, Task 7 deleted
    /// <c>Space1</c>/<c>Space2</c>/<c>Space3</c> from <c>Tokens.axaml</c> outright: three keys with no
    /// <c>{DynamicResource}</c> consumer anywhere, and none possible, because a Margin is a
    /// <c>Thickness</c> and XAML cannot compose one out of a <c>Double</c> resource. That deletion
    /// cost this test nothing, which is the point of the paragraph below.
    /// </para>
    /// <para>
    /// 0 is on the list because "touching" is the guide's own other option, and a file writing
    /// <c>Margin="0"</c> is opting out of nothing.
    /// </para>
    /// <para>
    /// This array is a hard-coded <c>string[]</c> and has never read <c>Tokens.axaml</c>. That is
    /// deliberate: the ladder is a PUBLISHED table, and a test that read the values it is testing
    /// would assert only that the file agrees with itself.
    /// </para>
    /// </remarks>
    private static readonly string[] AeroLadder = { "0", "3", "5", "7", "9", "11", "16", "22" };

    [Fact]
    public void NoSettingsFile_RestatesAConstantTheThemeOwns()
    {
        // The guard's job has not changed since Round 5: a local value outranks a style setter in
        // Avalonia, so a file that writes a number the THEME owns opts itself out of the class that
        // owns it, silently. What HAS changed twice is which numbers those are and which files are
        // scanned. Round 7 Task 7 re-pointed the ladder from Fluent 2's unpublished 4/8/12/16/20 to
        // Aero's published 5/7/11. Round 8 Task 6 deleted the classes that held the spacing — there
        // is no tm-card, tm-card-text, tm-card-expander or tm-group-box left — so the numbers live in
        // the markup now, and the scope widened with them to every file that states one: the six
        // settings *.axaml AND Dialogs/OptionsWindow.axaml, whose own 11 / 7 / 0,11,0,0 are the same
        // ladder and were previously guarded by nothing.
        //
        // Matched over ATTRIBUTES, not over the file's text, because these files' prose quotes the
        // very constants they no longer state (FoldersCard's remark explains the retired MaxWidth,
        // ToolsCard's the retired 30px indent). XDocument hands us the markup with comments gone.
        var offenders = new List<string>();

        foreach (var (file, doc) in SettingsFiles())
        {
            var attributes = doc.Descendants().Attributes().ToList();

            if (attributes.Any(a => a.Name.LocalName == "Margin"
                                    && a.Value.StartsWith("30,", StringComparison.Ordinal)))
                offenders.Add($"{file}:Margin=\"30,");

            if (attributes.Any(a => a.Name.LocalName == "MaxWidth" && a.Value == "260"))
                offenders.Add($"{file}:MaxWidth=\"260\"");

            foreach (var a in attributes)
            {
                // ColumnSpacing and RowSpacing are on this list since Round 8, Task 6, which is the
                // task that started using them: a Grid's own spacing is spacing, and leaving the two
                // out would have let the new grammar state any rung it liked in the one place it
                // most naturally states one.
                if (a.Name.LocalName is not ("Margin" or "Padding" or "Spacing"
                    or "ColumnSpacing" or "RowSpacing"))
                    continue;

                foreach (var rung in a.Value.Split(',', StringSplitOptions.TrimEntries))
                {
                    if (rung.Length > 0 && !AeroLadder.Contains(rung, StringComparer.Ordinal))
                        offenders.Add($"{file}:{a.Name.LocalName}=\"{a.Value}\"");
                }
            }
        }

        AssertRatchet(
            offenders.Distinct(StringComparer.Ordinal), PendingLocalConstants,
            "A local value outranks a style setter in Avalonia, so a settings file that restates a " +
            "constant the theme owns opts itself out of the rule that replaced it. Spacing comes from " +
            "Aero's published ladder — 3 / 5 / 7 / 9 / 11 / 16, plus 0 and the stated 2 x 11 = 22 — " +
            "and no file may state a rung outside it; the two retired constants (Margin=\"30,…\", " +
            "MaxWidth=\"260\") may not come back at all. Put the number on the ladder, or move it into " +
            "Themes/Shell/Launcher.axaml where a rule can own it.");
    }

    [Fact]
    public void NoSettingsRowString_ContainsAHardLineBreak()
    {
        // A string that OWNS A ROW of a DLU grid must stay one line. A hard "\n" makes that row
        // taller, breaks the 7-DIP rhythm below it, and pushes every later row down — at every
        // width, wrap or no wrap — on a dialog whose Height is a literal. Long wording is fine; this
        // test is only about breaks the STRING forces.
        //
        // Three element shapes qualify, and between them they are every localized line that owns a
        // row on this surface:
        //   * a named TextBlock in Grid.Column="0" with no ColumnSpan — the label half of a
        //     label-and-control row;
        //   * a named CheckBox — its Content IS its label (PUBLISHED, ctrl-check-boxes), which is
        //     where Round 8, Task 6 moved the four 行为 titles and the 常用工具 master's;
        //   * a named TextBlock that SPANS both columns — a help line or a status line, which has no
        //     control beside it to be measured against and instead owns the whole row.
        //
        // THE THIRD SHAPE WAS MISSING UNTIL THE REVIEW OF TASK 6 and its absence had already cost
        // the one string this test exists for: AppLanguage's PinToTaskbarFailed is written into
        // BehaviorCard's PinTaskbarStatusText, which carries Grid.ColumnSpan="2", so the column-0
        // filter excluded it — while this comment claimed it was "still guarded". It shipped a real
        // hard break once (found by the review of Round 5 task B) and is the reason the ratchet
        // exists at all.
        //
        // A VALUE line is deliberately NOT a fourth shape. RootPathText, DataPathText, DataInfoText
        // and UpdateStatusText sit in column 1 without spanning, and the first two are USER DATA
        // (a picked folder path) rather than an AppLanguage key — the keys-found assertion below
        // would fail on them, and correctly: there is no string in this repository to hold to a rule.
        //
        // Every element in scope is populated from AppLanguage in its own code-behind, so the key
        // set is recoverable from source: take the element's x:Name, collect every literal handed to
        // L(…) / AppLanguage.Text(…) / AppLanguage.Format(…) inside the statements that assign that
        // element's .Text or .Content, and read both languages of each key out of AppLanguage.
        var offenders = new List<string>();
        var labelCount = 0;

        foreach (var (file, doc) in CardFiles())
        {
            var code = File.ReadAllText(Path.Combine(SettingsDir, file + ".cs"));
            var labels = RowStringElementNames(doc);

            // A settings tab with no such element at all would make this test pass by checking
            // nothing.
            Assert.True(
                labels.Count > 0,
                $"{file}: no row string found. One is a named TextBlock in Grid.Column=\"0\", a " +
                "named TextBlock spanning both columns, or a named CheckBox carrying its own " +
                "Content. If this card really has none, this test has stopped covering it and must " +
                "be re-aimed rather than left silently passing.");

            foreach (var name in labels)
            {
                labelCount++;
                var keys = LanguageKeysAssignedTo(code, name);

                Assert.True(
                    keys.Count > 0,
                    $"{file}#{name}: no AppLanguage key found assigned to its .Text or .Content. " +
                    "Either this line stopped being populated from AppLanguage, or this test's " +
                    "source scan no longer recognises how — it must not silently check nothing.");

                foreach (var key in keys)
                {
                    var zh = AppLanguage.Text(key, AppLanguage.ZhHans);
                    var en = AppLanguage.Text(key, AppLanguage.EnUs);

                    // AppLanguage.Text returns the KEY ITSELF when the key is unknown, so "did it
                    // resolve" is "is the answer something other than the key". THE CHINESE HALF IS
                    // THE ONE THAT CAN ASK THAT and the English half is not: several of these keys
                    // are named after their own English string ("Language", "Font", "Hotkey",
                    // "Location"), so an English answer equal to the key is the CORRECT answer and
                    // testing it fails a passing build. No Chinese label in this table is an ASCII
                    // key name. Same trap, same reasoning, as
                    // OptionsWindowTests.EveryTabHeaderComesFromAppLanguage.
                    Assert.True(
                        zh != key,
                        $"{file}#{name}: \"{key}\" is not a key AppLanguage defines.");
                    Assert.False(
                        string.IsNullOrWhiteSpace(en),
                        $"{file}#{name}: \"{key}\" has no English text.");

                    if (zh.AsSpan().IndexOfAny('\n', '\r') >= 0 || en.AsSpan().IndexOfAny('\n', '\r') >= 0)
                        offenders.Add($"{file}#{name} → AppLanguage[\"{key}\"]");
                }
            }
        }

        Assert.True(labelCount >= 14,
            $"Found only {labelCount} row strings across the settings tabs. There were 18 when the " +
            "review of Round 8, Task 6 widened this scan: four on 外观, NINE on 行为 (two labels, " +
            "four check boxes and three full-width lines — AutoStartHelp, PinTaskbarStatusText, " +
            "HotkeyHelpText), two on 文件夹, one on 常用工具 and two on 更新 (UpdateStatusLabel and " +
            "UpdateHelpText). It was 14 before that, which is why the floor is 14: a drop back to it " +
            "is exactly the regression that hid PinToTaskbarFailed. A collapse in this number means " +
            "the scan has stopped recognising the markup, not that the surface lost its lines.");

        AssertRatchet(
            offenders, PendingHardBreaks,
            "A string that owns a row of a settings grid may not contain an explicit line break — it " +
            "renders as a real second line at every width, makes its Grid row taller, and pushes " +
            "every row below it down. Reword the string (see PinToTaskbarFailed, which " +
            "parenthesises its {0} instead of breaking before it).");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static string SettingsDir =>
        Path.Combine(RepoRoot(), "src", "TanMenu.Wpf", "Views", "Settings");

    private static string OptionsDialogPath =>
        Path.Combine(RepoRoot(), "src", "TanMenu.Wpf", "Dialogs", "OptionsWindow.axaml");

    /// <summary>
    /// The settings card files, in a stable order so a failure message reads the same twice.
    /// <c>AboutCard.axaml</c> is EXCLUDED: Round 8, Task 4 made it an About box's content rather than
    /// a settings row, and it has no label-and-control row to hold to this grammar. It is still
    /// scanned by <see cref="NoSettingsFile_RestatesAConstantTheThemeOwns"/>, which is about the
    /// ladder rather than about rows.
    /// </summary>
    private static IEnumerable<string> CardFileNames() =>
        Directory.EnumerateFiles(SettingsDir, "*.axaml")
            .Select(Path.GetFileName)
            .Where(f => f is not null && !string.Equals(f, "AboutCard.axaml", StringComparison.Ordinal))
            .Select(f => f!)
            .OrderBy(f => f, StringComparer.Ordinal);

    private static IEnumerable<(string File, XDocument Doc)> CardFiles() =>
        CardFileNames().Select(f => (f, XDocument.Load(Path.Combine(SettingsDir, f))));

    /// <summary>Every file the ladder is enforced over: all six settings <c>*.axaml</c> — AboutCard
    /// included, it states spacing like any other — plus the dialog that hosts five of them.</summary>
    private static IEnumerable<(string File, XDocument Doc)> SettingsFiles()
    {
        foreach (var f in Directory.EnumerateFiles(SettingsDir, "*.axaml")
                     .Select(Path.GetFileName)
                     .Where(f => f is not null)
                     .Select(f => f!)
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            yield return (f, XDocument.Load(Path.Combine(SettingsDir, f)));
        }

        yield return ("OptionsWindow.axaml", XDocument.Load(OptionsDialogPath));
    }

    /// <summary>The <c>x:</c> namespace every settings file declares, so <c>x:Name</c> can be read by
    /// name.</summary>
    private static readonly XName XamlName =
        XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

    /// <summary>
    /// The <c>x:Name</c> of every element in a settings file whose localized string owns a row, in
    /// document order: a named <c>TextBlock</c> in column 0 of a two-column grid (a label), a named
    /// <c>TextBlock</c> spanning both its columns (a help or status line), and every named
    /// <c>CheckBox</c>, whose <c>Content</c> is its own label.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The column test is written against the attribute rather than against the parent Grid's
    /// <c>ColumnDefinitions</c> so that a label inside one of 文件夹's group boxes counts the same as
    /// one on a bare tab. <see cref="StandardColumns"/> is asserted separately below, so a file that
    /// invented a third column would not slip through both.
    /// </para>
    /// <para>
    /// <b>The spanning shape is here because leaving it out silently dropped the one string this
    /// whole test was written for</b> — see the fact list in the class remarks. A spanning line has
    /// no control beside it, so "taller than the control it names" is not the harm; owning one more
    /// row of a fixed-height dialog is.
    /// </para>
    /// </remarks>
    private static List<string> RowStringElementNames(XDocument doc)
    {
        var names = new List<string>();

        foreach (var e in doc.Descendants())
        {
            var name = (string?)e.Attribute(XamlName);
            if (string.IsNullOrEmpty(name))
                continue;

            var span = (string?)e.Attribute("Grid.ColumnSpan");

            var isColumnZeroLabel = e.Name.LocalName == "TextBlock"
                                    && ((string?)e.Attribute("Grid.Column") ?? "0") == "0"
                                    && span is null
                                    && InTwoColumnGrid(e);

            var isFullWidthLine = e.Name.LocalName == "TextBlock"
                                  && span == "2"
                                  && InTwoColumnGrid(e);

            if (isColumnZeroLabel || isFullWidthLine || e.Name.LocalName == "CheckBox")
                names.Add(name!);
        }

        return names;
    }

    /// <summary>True when the element's nearest Grid ancestor is the <c>Auto,*</c> label-and-control
    /// grid. That is what tells a row label apart from a help line or a value line, both of which are
    /// named TextBlocks that are not in a label column.</summary>
    private static bool InTwoColumnGrid(XElement e)
    {
        var grid = e.Ancestors().FirstOrDefault(a => a.Name.LocalName == "Grid");
        return grid is not null && (string?)grid.Attribute("ColumnDefinitions") == StandardColumns;
    }

    /// <summary>
    /// Every distinct AppLanguage key handed to a localisation call inside a statement that assigns
    /// <c>&lt;name&gt;.Text</c> or <c>&lt;name&gt;.Content</c>. Non-greedy to the statement's own
    /// <c>;</c>, which is safe here because none of these assignments — plain, ternary, or
    /// <c>switch</c> expression — contains one internally.
    /// </summary>
    private static List<string> LanguageKeysAssignedTo(string code, string name)
    {
        var statements = Regex.Matches(
            code, @"\b" + Regex.Escape(name) + @"\.(?:Text|Content)\s*=(?!=)(.*?);", RegexOptions.Singleline);

        return statements
            .SelectMany(s => Regex.Matches(
                s.Groups[1].Value,
                @"(?:AppLanguage\.(?:Text|Format)|(?<![\w.])L)\(\s*""([^""]+)"""))
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static void AssertRatchet(
        IEnumerable<string> offenders, IReadOnlyCollection<string> pending, string rule)
    {
        var found = offenders.OrderBy(s => s, StringComparer.Ordinal).ToList();
        var expected = pending.OrderBy(s => s, StringComparer.Ordinal).ToList();

        var newlyBroken = found.Except(expected, StringComparer.Ordinal).ToList();
        var fixedButListed = expected.Except(found, StringComparer.Ordinal).ToList();

        Assert.True(
            newlyBroken.Count == 0,
            rule + Environment.NewLine + "Not conforming, and not on the pending list: " +
            string.Join(", ", newlyBroken));

        Assert.True(
            fixedButListed.Count == 0,
            "These now conform and must be struck off this test's pending list: " +
            string.Join(", ", fixedButListed));
    }

    /// <summary>Every two-column grid on the settings surface states the same template, because Grid
    /// has no <c>ColumnDefinitionsProperty</c> for a Setter to carry (see
    /// <see cref="StandardColumns"/>). This is what stops one card drifting to a third column and
    /// taking its labels off the x every other tab's share.</summary>
    [Fact]
    public void EverySettingsGrid_IsEitherTheTwoColumnTemplateOrHasNoColumns()
    {
        var offenders = new List<string>();

        foreach (var (file, doc) in CardFiles())
        {
            foreach (var grid in doc.Descendants().Where(e => e.Name.LocalName == "Grid"))
            {
                var columns = (string?)grid.Attribute("ColumnDefinitions");
                if (columns is null)
                    continue; // a single-column Grid used purely for rows
                if (columns != StandardColumns)
                    offenders.Add($"{file}:ColumnDefinitions=\"{columns}\"");
            }
        }

        Assert.True(offenders.Count == 0,
            "A settings row is a label in column 0 and its control in column 1, so every Grid that " +
            $"declares columns declares \"{StandardColumns}\". Offending: " +
            string.Join(", ", offenders));
    }

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
}
