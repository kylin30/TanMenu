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
}
