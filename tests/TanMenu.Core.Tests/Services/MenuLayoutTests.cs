using System.Collections.Generic;
using System.Linq;
using TanMenu.Core.Models;
using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class MenuLayoutTests
{
    private static List<DirectoryItem> MakeItems(int n)
    {
        var list = new List<DirectoryItem>();
        for (var i = 0; i < n; i++)
            list.Add(new DirectoryItem { Name = $"i{i}", FullPath = $@"C:\x\i{i}" });
        return list;
    }

    [Fact]
    public void ChunkIntoColumns_SplitsByColButtonCount()
    {
        var cols = MenuLayout.ChunkIntoColumns(MakeItems(20), 8);
        Assert.Equal(3, cols.Count);          // 8 + 8 + 4
        Assert.Equal(8, cols[0].Count);
        Assert.Equal(8, cols[1].Count);
        Assert.Equal(4, cols[2].Count);
    }

    [Fact]
    public void ChunkIntoColumns_ExactMultiple_NoTrailingEmptyColumn()
    {
        var cols = MenuLayout.ChunkIntoColumns(MakeItems(16), 8);
        Assert.Equal(2, cols.Count);
        Assert.All(cols, c => Assert.Equal(8, c.Count));
    }

    [Fact]
    public void ChunkIntoColumns_EmptyInput_ReturnsEmpty()
    {
        var cols = MenuLayout.ChunkIntoColumns(new List<DirectoryItem>(), 8);
        Assert.Empty(cols);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ChunkIntoColumns_NonPositiveColCount_TreatedAsSingleColumn(int colCount)
    {
        var items = MakeItems(5);
        var cols = MenuLayout.ChunkIntoColumns(items, colCount);
        Assert.Single(cols);
        Assert.Equal(5, cols[0].Count);
    }
}
