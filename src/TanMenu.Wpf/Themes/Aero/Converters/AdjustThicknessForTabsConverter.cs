// Copyright (c) 2026 Splitwirez
// Licensed under the MIT License. See LICENSE-aero-theme-for-avalonia.txt in the Themes/Aero
// directory for the full text of the copyright notice and permission notice.
// Source: https://github.com/Splitwirez/aero-theme-for-avalonia at main = a705b7c0
//         src/AeroAvalonia/Converters/AdjustThicknessForTabsConverter.cs
// Changes from upstream:
//   1. Namespace AeroAvalonia.Converters -> TanMenu.Wpf.Themes.Aero.Converters.
//   2. Nullable reference annotations on Convert, so it implements Avalonia 12.1's
//      IMultiValueConverter, whose one member is declared
//      "object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo
//      culture)" - note IList<object?>, not IList<object>. Without them this project's
//      <Nullable>enable</Nullable> reports CS8767 and the zero-warning bar breaks. The two pattern
//      tests below are unchanged and a null element fails them exactly as it did before, so the
//      behaviour is identical; only the annotation is new. This is the same change
//      Converters/AdjustCornerRadiusConverter.cs records as its own item 2.
//   Nothing else is touched: every other character below, including the line endings, the author's
//   own blank lines, his trailing whitespace on the separator lines and his leading-comma formatting
//   of the Thickness arguments, is byte for byte upstream.
//
// WHAT IT IS FOR, since nothing else in this tree calls it: ControlStyles/TabItem.axaml binds the
// tab's OUTER border thickness through this converter, handing it the item's own BorderThickness and
// the TabControl's TabStripPlacement. It zeroes the one edge that faces the page body - a Top strip
// gets no bottom border - so the selected tab opens into the page instead of being fenced off from
// it, which is the Windows 7 property sheet's defining join. A Dock the converter does not recognise
// leaves the thickness untouched, and fewer than two values yield 0 rather than throwing; both are
// upstream's own defensive branches, kept as written.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace TanMenu.Wpf.Themes.Aero.Converters
{
    public class AdjustThicknessForTabsConverter
        : IMultiValueConverter
    {
        public static AdjustThicknessForTabsConverter Instance { get; } = new();
        private AdjustThicknessForTabsConverter()
        {}


        protected static readonly Thickness _ZERO = new(0);
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var valuesCount = values.Count;
            if (valuesCount < 2)
                return _ZERO;
            
            if (!(values[0] is Thickness thickness))
                return _ZERO;
            
            if (!(values[1] is Dock dock))
                return thickness;
            
            return new Thickness(
                dock == Dock.Right
                    ? 0
                    : thickness.Left
                
                , dock == Dock.Bottom
                    ? 0
                    : thickness.Top
                
                , dock == Dock.Left
                    ? 0
                    : thickness.Right
                
                , dock == Dock.Top
                    ? 0
                    : thickness.Bottom
            );
        }
    }
}
