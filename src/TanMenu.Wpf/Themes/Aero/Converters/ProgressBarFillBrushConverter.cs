// Copyright (c) 2026 Splitwirez
// Licensed under the MIT License. See LICENSE-aero-theme-for-avalonia.txt in the Themes/Aero
// directory for the full text of the copyright notice and permission notice.
// Source: https://github.com/Splitwirez/aero-theme-for-avalonia at main = a705b7c0
//         src/AeroAvalonia/Converters/ProgressBarFillBrushConverter.cs
// Changes from upstream:
//   1. Namespace AeroAvalonia.Converters -> TanMenu.Wpf.Themes.Aero.Converters.
//   2. Nullable reference annotations. Avalonia 12.1's IMultiValueConverter declares
//      "object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo
//      culture)", so the upstream parameter type IList<object> does not implement the interface at
//      all here - a compile error, not a warning. TryGetColorFrom's first parameter becomes object?
//      for the same reason, and the null-forgiving operator on the Orientation unbox keeps
//      upstream's behaviour, which is to throw on a null values[1].
//   3. "return color != null;" -> "return true;". Color is a non-nullable struct, so the original
//      lifts to a Color? comparison the compiler reports as always true (CS0472). The value it
//      returned was always true; the replacement is that same value without the warning.
//   Nothing else is touched: every other character below, including the line endings and the
//   author's own blank lines, is byte for byte upstream.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

namespace TanMenu.Wpf.Themes.Aero.Converters
{
    public class ProgressBarFillBrushConverter
        : IMultiValueConverter
    {
        public static ProgressBarFillBrushConverter Instance { get; } = new();
        private ProgressBarFillBrushConverter()
        {}


        public static bool TryGetColorFrom(object? obj, out Color color)
        {
            if (obj == null)
            {
                goto fail;
            }
            else if (obj is Color cl)
            {
                color = cl;
            }
            else if (obj is SolidColorBrush scBrush)
            {
                color = scBrush.Color;
            }
            else if (obj is GradientBrush grBrush)
            {
                var stops = grBrush.GradientStops;
                if (stops.Count <= 0)
                    goto fail;
                color = stops[0].Color;
            }
            else
            {
                goto fail;
            }
            return true;

            fail:
            color = default;
            return false;
        }
        const double _L_DENOM = 35d / 41d;
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (!TryGetColorFrom(values[0], out Color color1))
            {
                return values[2];
            }
            
            bool vertical = (Orientation)values[1]! == Orientation.Vertical;

            var hsl1 = color1.ToHsl();
            var hsl0 = HslColor.FromAhsl(color1.A, hsl1.H, hsl1.S, hsl1.L * _L_DENOM);
            Color color0 = hsl0.ToRgb();

            return new LinearGradientBrush()
            {
                StartPoint = new(0, 0, RelativeUnit.Relative),
                EndPoint = new(
                    vertical ? 0 : 1,
                    vertical ? 1 : 0,
                    RelativeUnit.Relative
                ),
                GradientStops = new()
                {
                    new(color0, 0),
                    new(color1, 0.5),
                    new(color0, 1),
                },
            };
        }
    }
}