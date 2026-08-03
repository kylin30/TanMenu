// Copyright (c) 2026 Splitwirez
// Licensed under the MIT License. See LICENSE-aero-theme-for-avalonia.txt in the Themes/Aero
// directory for the full text of the copyright notice and permission notice.
// Source: https://github.com/Splitwirez/aero-theme-for-avalonia at main = a705b7c0
//         src/AeroAvalonia/Converters/ProgressBarAnimationConverter.cs
// Changes from upstream:
//   1. Namespace AeroAvalonia.Converters -> TanMenu.Wpf.Themes.Aero.Converters.
//   2. Nullable reference annotations, for the reason ProgressBarFillBrushConverter.cs's header
//      states; TryGetAs's out parameter becomes "out T?" because "value = default" on an
//      unconstrained T is a possible-null assignment (CS8601).
//   3. TryGetAs's guard was written "if (values.Count() <= index);" - an if with an EMPTY statement
//      body, so the else-if ran exactly when the count was sufficient. That is what the author
//      meant and it does work, but the compiler reports CS0642 on it and this project ships no
//      warning it can delete in one edit. Rewritten as one conjoined condition: the same two tests
//      in the same order, short-circuiting the same way, with the same result for every input.
//   Nothing else is touched: every other character below, including the line endings and the
//   author's own blank lines, is byte for byte upstream.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace TanMenu.Wpf.Themes.Aero.Converters
{
    public class ProgressBarAnimationConverter
        : IMultiValueConverter
    {
        public static ProgressBarAnimationConverter Instance { get; } = new();
        private ProgressBarAnimationConverter()
        {}




        static bool TryGetAs<T>(IEnumerable<object?> values, int index, out T? value)
        {
            if (values.Count() > index && values.ElementAt(index) is T ret)
            {
                value = ret;
                return true;
            }
            value = default;
            return false;
        }


        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (!TryGetAs(values, 0, out double containerWidth))
                return BindingOperations.DoNothing;

            if (!TryGetAs(values, 1, out double effectWidth))
                return BindingOperations.DoNothing;

            if (!TryGetAs(values, 2, out double distTravelled))
                return BindingOperations.DoNothing;


            return (containerWidth * distTravelled) - (effectWidth * (1 - distTravelled));
        }
    }
}