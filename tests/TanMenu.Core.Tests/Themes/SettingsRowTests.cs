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
/// Guards the parts of the 设置 row spec that <c>Themes/Fluent2/Launcher.axaml</c> cannot carry.
/// </summary>
/// <remarks>
/// <para>
/// Round 5, item 3 put every part of a settings row's anatomy that has an <c>AvaloniaProperty</c>
/// behind it into a style class. Four parts have none, so they live in the card files and the language
/// table instead, and only a source test can hold them:
/// </para>
/// <list type="number">
///   <item>the column template itself — <c>Grid</c> has no <c>ColumnDefinitionsProperty</c>
///   (reflection over the shipped <c>Avalonia.Controls.dll</c> 12.1.0 finds
///   <c>ShowGridLines</c>/<c>RowSpacing</c>/<c>ColumnSpacing</c>/<c>Column</c>/<c>Row</c>/
///   <c>ColumnSpan</c>/<c>RowSpan</c>/<c>IsSharedSizeScope</c> and nothing else), and a
///   <c>Setter</c> needs one;</item>
///   <item>the two constants the row spec retired — a style cannot delete a local value, and a local
///   value outranks a style in Avalonia, so a card that keeps writing <c>MaxWidth="260"</c> or
///   <c>Margin="30,…"</c> silently opts out of the class that replaced it. (<c>Margin="30,…"</c> was
///   replaced by <c>tm-card-expander</c>; <c>MaxWidth="260"</c> briefly moved onto
///   <c>tm-card-value</c> and was then measured inert on the spanning expansion row and deleted
///   outright, so today the guard keeps a retired constant from coming back rather than keeping it in
///   one place);</item>
///   <item>invariant I1, "the header is exactly one title line and one subtitle line" — a count of
///   sibling elements is not a property of anything;</item>
///   <item>I1 again, at the string level — no wording a subtitle can be given may contain a hard line
///   break. A style cannot forbid one, and a two-element header still renders three lines when the
///   text itself carries "\n". Added by the review of task B, which found the shipped
///   <c>PinToTaskbarFailed</c> doing exactly that.</item>
/// </list>
/// <para>
/// Like <see cref="Fluent2TokenTests"/> these are source tests, and for the same reason: every
/// failure they catch is silent at run time. A card that wraps its Grid in a StackPanel still
/// renders; it just stops being able to put an expansion row at column 1's origin. A third line in
/// the header still renders; it just moves that row's icon and control off the y every other row
/// uses.
/// </para>
/// <para>
/// <b>The three <c>Pending…</c> arrays are a ratchet, not an exemption.</b> Round 5 task A shipped the
/// style classes and listed, in these arrays, the exact set of cards that still violated each
/// invariant; task B migrated all sixteen rows and emptied all three. Each test asserts the violation
/// set MATCHES its list, so a NEW violation fails, and so does a listed one that has been fixed
/// without being struck off. Now that every array is empty, each test reads as the plain invariant it
/// is named for. Do not add an entry to buy time; add one only to record a state the very next commit
/// removes.
/// </para>
/// </remarks>
public class SettingsRowTests
{
    /// <summary>Cards whose <c>Border</c> does not directly wrap the standard column Grid. Empty since
    /// Round 5 task B: every card group's rows are the Grid itself.</summary>
    private static readonly string[] PendingColumnGrid = Array.Empty<string>();

    /// <summary>File + constant pairs still written locally instead of taken from the theme. Empty since
    /// Round 5 task B: the 30px indent is now column 1 plus <c>tm-card-expander</c>, and the 260 bound
    /// went to <c>tm-card-value</c> and has since been deleted from there too.</summary>
    private static readonly string[] PendingLocalConstants = Array.Empty<string>();

    /// <summary>Cards whose header cell is not exactly one title line plus one subtitle line. Empty since
    /// Round 5 task B: the two bare titles gained the line that used to hang below their card, the two
    /// folder rows' path/size lines moved to the expansion row, and the update card gained a static
    /// title.</summary>
    private static readonly string[] PendingTwoLineHeader = Array.Empty<string>();

