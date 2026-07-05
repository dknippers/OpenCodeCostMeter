using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TokenTrackerWidget.Models;
using TokenTrackerWidget.Services;

namespace TokenTrackerWidget.ViewModels;

public partial class WidgetViewModel : ObservableObject, IDisposable
{
    private readonly UsagePoller _poller;
    private bool _disposed;

    public WidgetViewModel(UsagePoller poller)
    {
        _poller = poller;
        _poller.Updated += OnUpdated;
        _poller.Error += OnError;
    }

    [ObservableProperty] private string _dayKeyText = "";
    [ObservableProperty] private string _activeModelText = "no active session";
    [ObservableProperty] private bool _isLive;
    [ObservableProperty] private string _todayCostText = "$0.00";
    [ObservableProperty] private bool _isTodayCostHighlighted;
    [ObservableProperty] private string _callsText = "0 calls";
    [ObservableProperty] private bool _isRetrying;
    [ObservableProperty] private string _lastErrorText = "";
    [ObservableProperty] private bool _isBreakdownExpanded;

    public string BreakdownToggleText => IsBreakdownExpanded ? "hide" : "details";

    partial void OnIsBreakdownExpandedChanged(bool value) => OnPropertyChanged(nameof(BreakdownToggleText));

    [RelayCommand]
    private void ToggleBreakdown() => IsBreakdownExpanded = !IsBreakdownExpanded;

    private double _lastCost = 0.0;
    private bool _isFirstUpdate = true;
    private readonly Dictionary<string, double> _lastModelCosts = new();

    public ObservableCollection<ModelRowViewModel> ModelRows { get; } = new();

    public void Start() => _poller.Start();
    public void Stop() => _poller.Stop();
    public void ForceNow() => _poller.Force();
    public void SetInterval(double s) => _poller.SetInterval(s);

    private void OnUpdated(object? sender, DayUsageSnapshot snap)
    {
        DayKeyText = "Total Spent Today · " + snap.DayKey;
        ActiveModelText = string.IsNullOrEmpty(snap.ActiveModel)
            ? "no active session"
            : snap.ActiveModel;
        IsLive = snap.IsLive;

        var cost = snap.Cost;
        if (_isFirstUpdate)
        {
            IsTodayCostHighlighted = false;
            _isFirstUpdate = false;
        }
        else
        {
            var delta = cost - _lastCost;
            IsTodayCostHighlighted = Math.Abs(delta) >= 0.01;
        }
        _lastCost = cost;
        TodayCostText = cost.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-US"));

        CallsText = $"{snap.Calls} {(snap.Calls == 1 ? "call" : "calls")}";

        var highlightedModels = new HashSet<string>();
        var canHighlightModels = _lastModelCosts.Count > 0;
        foreach (var b in snap.Models)
        {
            var key = ModelKey(b);
            if (canHighlightModels && b.Cost - _lastModelCosts.GetValueOrDefault(key) >= 0.01)
            {
                highlightedModels.Add(key);
            }
        }

        ModelRows.Clear();
        foreach (var b in snap.Models)
        {
            if (b.Cost < 0.005) continue;
            var row = new ModelRowViewModel(b)
            {
                IsCostHighlighted = highlightedModels.Contains(ModelKey(b))
            };
            ModelRows.Add(row);
        }

        _lastModelCosts.Clear();
        foreach (var b in snap.Models)
        {
            _lastModelCosts[ModelKey(b)] = b.Cost;
        }

        IsRetrying = false;
        LastErrorText = string.Empty;
    }

    private static string ModelKey(ModelBreakdown b)
        => string.IsNullOrEmpty(b.Provider) ? b.Model : $"{b.Provider}/{b.Model}";

    private void OnError(object? sender, Exception ex)
    {
        IsRetrying = true;
        LastErrorText = ex is Microsoft.Data.Sqlite.SqliteException
            ? "db locked"
            : ex.GetType().Name;
    }

    public void Detach()
    {
        _poller.Updated -= OnUpdated;
        _poller.Error -= OnError;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
    }
}