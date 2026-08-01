using System.Linq;
using TanMenu.Wpf.ViewModels;
using Xunit;

namespace TanMenu.Core.Tests.ViewModels;

/// <summary>
/// Guards the two launcher rules most likely to be "improved" into regressions during maintenance:
/// what the search box considers a match, and what a section actually renders once a filter is on.
/// </summary>
public class MenuModelsTests
{
    private static MenuItemVm Item(string name, bool disabled = false) =>
        new() { Name = name, FullPath = @"C:\x\" + name, IsDisabled = disabled };

    // ---- Matching ------------------------------------------------------------------------------

    [Theory]
    [InlineData("Visual Studio Code", "code", true)]   // case-insensitive
    [InlineData("Visual Studio Code", "CODE", true)]
    [InlineData("Visual Studio Code", "ual stu", true)] // substring, not word-boundary
    [InlineData("计算器", "算", true)]                   // CJK substring
    [InlineData("Visual Studio Code", "vsc", false)]   // NOT fuzzy / initials
    [InlineData("记事本", "jishiben", false)]            // NOT pinyin
    [InlineData("Café", "cafe", false)]                // NOT accent folding
    [InlineData("Visual Studio Code", "", false)]      // empty filter matches nothing
    public void Matches_IsAnOrdinalCaseInsensitiveSubstringOfTheNameOnly(
        string name, string filter, bool expected) =>
        Assert.Equal(expected, Item(name).Matches(filter));

    [Fact]
    public void Matches_IgnoresThePath_OnlyTheNameCounts()
    {
        // The item's path contains "Windows", its name does not — searching for the path must not hit.
        var item = new MenuItemVm { Name = "记事本", FullPath = @"C:\Windows\System32\notepad.exe" };
        Assert.False(item.Matches("Windows"));
        Assert.False(item.Matches("notepad"));
        Assert.True(item.Matches("记事"));
    }

    [Fact]
    public void Matches_DoesNotConsiderIsDisabled()
    {
        // A disabled item still matches, so a filter does not make a broken shortcut vanish — it is
        // information ("this one is broken"), and hiding it would look like the item was deleted.
        Assert.True(Item("broken link", disabled: true).Matches("broken"));
    }

    // ---- Filtering -----------------------------------------------------------------------------

    private static MenuGroupVm Group(params string[] names)
    {
        var g = new MenuGroupVm { Directory = @"C:\g", DirectoryName = "g" };
        g.Items.AddRange(names.Select(n => Item(n)));
        return g;
    }

    [Fact]
    public void ApplyFilter_RemovesNonMatches_RatherThanDimmingThem()
    {
        var g = Group("Visual Studio Code", "Notepad", "计算器");
        g.ApplyFilter("note");

        // The retro launcher kept every item laid out and only faded the non-matches. The Fluent 2
        // launcher genuinely filters: a non-match is ABSENT from what the section renders.
        Assert.Single(g.VisibleItems);
        Assert.Equal("Notepad", g.VisibleItems[0].Name);
        Assert.Equal(3, g.Items.Count); // ...and the underlying set is untouched
    }

    [Fact]
    public void ApplyFilter_WithEmptyText_ShowsEverything()
    {
        var g = Group("a", "b", "c");
        g.ApplyFilter("x");
        Assert.Empty(g.VisibleItems);

        g.ApplyFilter("");
        Assert.Equal(3, g.VisibleItems.Count);
    }

    [Fact]
    public void HasVisibleItems_GoesFalseWhenAFilterEmptiesTheSection()
    {
        // This is what removes a whole section heading from the layout instead of leaving an empty
        // caption behind.
        var g = Group("Notepad");
        Assert.False(g.HasVisibleItems); // nothing applied yet

        g.ApplyFilter("");
        Assert.True(g.HasVisibleItems);

        g.ApplyFilter("zzz");
        Assert.False(g.HasVisibleItems);
    }

    [Fact]
    public void ApplyFilter_RepeatedWithTheSameText_DoesNotChurnTheBinding()
    {
        var g = Group("a", "b");
        g.ApplyFilter("a");
        var first = g.VisibleItems;
        g.ApplyFilter("a");

        // Typing re-applies on every keystroke; recomputing an identical list would raise
        // PropertyChanged and re-realize the whole tile grid for nothing.
        Assert.Same(first, g.VisibleItems);
    }

    [Fact]
    public void InvalidateFilter_ForcesARecomputeAfterTheItemsChange()
    {
        var g = Group("a");
        g.ApplyFilter("");
        Assert.Single(g.VisibleItems);

        // A rebuild replaces Items in place; without invalidation the memoized filter would decide
        // it had already applied "" and the new item would never appear.
        g.Items.Add(Item("b"));
        g.InvalidateFilter();
        g.ApplyFilter("");

        Assert.Equal(2, g.VisibleItems.Count);
    }

    [Fact]
    public void ApplyFilter_KeepsDisabledMatches()
    {
        var g = new MenuGroupVm { Directory = @"C:\g", DirectoryName = "g" };
        g.Items.Add(Item("broken link", disabled: true));
        g.ApplyFilter("broken");

        Assert.Single(g.VisibleItems);
        Assert.True(g.VisibleItems[0].IsDisabled);
    }

    [Fact]
    public void IsDirectoryClickable_IsFalseForTheBuiltInToolsGroup()
    {
        // The 常用工具 group carries an empty Directory, which is what makes its title inert
        // (no folder icon, no tooltip, no click).
        Assert.False(new MenuGroupVm { Directory = "", DirectoryName = "常用工具" }.IsDirectoryClickable);
        Assert.True(new MenuGroupVm { Directory = @"C:\Tools", DirectoryName = "Tools" }.IsDirectoryClickable);
    }

    // ---- Columns (Round 3, Task 4: items per column is a real setting again) -------------------

    [Fact]
    public void Columns_DefaultCapacityIsTen_MatchingGeneralConfigColButtonCount()
    {
        // A freshly built group (SetColumnCapacity not yet called by
        // LauncherViewModel.ApplyGroupSettings) still chunks at the app-wide default until the real
        // config value is applied — see MenuGroupVm's own remarks on _columnCapacity.
        var g = Group(Enumerable.Range(1, 25).Select(i => "item" + i).ToArray());
        g.ApplyFilter("");

        Assert.Equal(3, g.Columns.Count); // 25 at 10 per column: 10, 10, 5
        Assert.Equal(10, g.Columns[0].Items.Count);
        Assert.Equal(10, g.Columns[1].Items.Count);
        Assert.Equal(5, g.Columns[2].Items.Count);
    }

    [Fact]
    public void Columns_ThirtyItemCategory_ChunksIntoThreeColumnsOfTen()
    {
        // The exact scenario the brief calls out: a 30-item category at the default (10) must not
        // render as one unwrapped column of 30, which is what used to drag WindowHost.FitToContent's
        // height probe toward a near-full-screen window.
        var g = Group(Enumerable.Range(1, 30).Select(i => "item" + i).ToArray());
        g.ApplyFilter("");

        Assert.Equal(3, g.Columns.Count);
        Assert.All(g.Columns, c => Assert.Equal(10, c.Items.Count));
    }

    [Fact]
    public void SetColumnCapacity_Change_ReChunksImmediately()
    {
        var g = Group(Enumerable.Range(1, 12).Select(i => "item" + i).ToArray());
        g.ApplyFilter("");
        Assert.Equal(2, g.Columns.Count); // 12 at the default 10: 10 + 2

        g.SetColumnCapacity(5);
        Assert.Equal(3, g.Columns.Count); // 12 at 5: 5 + 5 + 2

        g.SetColumnCapacity(20);
        Assert.Single(g.Columns); // 12 at 20: one column, no padding
        Assert.Equal(12, g.Columns[0].Items.Count);
    }

    [Fact]
    public void SetColumnCapacity_NonPositiveValue_TreatedAsOne()
    {
        // A hand-edited config.json can carry ColButtonCount <= 0 (GeneralConfig.ColButtonCount is not
        // itself clamped on load) — this must not divide by zero or produce a negative-size chunk.
        var g = Group("a", "b", "c");
        g.ApplyFilter("");

        g.SetColumnCapacity(0);
        Assert.Equal(3, g.Columns.Count);
        Assert.All(g.Columns, c => Assert.Single(c.Items));
    }

    [Fact]
    public void SetColumnCapacity_SameValue_IsANoOp()
    {
        var g = Group("a", "b", "c");
        g.ApplyFilter("");
        var before = g.Columns;

        g.SetColumnCapacity(10); // already the default — nothing should change
        Assert.Same(before, g.Columns);
    }

    [Fact]
    public void ApplyFilter_ReComputesColumnsFromVisibleItemsOnly()
    {
        // Columns must reflect what the filter actually shows, not the group's full item list —
        // otherwise a filtered-out item would still occupy a column slot.
        var g = Group("Visual Studio Code", "Notepad", "计算器", "Paint", "Explorer",
            "Terminal", "Registry", "Task Manager", "Control Panel", "Photos", "Camera");
        g.ApplyFilter(""); // 11 items at the default 10 per column: two columns

        Assert.Equal(2, g.Columns.Count);

        g.ApplyFilter("a"); // narrows to whatever matches "a" — well under one column's worth
        var totalAfterFilter = g.Columns.Sum(c => c.Items.Count);
        Assert.Equal(g.VisibleItems.Count, totalAfterFilter);
        Assert.Single(g.Columns);
    }
}
