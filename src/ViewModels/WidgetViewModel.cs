using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using OpenCodeCostMeter.Models;
using OpenCodeCostMeter.Services;

namespace OpenCodeCostMeter.ViewModels;

public partial class WidgetViewModel : ObservableObject, IDisposable
{
    private readonly UsagePoller _poller;
    private readonly DispatcherTimer _highlightTimer;
    private bool _disposed;

    public WidgetViewModel(UsagePoller poller)
    {
        _poller = poller;
        _poller.Updated += OnUpdated;
        _poller.Error += OnError;

        _highlightTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2.0)
        };
        _highlightTimer.Tick += OnHighlightTimerTick;
    }

    private void OnHighlightTimerTick(object? sender, EventArgs e)
    {
        _highlightTimer.Stop();
        IsTodayCostHighlighted = false;
        foreach (var row in ModelRows)
        {
            row.IsCostHighlighted = false;
        }
    }

    [ObservableProperty] private string _todayCostText = "$0.00";
    [ObservableProperty] private bool _isTodayCostHighlighted;
    [ObservableProperty] private bool _isRetrying;
    [ObservableProperty] private string _lastErrorText = "";
    [ObservableProperty] private bool _isBreakdownExpanded;
    [ObservableProperty] private bool _hasModels;

    public string BreakdownToggleText => IsBreakdownExpanded ? "less" : "more";

    partial void OnIsBreakdownExpandedChanged(bool value) => OnPropertyChanged(nameof(BreakdownToggleText));

    [RelayCommand]
    private void ToggleBreakdown() => IsBreakdownExpanded = !IsBreakdownExpanded;

    private string _lastCostText = "$0.00";
    private bool _isFirstUpdate = true;
    private readonly Dictionary<string, string> _lastModelCostTexts = new();

    public ObservableCollection<ModelRowViewModel> ModelRows { get; } = new();

    public void Start() => _poller.Start();
    public void ForceNow() => _poller.Force();
    public void SetInterval(double s) => _poller.SetInterval(s);

    private void OnUpdated(object? sender, DayUsageSnapshot snap)
    {
        var costText = snap.Cost.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
        bool anyHighlight = false;
        if (!_isFirstUpdate && costText != _lastCostText)
        {
            IsTodayCostHighlighted = true;
            anyHighlight = true;
        }
        _isFirstUpdate = false;
        _lastCostText = costText;
        TodayCostText = costText;

        var highlightedModels = new HashSet<string>();
        var canHighlightModels = _lastModelCostTexts.Count > 0;
        foreach (var b in snap.Models)
        {
            var key = ModelKey(b);
            var modelCostText = b.Cost.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
            if (canHighlightModels && _lastModelCostTexts.TryGetValue(key, out var prevCostText) && prevCostText != modelCostText)
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
            if (row.IsCostHighlighted) anyHighlight = true;
            ModelRows.Add(row);
        }

        if (anyHighlight)
        {
            _highlightTimer.Stop();
            _highlightTimer.Start();
        }

        _lastModelCostTexts.Clear();
        foreach (var b in snap.Models)
        {
            _lastModelCostTexts[ModelKey(b)] = b.Cost.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
        }

        HasModels = ModelRows.Count > 0;
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
        _highlightTimer.Stop();
        _highlightTimer.Tick -= OnHighlightTimerTick;
        Detach();
    }
}