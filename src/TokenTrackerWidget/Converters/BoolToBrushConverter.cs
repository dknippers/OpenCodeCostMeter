using System.Windows.Data;
using System.Windows.Media;

namespace TokenTrackerWidget.Converters;

public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x4C, 0xE3, 0x8B));
    public Brush FalseBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return value is bool b ? (b ? TrueBrush : FalseBrush) : FalseBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}