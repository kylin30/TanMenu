using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace TanMenu.Core.Tests.Themes;

/// <summary>
/// Guards the parts of the 设置 row spec that <c>Themes/Fluent2/Launcher.axaml</c> cannot carry.
/// </summary>
/// <remarks>
/// <para>
/// Round 5, item 3 put every part of a settings row's anatomy that has an <c>AvaloniaProperty</c>
/// behind it into a style class. Three parts have none, so they are copied into every card file and
/// only a source test can hold them:
/// </para>
/// <list type="number">
///   <item>the column template itself — <c>Grid</c> has no <c>ColumnDefinitionsProperty</c>
///   (reflection over the shipped <c>Avalonia.Controls.dll</c> 12.1.0 finds
///   <c>ShowGridLines</c>/<c>RowSpacing</c>/<c>ColumnSpacing</c>/<c>Column</c>/<c>Row</c>/
///   <c>ColumnSpan</c>/<c>RowSpan</c>/<c>IsSharedSizeScope</c> and nothing else), and a
///   <c>Setter</c> needs one;</item>
///   <item>the two constants the theme now owns — a style cannot delete a local value, and a local
///   value outranks a style in Avalonia, so a card that keeps writing <c>MaxWidth="260"</c> or
///   <c>Margin="30,…"</c> silently opts out of the class that replaced it;</item>
///   <item>invariant I1, "the header is exactly one title line and one subtitle line" — a count of
///   sibling elements is not a property of anything.</item>
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
/// style classes; task B migrates the sixteen rows onto them. Until it does, the cards below are the
/// exact set that still violates each invariant, and each test asserts the violation set MATCHES that
/// list — so a NEW violation fails, and so does a listed one that has been fixed without being struck
/// off. Every array is empty when the migration is complete, at which point each test reads as the
/// plain invariant it is named for. Do not add an entry to buy time; add one only to record a state
/// the very next commit removes.
/// </para>
/// </remarks>
public class SettingsRowTests
{
    /// <summary>Cards whose <c>Border</c> does not directly wrap the standard column Grid.</summary>
    private static readonly string[] PendingColumnGrid =
    {
        "BehaviorCard.axaml#4",   // 固定到任务栏 — root StackPanel, status line below the Grid
        "BehaviorCard.axaml#6",   // 热键 — root StackPanel, help line below the Grid
        "FoldersCard.axaml#2",    // 数据文件夹 — root StackPanel, buttons below the Grid
        "FoldersCard.axaml#3",    // 维护 — root StackPanel, buttons below the Grid
        "ToolsCard.axaml#1",      // 显示"常用工具"分类 — root StackPanel, tool list below the Grid
        "UpdatesCard.axaml#2",    // update status — root StackPanel, progress + buttons below the Grid
    };

    /// <summary>File + constant pairs still written locally instead of taken from the theme.</summary>
    private static readonly string[] PendingLocalConstants =
    {
        "BehaviorCard.axaml:Margin=\"30,",
        "FoldersCard.axaml:MaxWidth=\"260\"",
        "FoldersCard.axaml:Margin=\"30,",
        "ToolsCard.axaml:Margin=\"30,",
        "UpdatesCard.axaml:Margin=\"30,",
    };

    /// <summary>Cards whose header cell is not exactly one title line plus one subtitle line.</summary>
    private static readonly string[] PendingTwoLineHeader =
    {
        "BehaviorCard.axaml#4",   // 固定到任务栏 — bare title, status line lives below the Grid
        "BehaviorCard.axaml#6",   // 热键 — bare title, help line lives below the Grid
        "FoldersCard.axaml#1",    // 主文件夹 — path line is a third line inside the header
        "FoldersCard.axaml#2",    // 数据文件夹 — path and size are a third and fourth line
        "UpdatesCard.axaml#2",    // update status — the dynamic status wears tm-card-title, no subtitle
    };

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
        // it is now tm-card-value's, together with the HorizontalAlignment that stops MaxWidth centring
        // the line.
        string[] retired = { "Margin=\"30,", "MaxWidth=\"260\"" };
        var offenders = new List<string>();

        foreach (var file in CardFileNames())
        {
            var text = File.ReadAllText(Path.Combine(CardsDir, file));
            offenders.AddRange(retired.Where(text.Contains).Select(c => $"{file}:{c}"));
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
