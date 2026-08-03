// Copyright (c) 2026 Splitwirez
// Licensed under the MIT License. See LICENSE-aero-theme-for-avalonia.txt in the Themes/Aero
// directory for the full text of the copyright notice and permission notice.
// Source: https://github.com/Splitwirez/aero-theme-for-avalonia at main = a705b7c0
//         src/AeroAvalonia/Converters/ProgressBarFillColorConverter.cs
// Changes from upstream:
//   1. Namespace AeroAvalonia.Converters -> TanMenu.Wpf.Themes.Aero.Converters.
//   2. Nullable reference annotations, for the reason ProgressBarFillBrushConverter.cs's header
//      states. The null-forgiving operator on lightnessObj.ToString() keeps upstream's behaviour:
//      object.ToString() is declared string?, upstream assigned it to a string, and a null there
//      would already have thrown on the very next line.
//   Nothing else is touched: every other character below, including the line endings and the
//   author's own blank lines, is byte for byte upstream.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TanMenu.Wpf.Themes.Aero.Converters
{
    public class ProgressBarFillColorConverter
        : IMultiValueConverter
    {
        const string _BRUSH = ";brush";
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var valuesCount = values.Count();
            if (valuesCount < 2)
                return null;

            var fallback = values[1];
            if (!ProgressBarFillBrushConverter.TryGetColorFrom(values[0], out Color color1))
                return fallback;

            object? lightnessObj = values.Count() > 2
                ? values[2]
                : parameter
            ;

            if (lightnessObj == null)
                return fallback;

            bool returnBrush = false;
            string lightnessStr = lightnessObj.ToString()!;
            if (lightnessStr.Contains(_BRUSH))
            {
                returnBrush = true;
                lightnessStr = lightnessStr.Replace(_BRUSH, string.Empty);
            }


            if (!double.TryParse(lightnessStr, out double lightness))
                return fallback;


            var hsl = color1.ToHsl();
            hsl = HslColor.FromAhsl(color1.A, hsl.H, hsl.S, lightness);
            Color color = hsl.ToRgb();

            if (returnBrush)
                return new SolidColorBrush(color);
            else
                return color;
        }
    }
}