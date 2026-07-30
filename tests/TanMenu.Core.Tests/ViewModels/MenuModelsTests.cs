using System.Linq;
using TanMenu.Wpf.ViewModels;
using Xunit;

namespace TanMenu.Core.Tests.ViewModels;

/// <summary>
/// Guards the two launcher rules most likely to be "improved" into regressions during maintenance:
/// what the search box considers a match, and how items are chunked into columns.
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
        // A disabled item still highlights (only Enter skips it). The old ItemClass behaved this way;
        // "fixing" it here would silently change what the user sees.
        Assert.True(Item("broken link", disabled: true).Matches("broken"));
    }

    // ---- Column chunking -----------------------------------------------------------------------

    private static MenuGroupVm Group(int itemCount)
    {
        var g = new MenuGroupVm { Directory = @"C:\g", DirectoryName = "g" };
        g.Items.AddRange(Enumerable.Range(0, itemCount).Select(i => Item("item" + i)));
        return g;
    }

    [Fact]
    public void Rechunk_FillsColumnsSequentially_NotBalanced()
    {
        var g = Group(7);
        g.Rechunk(3);

        // 7 items at 3 per column = 3 + 3 + 1. NOT a balanced 3/2/2 split.
        Assert.Equal(3, g.Columns.Count);
        Assert.Equal(3, g.Columns[0].Items.Count);
        Assert.Equal(3, g.Columns[1].Items.Count);
        Assert.Single(g.Columns[2].Items);
        Assert.Equal("item0", g.Columns[0].Items[0].Name);
        Assert.Equal("item3", g.Columns[1].Items[0].Name);
        Assert.Equal("item6", g.Columns[2].Items[0].Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Rechunk_ClampsNonPositiveColumnCountsToOne(int configured)
    {
        // Enumerable.Chunk(0) throws; the config value is user-editable and has reached 0 before.
        var g = Group(3);
        g.Rechunk(configured);

        Assert.Equal(3, g.Columns.Count);
        Assert.All(g.Columns, c => Assert.Single(c.Items));
    }

    [Fact]
    public void Rechunk_WithMoreCapacityThanItems_ProducesOneColumn()
    {
        var g = Group(4);
        g.Rechunk(10); // the default ColButtonCount
        Assert.Single(g.Columns);
        Assert.Equal(4, g.Columns[0].Items.Count);
    }

    [Fact]
    public void Rechunk_IsIdempotentForTheSameSize_ButReshapesOnChange()
    {
        var g = Group(6);
        g.Rechunk(3);
        var first = g.Columns;
        g.Rechunk(3);
        Assert.Same(first, g.Columns); // no churn when a settings change did not touch the count

        g.Rechunk(2);
        Assert.NotSame(first, g.Columns);
        Assert.Equal(3, g.Columns.Count);
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
