// Copyright (c) 2026 Splitwirez
// Licensed under the MIT License. See LICENSE-aero-theme-for-avalonia.txt in the Themes/Aero
// directory for the full text of the copyright notice and permission notice.
// Source: https://github.com/Splitwirez/aero-theme-for-avalonia at main = a705b7c0
//         src/AeroAvalonia/Converters/DoubleIsNaNConverter.cs
// Changes from upstream:
//   1. Namespace AeroAvalonia.Converters -> TanMenu.Wpf.Themes.Aero.Converters.
//   2. Nullable reference annotations on Convert / ConvertBack, for the reason
//      AdjustCornerRadiusConverter.cs's header states.
//   3. "internal class" -> "public class". Upstream reaches the two singletons with {x:Static} from
//      XAML compiled into the same assembly, which internal permits, and this project's XAML is in
//      the same assembly too - but the type is part of the vendored theme's surface and the rest of
//      these five are public. The two singletons themselves are unchanged.
//   Nothing else is touched: every other character below, including the line endings and the
//   author's own blank lines, is byte for byte upstream.

using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TanMenu.Wpf.Themes.Aero.Converters
{
    public class DoubleIsNaNConverter
        : IValueConverter
    {
        public static readonly DoubleIsNaNConverter IsNaN = new DoubleIsNaNConverter(true);
        public static readonly DoubleIsNaNConverter IsNotNaN = new DoubleIsNaNConverter(false);
        
        
        readonly bool _equalTo;
        private DoubleIsNaNConverter(bool equalTo)
            => _equalTo = equalTo;
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (!(value is double val))
                return _equalTo;
            
            return (double.IsNaN(val) || double.IsInfinity(val)) == _equalTo;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
