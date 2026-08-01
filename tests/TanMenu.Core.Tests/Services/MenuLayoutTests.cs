using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

/// <summary>
/// <see cref="MenuLayout.ChunkIntoColumns{T}"/> is the pure arithmetic behind Round 3 Task 4's
/// "items per column" setting (<c>GeneralConfig.ColButtonCount</c>) — tested here against plain
/// strings/ints, with no Avalonia/UI dependency at all, exactly because it was pulled out of
/// <c>TanMenu.Wpf.ViewModels.MenuGroupVm</c> for that reason.
/// </summary>
public class MenuLayoutTests
{
    private static List<string> Items(int count) =>
        Enumerable.Range(1, count).Select(i => "item" + i).ToList();

    [Fact]
    public void ChunkIntoColumns_ExactMultiple_ProducesEvenColumns()
    {
        var columns = MenuLayout.ChunkIntoColumns(Items(10), 5);

        Assert.Equal(2, columns.Count);
        Assert.Equal(5, columns[0].Count);
        Assert.Equal(5, columns[1].Count);
        Assert.Equal("item1", columns[0][0]);
        Assert.Equal("item5", columns[0][4]);
        Assert.Equal("item6", columns[1][0]);
        Assert.Equal("item10", columns[1][4]);
    }

    [Fact]
    public void ChunkIntoColumns_Remainder_LastColumnHoldsWhateverIsLeft()
    {
        // 23 items at 10 per column: two full columns of 10, a third of 3 — never padded to 10.
        var columns = MenuLayout.ChunkIntoColumns(Items(23), 10);

        Assert.Equal(3, columns.Count);
        Assert.Equal(10, columns[0].Count);
        Assert.Equal(10, columns[1].Count);
        Assert.Equal(3, columns[2].Count);
        Assert.Equal("item21", columns[2][0]);
        Assert.Equal("item23", columns[2][2]);
    }

    [Fact]
    public void ChunkIntoColumns_FewerItemsThanCapacity_OneColumnNoPadding()
    {
        var columns = MenuLayout.ChunkIntoColumns(Items(3), 10);

        Assert.Single(columns);
        Assert.Equal(3, columns[0].Count);
    }

    [Fact]
    public void ChunkIntoColumns_EmptyInput_ProducesNoColumns()
    {
        var columns = MenuLayout.ChunkIntoColumns(Items(0), 10);

        Assert.Empty(columns);
    }

    [Fact]
    public void ChunkIntoColumns_DefaultCapacityOfTen_ThirtyItemsProduceThreeColumns()
    {
        // The scenario the brief calls out explicitly: a 30-item category at the default
        // ColButtonCount (10) must land in exactly three columns of 10, not one column of 30 — this
        // is what keeps WindowHost.FitToContent's height probe from growing the window to fit all 30
        // in a single, unwrapped column.
        var columns = MenuLayout.ChunkIntoColumns(Items(30), 10);

        Assert.Equal(3, columns.Count);
        Assert.All(columns, c => Assert.Equal(10, c.Count));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ChunkIntoColumns_NonPositiveCapacity_TreatedAsOne(int perColumn)
    {
        // A hand-edited config.json can carry ColButtonCount <= 0; without this clamp the loop would
        // never advance (perColumn == 0) or would try to Skip/Take a negative count (perColumn < 0).
        var columns = MenuLayout.ChunkIntoColumns(Items(3), perColumn);

        Assert.Equal(3, columns.Count);
        Assert.All(columns, c => Assert.Single(c));
    }

    [Fact]
    public void ChunkIntoColumns_PreservesOriginalOrder()
    {
        var columns = MenuLayout.ChunkIntoColumns(Items(7), 3);

        var flattened = columns.SelectMany(c => c).ToList();
        Assert.Equal(Items(7), flattened);
    }

    [Fact]
    public void ChunkIntoColumns_CapacityOfOne_EveryItemGetsItsOwnColumn()
    {
        var columns = MenuLayout.ChunkIntoColumns(Items(4), 1);

        Assert.Equal(4, columns.Count);
        Assert.All(columns, c => Assert.Single(c));
    }
}
