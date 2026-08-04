// Copyright (c) 2026 Splitwirez
// Licensed under the MIT License. See LICENSE-aero-theme-for-avalonia.txt in the Themes/Aero
// directory for the full text of the copyright notice and permission notice.
// Source: https://github.com/Splitwirez/aero-theme-for-avalonia at main = a705b7c0
//         src/AeroAvalonia/Controls/HeaderGapClipper.cs
// Changes from upstream:
//   1. Namespace AeroAvalonia -> TanMenu.Wpf.Themes.Aero.Controls.
//   2. HeaderElement is StyledProperty<Layoutable?> rather than StyledProperty<Layoutable>. Its
//      default value is null - the template binds it to a part that does not exist yet when the
//      property is first read - so under this project's <Nullable>enable</Nullable> the
//      non-nullable form reports on the generated accessors. Upstream's own code already tests
//      "headerEl == null", so the annotation only states what was always true.
//   3. CreateClipGeometry returns StreamGeometry? rather than StreamGeometry, for the same reason:
//      upstream's own first statement returns null.
//   4. The two TranslatePoint results are pattern-matched instead of dereferenced with .Value.
//      Visual.TranslatePoint returns Point? and yields null when the two visuals are not in one
//      connected tree, which is reachable here: the refresh is posted to the dispatcher, so it can
//      run after the group box has been detached (this app switches PageHost.Content between two
//      pages, which detaches every settings card at once). Upstream would throw
//      InvalidOperationException out of Nullable<Point>.Value; this returns no clip instead, which
//      is what the null-header branch above it already does.
//   5. ADDED: the clip is also refreshed when the HEADER's own Bounds change, through a
//      PropertyChanged hook that follows HeaderElement. Upstream refreshes only on attach and on
//      the CLIPPER's own SizeChanged, and the notch is cut to the header's rect - so a header whose
//      text changes without the group box resizing keeps a hole of the old width. That is not
//      hypothetical here: every group head on the 设置 page is written from code-behind
//      (SettingsPage.ApplyStaticText) and rewritten on every 语言 change, while the group box's own
//      width is the card column's and does not move. The hook is torn down with the element it
//      follows, so nothing outlives the control.
//   6. "using System;" dropped - nothing in the file uses it once (4) removes the exception path.
//   Everything else, including the author's own blank-line spacing, is byte for byte upstream.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace TanMenu.Wpf.Themes.Aero.Controls
{
    public class HeaderGapClipper
        : Decorator
    {
        public static readonly StyledProperty<Layoutable?> HeaderElementProperty
            = AvaloniaProperty.Register<HeaderGapClipper, Layoutable?>(nameof(HeaderElement));
        public Layoutable? HeaderElement
        {
            get => GetValue(HeaderElementProperty);
            set => SetValue(HeaderElementProperty, value);
        }




        static HeaderGapClipper()
        {
            HeaderGapClipper.ClipToBoundsProperty.OverrideDefaultValue<HeaderGapClipper>(true);
        }




        public HeaderGapClipper()
            : base()
        {
        }




        StreamGeometry? CreateClipGeometry()
        {
            var headerEl = HeaderElement;
            if (headerEl == null)
                return null;
            var hElBounds = headerEl.Bounds;
            if (headerEl.TranslatePoint(hElBounds.TopLeft, this) is not { } hElTl)
                return null;
            if (headerEl.TranslatePoint(hElBounds.BottomRight, this) is not { } hElBr)
                return null;
            StreamGeometry clipGeom = new();
            var bounds = Bounds;
            var l = bounds.X;
            var t = bounds.Y;
            var r = bounds.Right;
            var b = bounds.Bottom;
            using (var ctx = clipGeom.Open())
            {
                ctx.BeginFigure(new(l, t), true);
                ctx.LineTo(new(hElTl.X, t));
                ctx.LineTo(new(hElTl.X, hElBr.Y));
                ctx.LineTo(new(hElBr.X, hElBr.Y));
                ctx.LineTo(new(hElBr.X, t));
                ctx.LineTo(new(r, t));
                ctx.LineTo(new(r, b));
                ctx.LineTo(new(l, b));
                ctx.EndFigure(true);
            }
            return clipGeom;
        }


        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            RefreshClipGeometry();
        }
        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            DelayedRefreshClipGeometry();
        }

        // Change 5: follow the header's own Bounds, not just ours.
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != HeaderElementProperty)
                return;

            if (change.GetOldValue<Layoutable?>() is { } old)
                old.PropertyChanged -= OnHeaderPropertyChanged;
            if (change.GetNewValue<Layoutable?>() is { } added)
                added.PropertyChanged += OnHeaderPropertyChanged;

            DelayedRefreshClipGeometry();
        }

        void OnHeaderPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == BoundsProperty)
                DelayedRefreshClipGeometry();
        }


        void DelayedRefreshClipGeometry()
            => Dispatcher.UIThread.Post(RefreshClipGeometry);
        void RefreshClipGeometry()
        {
            Clip = CreateClipGeometry();
        }
    }
}
