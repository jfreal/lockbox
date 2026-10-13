using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Lockbox.App.Controls;

/// <summary>true → Visible. Pass "invert" as the parameter for the opposite.</summary>
public sealed class BoolToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var on = value is true;
        if (parameter is "invert")
            on = !on;
        return on ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>null or empty string → Collapsed.</summary>
public sealed class PresentToVisibility : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Strength meter: segment N is lit when the level is at least N.</summary>
public sealed class StrengthSegmentOpacity : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int level && int.TryParse(parameter as string, out var segment) && level >= segment ? 1.0 : 0.18;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Hides a row's top divider on the first row. Values: [item, the ItemsControl's ItemsSource, its Count].
/// Count is only there so the binding re-runs when the list changes.
/// </summary>
public sealed class DividerVisibility : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length >= 2 && values[1] is System.Collections.IList list && list.Count > 0 && ReferenceEquals(list[0], values[0])
            ? Visibility.Collapsed
            : Visibility.Visible;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
