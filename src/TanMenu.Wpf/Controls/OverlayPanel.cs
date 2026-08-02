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
/// <c>position:fixed</c>, which contributes no layout size (launcher spec §7 and §9). Round 5, item 2
/// deleted the About panel — 关于 is a settings card now — so the overlays this holds today are the
/// startup splash, the two empty states and the toast. That removes the panel's original occasion, not
/// its job: every one of those four is still a child that must not feed the window's measure.
/// </para>
/// <para>
/// Between 0.12 and Round 2's Task 4 the window was a fixed, user-resized application window that never
/// sized itself from content, so this panel's own DesiredSize did not feed back into the window over
/// that stretch. Round 2 brought content-driven sizing back — <c>WindowHost.FitToContent</c> measures
/// <c>AppPage</c> (this panel's own host) directly, so this panel's DesiredSize is once again exactly
/// what the window's content-driven width/height are built from, on every cold start. Either way, this
/// panel still exists for the same original reason: it shapes how the OVERLAYS themselves measure — they
/// see the first child's (the main surface's) desired width rather than the raw available size, so the
/// toast's single-line text still ellipsises against the real content width instead of an unconstrained
/// one, AND — the part that would break without it — showing a toast or an empty state never changes
/// what gets measured, which would otherwise feed a moving target into fit-to-content.
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
