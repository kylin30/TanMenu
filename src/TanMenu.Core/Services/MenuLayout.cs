namespace TanMenu.Core.Services;

/// <summary>
/// Splits a category's items into columns of at most <c>perColumn</c> each, in original order —
/// restoring the pre-Avalonia Blazor build's own <c>MenuLayout.ChunkIntoColumns</c> shape
/// (docs/plans/2026-06-22-tanmenu-wpf-blazor.md §"Interfaces"), which the Fluent 2 rebuild replaced
/// with a height-driven vertical WrapPanel (items per column tracked the window's own height, and
/// <c>GeneralConfig.ColButtonCount</c> was kept only as a round-tripped legacy value) and Round 3's
/// Task 4 replaced back with this, now that the item count per column is a real setting again.
/// <para>
/// An item-count cap decided here, in the DATA, survives <c>WindowHost.FitToContent</c>'s own
/// unbounded-height measurement probe unchanged — a plain vertical StackPanel's DesiredSize is the
/// sum of however many children it was actually given, regardless of the available-size budget it is
/// handed, unlike a height-driven WrapPanel whose wrap point depends entirely on that budget (see
/// <c>WindowHost.FitToContent</c>'s own remarks). That is why this lives in the data layer rather
/// than as a panel property in <c>Themes/Fluent2/Launcher.axaml</c>.
/// </para>
/// <para>
/// Pulled out as a free function in Core — rather than left inline in
/// <c>TanMenu.Wpf.ViewModels.MenuGroupVm</c> — purely so it is unit-testable without any Avalonia/UI
/// dependency at all.
/// </para>
/// </summary>
public static class MenuLayout
{
    /// <summary>Chunk <paramref name="items"/> into columns of <paramref name="perColumn"/>, preserving
    /// order. The last column holds the remainder and is never padded.</summary>
    /// <param name="items">The items to chunk, in the order they should render.</param>
    /// <param name="perColumn">Items per column. Values below 1 are treated as 1: this can be reached
    /// with a hand-edited config.json's <c>GeneralConfig.ColButtonCount</c> set to 0 or negative (the
    /// 设置 page's own control restricts the UI to a sane range, but the on-disk value is not
    /// otherwise validated), and a literal 0 would turn the loop below into an infinite sequence of
    /// empty columns.</param>
    public static List<List<T>> ChunkIntoColumns<T>(IReadOnlyList<T> items, int perColumn)
    {
        var capacity = Math.Max(1, perColumn);
        var columns = new List<List<T>>();

        for (var i = 0; i < items.Count; i += capacity)
        {
            var take = Math.Min(capacity, items.Count - i);
            var column = new List<T>(take);
            for (var j = 0; j < take; j++)
                column.Add(items[i + j]);
            columns.Add(column);
        }

        return columns;
    }
}
