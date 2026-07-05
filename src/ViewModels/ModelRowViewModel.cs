using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using OpenCodeCostMeter.Models;

namespace OpenCodeCostMeter.ViewModels;

public partial class ModelRowViewModel : ObservableObject
{
    public string Header { get; }
    public string CostText { get; }

    [ObservableProperty] private bool _isCostHighlighted;

    public ModelRowViewModel(ModelBreakdown b)
    {
        Header = DisplayHeader(b);
        CostText = b.Cost.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
    }

    private static string DisplayHeader(ModelBreakdown b)
    {
        if (string.IsNullOrEmpty(b.Model)) return "(unknown)";
        if (string.IsNullOrEmpty(b.Provider)) return b.Model;
        return b.Model;
    }
}