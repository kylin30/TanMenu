using Avalonia;
using Avalonia.Controls;

namespace TanMenu.Wpf.Controls;

/// <summary>
/// A stacking panel whose FIRST child alone determines the desired size; every later child is an
/// overlay measured against that size and arranged over the whole panel.
/// <para>
/// This exists because the launcher window is content-sized (<c>SizeToContent</c>): the About panel
/// and the error toast must be able to cover the window without ever changing what the window
/// measures to. The old WebView build got this for free — both were <c>position:fixed</c>, which
/// contributes no layout size (launcher spec §7 and §9). A plain <see cref="Panel"/> would take the
/// MAX of all children, so opening About or showing a toast would resize and re-place the popup.
/// </para>
/// <para>
/// Overlays are measured against the first child's desired size rather than the raw (often infinite)
/// constraint so that width-dependent behavior still works — e.g. the toast's single-line text
/// ellipsises against the real content width instead of growing without bound.
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
