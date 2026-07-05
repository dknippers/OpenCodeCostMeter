using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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
    [ObservableProperty] private string _activeModelText = "";
    [ObservableProperty] private string _activeSessionLine = "";
    [ObservableProperty] private bool _isLive;
    [ObservableProperty] private string _todayInputText = "0";
    [ObservableProperty] private string _todayOutputText = "0";
    [ObservableProperty] private string _todayReasoningText = "0";
    [ObservableProperty] private string _todayCacheReadText = "0";
    [ObservableProperty] private string _todayCacheWriteText = "0";
    [ObservableProperty] private string _todayCostText = "";
    [ObservableProperty] private string _todayCostDeltaText = "";
    [ObservableProperty] private string _callsText = "0 calls";
    [ObservableProperty] private bool _isRetrying;
    [ObservableProperty] private string _lastErrorText = "";
    [ObservableProperty] private DisplayMode _displayMode = DisplayMode.Tokens;

    private double _lastCost = 0.0;

    public ObservableCollection<ModelBreakdown> ModelBreakdowns { get; } = new();

    public void Start() => _poller.Start();
    public void Stop() => _poller.Stop();
    public void ForceNow() => _poller.Force();
    public void SetInterval(double s) => _poller.SetInterval(s);

    private void OnUpdated(object? sender, DayUsageSnapshot snap)
    {
        DayKeyText = snap.DayKey;
        ActiveModelText = string.IsNullOrEmpty(snap.ActiveModel)
            ? "no active session"
            : snap.ActiveModel;
        ActiveSessionLine = string.IsNullOrEmpty(snap.ActiveSessionTitle)
            ? "—"
            : TrimTitle(snap.ActiveSessionTitle);
        IsLive = snap.IsLive;

        TodayInputText = FormatUtil.FormatBig(snap.Input);
        TodayOutputText = FormatUtil.FormatBig(snap.Output);
        TodayReasoningText = FormatUtil.FormatBig(snap.Reasoning);
        TodayCacheReadText = FormatUtil.FormatBig(snap.CacheRead);
        TodayCacheWriteText = FormatUtil.FormatBig(snap.CacheWrite);
        CallsText = $"{snap.Calls} {(snap.Calls == 1 ? "call" : "calls")}";

        var costCents = FormatUtil.ToCents(snap.Cost);
        if (costCents != _lastCost)
        {
            var delta = snap.Cost - _lastCost;
            TodayCostDeltaText = delta > 0
                ? "+" + FormatUtil.FormatCurrency(delta)
                : string.Empty;
            _lastCost = snap.Cost;
            CostChanged?.Invoke(this, EventArgs.Empty);
        }
        TodayCostText = FormatUtil.FormatCurrency(snap.Cost);

        ModelBreakdowns.Clear();
        foreach (var b in snap.Models)
            ModelBreakdowns.Add(b);

        IsRetrying = false;
        LastErrorText = string.Empty;
    }

    private void OnError(object? sender, Exception ex)
    {
        IsRetrying = true;
        LastErrorText = ex is Microsoft.Data.Sqlite.SqliteException
            ? "(db locked or unavailable)"
            : ex.GetType().Name;
    }

    private static string TrimTitle(string s)
        => s.Length > 42 ? s[..42] + "…" : s;

    public event EventHandler? CostChanged;

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