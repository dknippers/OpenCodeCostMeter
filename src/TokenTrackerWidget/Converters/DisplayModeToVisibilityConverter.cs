using System.Windows.Data;

namespace TokenTrackerWidget.Converters;

public sealed class DisplayModeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is TokenTrackerWidget.Models.DisplayMode mode)
        {
            var want = parameter?.ToString() ?? "Tokens";
            var wantMode = Enum.Parse<TokenTrackerWidget.Models.DisplayMode>(want);
            return mode == wantMode || mode == TokenTrackerWidget.Models.DisplayMode.Both
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
        }
        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}