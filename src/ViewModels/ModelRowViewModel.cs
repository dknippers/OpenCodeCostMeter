using CommunityToolkit.Mvvm.ComponentModel;
using OpenCodeCostMeter.Models;
using OpenCodeCostMeter.Services;
using System.Globalization;

namespace OpenCodeCostMeter.ViewModels;

public partial class ModelRowViewModel : ObservableObject
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    public string Header { get; }
    public string CostText { get; }

    [ObservableProperty] private bool _isCostHighlighted;

    public ModelRowViewModel(ModelBreakdown b)
    {
        Header = ModelDisplayNameRules.Format(b.Model);
        CostText = b.Cost.ToString("C3", EnUs);
    }
}