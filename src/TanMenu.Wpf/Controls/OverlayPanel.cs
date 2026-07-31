using Avalonia;
using Avalonia.Controls;

namespace TanMenu.Wpf.Controls;

/// <summary>
/// A stacking panel whose FIRST child alone determines the desired size; every later child is an
/// overlay measured against that size and arranged over the whole panel.
/// <para>
/// Written for the content-sized (<c>SizeToContent</c>) launcher popup: the About panel and the error
/// toast had to cover the window without ever changing what the window measured to (a plain
/// <see cref="Panel"/> would take the MAX of all children, so opening About or showing a toast would
/// resize and re-place the popup). The old WebView build got this for free — both were
/// <c>position:fixed</c>, which contributes no layout size (launcher spec §7 and §9).
/// </para>
/// <para>
/// The window has been a fixed, user-resized application window since 0.12 and no longer sizes
/// itself from content, so this panel's own DesiredSize no longer feeds back into the window. It stays
/// in place because it still shapes how the OVERLAYS themselves measure: they see the first child's
/// (the main surface's) desired width rather than the raw available size, so the toast's single-line
/// text still ellipsises against the real content width instead of an unconstrained one.
/// </para>
/// </summary>
public sealed class OverlayPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Children;
        if (children.Count == 0)
            return default;

        var content = children[0];
        content.Measure(availableSize);
        var size = content.DesiredSize;

        for (var i = 1; i < children.Count; i++)
            children[i].Measure(size);

        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rect = new Rect(finalSize);
        foreach (var child in Children)
            child.Arrange(rect);
        return finalSize;
    }
}
