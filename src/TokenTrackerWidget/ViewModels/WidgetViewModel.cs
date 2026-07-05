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
    [ObservableProperty] private string _todayCostDeltaText = "";
    [ObservableProperty] private bool _hasDelta;
    [ObservableProperty] private string _callsText = "0 calls";
    [ObservableProperty] private bool _isRetrying;
    [ObservableProperty] private string _lastErrorText = "";
    [ObservableProperty] private bool _isBreakdownExpanded;

    public string BreakdownToggleText => IsBreakdownExpanded ? "Show Less" : "Usage Details";

    partial void OnIsBreakdownExpandedChanged(bool value) => OnPropertyChanged(nameof(BreakdownToggleText));

    [RelayCommand]
    private void ToggleBreakdown() => IsBreakdownExpanded = !IsBreakdownExpanded;

    private double _lastCost = 0.0;

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
        var delta = cost - _lastCost;
        if (Math.Abs(delta) > 0.0001)
        {
            TodayCostDeltaText = (delta > 0 ? "+" : "") + delta.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
            HasDelta = true;
        }
        else
        {
            TodayCostDeltaText = string.Empty;
            HasDelta = false;
        }
        _lastCost = cost;
        TodayCostText = cost.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-US"));

        CallsText = $"{snap.Calls} {(snap.Calls == 1 ? "call" : "calls")}";

        ModelRows.Clear();
        foreach (var b in snap.Models)
        {
            if (b.Cost < 0.005) continue;
            ModelRows.Add(new ModelRowViewModel(b));
        }

        IsRetrying = false;
        LastErrorText = string.Empty;
    }

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