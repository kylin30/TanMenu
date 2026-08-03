// Copyright (c) 2026 Splitwirez
// Licensed under the MIT License. See LICENSE-aero-theme-for-avalonia.txt in the Themes/Aero
// directory for the full text of the copyright notice and permission notice.
// Source: https://github.com/Splitwirez/aero-theme-for-avalonia at main = a705b7c0
//         src/AeroAvalonia/Converters/AdjustCornerRadiusConverter.cs
// Changes from upstream:
//   1. Namespace AeroAvalonia.Converters -> TanMenu.Wpf.Themes.Aero.Converters.
//   2. Nullable reference annotations on Convert / ConvertBack, so they implement Avalonia 12.1's
//      IValueConverter, whose members are declared
//      "object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)".
//      Without them this project's <Nullable>enable</Nullable> reports CS8767/CS8766 and the
//      zero-warning bar breaks. The null-forgiving operator on the cast keeps upstream's behaviour
//      exactly - it still throws on a null or non-CornerRadius input; only the annotation is new.
//   Nothing else is touched: every other character below, including the line endings and the
//   author's own blank lines, is byte for byte upstream.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace TanMenu.Wpf.Themes.Aero.Converters
{
    public class AdjustCornerRadiusConverter
        : IValueConverter
    {
        public double Amount { get; set; } = 0.0;
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            CornerRadius radii = (CornerRadius)value!;
            var amount = Amount;

            return new CornerRadius(
                radii.TopLeft + amount
                , radii.TopRight + amount
                , radii.BottomRight + amount
                , radii.BottomLeft + amount
            );
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}