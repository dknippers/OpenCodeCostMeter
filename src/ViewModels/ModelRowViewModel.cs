using CommunityToolkit.Mvvm.ComponentModel;
using OpenCodeCostMeter.Models;
using OpenCodeCostMeter.Services;
using System.Globalization;

namespace OpenCodeCostMeter.ViewModels;

public partial class ModelRowViewModel : ObservableObject
{
    public string Header { get; }
    public string CostText { get; }

    [ObservableProperty] private bool _isCostHighlighted;

    public ModelRowViewModel(ModelBreakdown b)
    {
        Header = ModelDisplayNameRules.Format(b.Model);
        CostText = b.Cost.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
    }
}