    /// <summary>The column template every card repeats because no style can carry it.</summary>
    private const string StandardColumns = "Auto,*,Auto";

    [Fact]
    public void EveryCardBorder_WrapsTheStandardColumnGrid()
    {
        var offenders = new List<string>();

        foreach (var (file, doc) in CardFiles())
        {
            var cards = Cards(doc);
            for (var i = 0; i < cards.Count; i++)
            {
                var children = cards[i].Elements().ToList();
                var ok = children.Count == 1
                         && children[0].Name.LocalName == "Grid"
                         && (string?)children[0].Attribute("ColumnDefinitions") == StandardColumns;
                if (!ok)
                    offenders.Add(CardId(file, i));
            }
        }

        AssertRatchet(
            offenders, PendingColumnGrid,
            $"Every <Border Classes=\"tm-card\"> must wrap exactly one <Grid " +
            $"ColumnDefinitions=\"{StandardColumns}\"> — an expansion row can only start at column 1's " +
            "origin if the card IS that Grid. Extra content goes on Grid.Row=\"1\" with " +
            "Classes=\"tm-card-expander\", never in a wrapping StackPanel.");
    }

    [Fact]
    public void NoCardFile_RestatesAConstantTheThemeOwns()
    {
        // Margin="30,…" was the hand-written indent that put below-row content under the title; it is
        // now column 1 plus tm-card-expander's own 8. MaxWidth="260" was repeated on three value lines;
        // it moved onto tm-card-value and was then deleted from there as well, once the value lines'
        // move to a spanning expansion row was measured to leave it bounding nothing (see
        // tm-card-value's remark in Launcher.axaml). So this half of the guard now keeps a retired
        // constant from being reintroduced locally, rather than keeping it in one place.
        //
        // Matched over ATTRIBUTES, not over the file's text. Both retired constants are documented in
        // the prose of the very card files that have to be rewritten to stop using them (FoldersCard's
        // class-level remark explains the MaxWidth, and ToolsCard's and UpdatesCard's explain the 30px
        // indent), so a text scan would fail the migration on a comment that correctly describes what
        // was removed. XDocument hands us the markup with the comments already gone.
        var offenders = new List<string>();

        foreach (var (file, doc) in CardFiles())
        {
            var attributes = doc.Descendants().Attributes().ToList();

            if (attributes.Any(a => a.Name.LocalName == "Margin"
                                    && a.Value.StartsWith("30,", StringComparison.Ordinal)))
                offenders.Add($"{file}:Margin=\"30,");

            if (attributes.Any(a => a.Name.LocalName == "MaxWidth" && a.Value == "260"))
                offenders.Add($"{file}:MaxWidth=\"260\"");
        }

        AssertRatchet(
            offenders, PendingLocalConstants,
            "A local value outranks a style setter in Avalonia, so a card that restates one of these " +
            "constants opts itself out of the class that replaced it. Delete the attribute and add the " +
            "class (tm-card-expander / tm-card-value).");
    }

    [Fact]
    public void EveryCardHeader_IsOneTitleLineAndOneSubtitleLine()
    {
        // Invariant I1, and the reason the icon and the trailing control can share one
        // VerticalAlignment="Center" across all sixteen rows: a header of a fixed two lines centres at
        // the same y everywhere. The header CELL is the Grid's column 1 on row 0 — value lines, status
        // lines and help lines belong on the expansion row, so they are outside this count by
        // construction rather than by an exception list.
        var offenders = new List<string>();

        foreach (var (file, doc) in CardFiles())
        {
            var cards = Cards(doc);
            for (var i = 0; i < cards.Count; i++)
            {
                var header = HeaderCell(cards[i]).ToList();
                var titles = ClassCount(header, "tm-card-title");
                var subs = ClassCount(header, "tm-card-sub");
                if (titles != 1 || subs != 1)
                    offenders.Add(CardId(file, i));
            }
        }

        AssertRatchet(
            offenders, PendingTwoLineHeader,
            "A card's header cell (the Grid's Grid.Column=\"1\" content on row 0) must hold exactly one " +
            "Classes=\"tm-card-title\" and exactly one Classes=\"tm-card-sub\". A third line moves that " +
            "row's icon and control off the y every other row centres on; a missing subtitle does the " +
            "same in the other direction.");
    }

