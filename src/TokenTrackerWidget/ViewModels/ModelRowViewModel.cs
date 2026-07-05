using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TokenTrackerWidget.Models;

namespace TokenTrackerWidget.ViewModels;

public partial class ModelRowViewModel : ObservableObject
{
    public string Header { get; }
    public int Calls { get; }
    public string CallsText { get; }
    public string CostText { get; }

    [ObservableProperty] private Brush _accentBrush;
    [ObservableProperty] private bool _isCostHighlighted;

    public ModelRowViewModel(ModelBreakdown b)
    {
        Header = DisplayHeader(b);
        Calls = b.Calls;
        CallsText = $"{b.Calls}×";
        CostText = b.Cost.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
        var color = ModelColorResolver.For(b.Provider, b.Model);
        AccentBrush = new SolidColorBrush(color)
        {
            Opacity = 0.9
        };
    }

    private static string DisplayHeader(ModelBreakdown b)
    {
        if (string.IsNullOrEmpty(b.Model)) return "(unknown)";
        if (string.IsNullOrEmpty(b.Provider)) return b.Model;
        return b.Model;
    }
}