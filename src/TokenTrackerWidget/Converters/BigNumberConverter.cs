using System.Globalization;
using System.Windows.Data;

namespace TokenTrackerWidget.Converters;

public sealed class BigNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long l) return FormatLong(l);
        if (value is int i) return FormatLong(i);
        if (value is double d) return FormatDouble(d);
        return value?.ToString() ?? "0";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static string FormatLong(long n)
        => n switch
        {
            >= 1_000_000 => (n / 1_000_000.0).ToString("0.##", CultureInfo.InvariantCulture) + "M",
            >= 1_000 => (n / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k",
            _ => n.ToString("0", CultureInfo.InvariantCulture)
        };

    private static string FormatDouble(double n)
        => n switch
        {
            >= 1_000_000 => (n / 1_000_000.0).ToString("0.##", CultureInfo.InvariantCulture) + "M",
            >= 1_000 => (n / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k",
            _ => n.ToString("0.#", CultureInfo.InvariantCulture)
        };
}