    [Fact]
    public void NoHeaderSubtitleString_ContainsAHardLineBreak()
    {
        // Invariant I1 again, one level below the element count above. A header of exactly two ELEMENTS
        // is still a header of three LINES when the subtitle's own text carries "\n": a TextBlock renders
        // an explicit break at every window width, wrap or no wrap, so the card grows past 62.67 DIP and
        // its icon and trailing control leave the y every other row centres on. That is the same defect
        // 7.png reported, arrived at from the string side instead of the markup side.
        //
        // AppLanguage's PinToTaskbarFailed shipped exactly that until the review that added this test,
        // and the state was reachable and persistent: RefreshTaskbarPinStateAsync's catch builds
        // new TaskbarPinState(TaskbarPinStatus.Failed, ex.Message) and ApplyTaskbarPinState writes the
        // string into PinTaskbarStatusText, which is the 固定到任务栏 row's subtitle.
        //
        // Every subtitle on the page is populated from AppLanguage in its own card's code-behind, so the
        // key set is recoverable from source: take the header cell's named tm-card-sub elements, collect
        // every literal handed to L(…) / AppLanguage.Text(…) / AppLanguage.Format(…) inside the statements
        // that assign that element's .Text, and read both languages of each key out of AppLanguage itself.
        // Long wording is fine — this test is only about breaks the string forces, not about wrapping.
        var offenders = new List<string>();
        var cardCount = 0;
        var subtitleCount = 0;

        foreach (var (file, doc) in CardFiles())
        {
            var code = File.ReadAllText(Path.Combine(CardsDir, file + ".cs"));
            cardCount += Cards(doc).Count;

            foreach (var name in HeaderSubtitleNames(doc))
            {
                subtitleCount++;
                var keys = LanguageKeysAssignedTo(code, name);

                // Without this, a subtitle whose assignment the scan fails to recognise would make the
                // test pass by checking nothing. Every one of them is code-populated today.
                Assert.True(
                    keys.Count > 0,
                    $"{file}#{name}: no AppLanguage key found assigned to its .Text. Either this subtitle " +
                    "stopped being populated from AppLanguage, or this test's source scan no longer " +
                    "recognises how — it must not silently check nothing.");

                foreach (var key in keys)
                {
                    var zh = AppLanguage.Text(key, AppLanguage.ZhHans);
                    var en = AppLanguage.Text(key, AppLanguage.EnUs);

                    // AppLanguage.Text returns the key itself when the key is unknown.
                    Assert.True(
                        zh != key && en != key,
                        $"{file}#{name}: \"{key}\" is not a key AppLanguage defines.");

                    if (zh.AsSpan().IndexOfAny('\n', '\r') >= 0 || en.AsSpan().IndexOfAny('\n', '\r') >= 0)
                        offenders.Add($"{file}#{name} → AppLanguage[\"{key}\"]");
                }
            }
        }

        // Every card has exactly one header subtitle — EveryCardHeader_IsOneTitleLineAndOneSubtitleLine
        // asserts that — so a shortfall here means one is unnamed and therefore unreachable by the scan,
        // not that a row is exempt. Give it an x:Name and populate it from AppLanguage like the rest.
        Assert.True(
            subtitleCount == cardCount,
            $"Found {subtitleCount} named header subtitles across {cardCount} cards. Every card's " +
            "tm-card-sub needs an x:Name so its wording can be checked for hard line breaks.");

        Assert.True(
            offenders.Count == 0,
            "A settings row's subtitle may not contain an explicit line break — it renders as a real " +
            "second line at every window width and takes the header to three lines, moving that row's " +
            "icon and control off the y invariant I1 puts every other row on. Reword the string (see " +
            "PinToTaskbarFailed, which parenthesises its {0} instead of breaking before it); wrapping " +
            "long text is what tm-card-sub is already for. Offending: " + string.Join(", ", offenders));
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static string CardsDir =>
        Path.Combine(RepoRoot(), "src", "TanMenu.Wpf", "Views", "Settings");

    /// <summary>The card group files, in a stable order so a failure message reads the same twice.</summary>
    private static IEnumerable<string> CardFileNames() =>
        Directory.EnumerateFiles(CardsDir, "*.axaml")
            .Select(Path.GetFileName)
            .Where(f => f is not null)
            .Select(f => f!)
            .OrderBy(f => f, StringComparer.Ordinal);

    private static IEnumerable<(string File, XDocument Doc)> CardFiles() =>
        CardFileNames().Select(f => (f, XDocument.Load(Path.Combine(CardsDir, f))));

    /// <summary>Every settings card in one file, in document order.</summary>
    private static List<XElement> Cards(XDocument doc) =>
        doc.Descendants()
            .Where(e => e.Name.LocalName == "Border" && (string?)e.Attribute("Classes") == "tm-card")
            .ToList();

    /// <summary>A card's identity, stable under edits elsewhere in the file: file plus its 1-based ordinal.</summary>
    private static string CardId(string file, int index) => $"{file}#{index + 1}";

    /// <summary>
    /// The header cell's contents: the children of the card's <c>Auto,*,Auto</c> Grid that sit in
    /// column 1 on row 0. Written to work on a card that already wraps that Grid directly and on one
    /// that still nests it inside a StackPanel, so the invariant is measurable before and after the
    /// migration rather than only after it.
    /// </summary>
    private static IEnumerable<XElement> HeaderCell(XElement card)
    {
        var grid = card.Descendants().FirstOrDefault(
            e => e.Name.LocalName == "Grid"
                 && (string?)e.Attribute("ColumnDefinitions") == StandardColumns);
        if (grid is null)
            return Array.Empty<XElement>();

        return grid.Elements().Where(
            e => (string?)e.Attribute("Grid.Column") == "1"
                 && ((string?)e.Attribute("Grid.Row") ?? "0") == "0");
    }

    /// <summary>
    /// Elements carrying exactly this class attribute, the element itself included. Exact rather than
    /// token-wise on purpose: <c>Classes="tm-card-sub tm-card-value"</c> is a value line, not a
    /// subtitle line, and must not count towards the header.
    /// </summary>
    private static int ClassCount(IEnumerable<XElement> roots, string classes) =>
        roots.SelectMany(r => r.DescendantsAndSelf())
            .Count(e => (string?)e.Attribute("Classes") == classes);

    /// <summary>
    /// The <c>x:Name</c> of every header-cell subtitle in a card file, in document order. Exact class
    /// match for the same reason <see cref="ClassCount"/> uses one: <c>"tm-card-sub tm-card-value"</c> is
    /// an expansion-row value line, and those do not sit in the header cell anyway.
    /// </summary>
    private static List<string> HeaderSubtitleNames(XDocument doc) =>
        Cards(doc)
            .SelectMany(card => HeaderCell(card).SelectMany(e => e.DescendantsAndSelf()))
            .Where(e => (string?)e.Attribute("Classes") == "tm-card-sub")
            .Select(e => (string?)e.Attribute(XamlName))
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList();

    /// <summary>The <c>x:</c> namespace every card file declares, so <c>x:Name</c> can be read by name.</summary>
    private static readonly XName XamlName =
        XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

    /// <summary>
    /// Every distinct AppLanguage key handed to a localisation call inside a statement that assigns
    /// <c>&lt;name&gt;.Text</c>. Non-greedy to the statement's own <c>;</c>, which is safe here because
    /// none of these assignments — plain, ternary, or <c>switch</c> expression — contains one internally.
    /// </summary>
    private static List<string> LanguageKeysAssignedTo(string code, string name)
    {
        var statements = Regex.Matches(
            code, @"\b" + Regex.Escape(name) + @"\.Text\s*=(?!=)(.*?);", RegexOptions.Singleline);

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
