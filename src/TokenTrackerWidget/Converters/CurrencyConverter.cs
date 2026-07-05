using System.Globalization;
using System.Windows.Data;

namespace TokenTrackerWidget.Converters;

public sealed class CurrencyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d) return d.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
        if (value is decimal m) return m.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
        return value?.ToString() ?? "$0.00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}