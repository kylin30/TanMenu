using System.Collections.Generic;
using TanMenu.Core.Models;

namespace TanMenu.Core.Services;

/// <summary>
/// Pure layout helper: splits a folder's items into columns of at most
/// <c>colButtonCount</c> rows each — the port of the old Razor
/// <c>Items.Chunk(General.ColButtonCount)</c> grouping.
/// </summary>
public static class MenuLayout
{
    public static List<List<DirectoryItem>> ChunkIntoColumns(
        IReadOnlyList<DirectoryItem> items, int colButtonCount)
    {
        var result = new List<List<DirectoryItem>>();
        if (items == null || items.Count == 0)
            return result;

        if (colButtonCount <= 0)
        {
            result.Add(new List<DirectoryItem>(items));
            return result;
        }

        for (var i = 0; i < items.Count; i += colButtonCount)
        {
            var column = new List<DirectoryItem>();
            var end = System.Math.Min(i + colButtonCount, items.Count);
            for (var j = i; j < end; j++)
                column.Add(items[j]);
            result.Add(column);
        }

        return result;
    }
}
