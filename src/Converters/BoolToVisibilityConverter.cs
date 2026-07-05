using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OpenCodeCostMeter.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public Visibility TrueVisibility { get; set; } = Visibility.Visible;
    public Visibility FalseVisibility { get; set; } = Visibility.Collapsed;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b ? (b ? TrueVisibility : FalseVisibility) : FalseVisibility;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}