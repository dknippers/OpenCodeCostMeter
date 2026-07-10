using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCodeCostMeter.Models;
using OpenCodeCostMeter.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;

namespace OpenCodeCostMeter.ViewModels;

public partial class WidgetViewModel : ObservableObject, IDisposable
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

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

    [RelayCommand]
    private void ToggleBreakdown() => IsBreakdownExpanded = !IsBreakdownExpanded;

    private string _lastCostText = "$0.00";
    private bool _isFirstUpdate = true;
    private Dictionary<string, string> _lastModelCostTexts = new();
    private readonly Dictionary<string, ModelRowViewModel> _rowsByKey = new();

    [ObservableProperty]
    private ObservableCollection<ModelRowViewModel> _modelRows = new();

    public void Start() => _poller.Start();
    public void SetInterval(double s) => _poller.SetInterval(s);

    private void OnUpdated(object? sender, DayUsageSnapshot snap)
    {
        var costText = snap.Cost.ToString("C2", EnUs);
        bool totalChanged;
        if (_isFirstUpdate)
        {
            totalChanged = false;
            _isFirstUpdate = false;
        }
        else
        {
            totalChanged = costText != _lastCostText;
        }
        IsTodayCostHighlighted = totalChanged;
        _lastCostText = costText;
        TodayCostText = costText;

        var anyHighlight = totalChanged;
        var canHighlight = _lastModelCostTexts.Count > 0;
        var nextCostTexts = new Dictionary<string, string>(snap.Models.Count);
        var visibleKeys = new List<string>(snap.Models.Count);

        // Single pass: detect highlights, reuse/create rows, capture next cost texts.
        foreach (var b in snap.Models)
        {
            var key = ModelKey(b);
            var modelCostText = b.Cost.ToString("C3", EnUs);
            nextCostTexts[key] = modelCostText;

            if (b.Cost < 0.0005) continue;

            var newlyHighlighted = canHighlight
                && _lastModelCostTexts.TryGetValue(key, out var prev) && prev != modelCostText;
            if (newlyHighlighted) anyHighlight = true;

            visibleKeys.Add(key);

            ModelRowViewModel row;
            if (_rowsByKey.TryGetValue(key, out var existing))
            {
                row = existing;
                row.CostText = modelCostText;
            }
            else
            {
                row = new ModelRowViewModel(b);
                _rowsByKey[key] = row;
            }
            row.IsCostHighlighted = newlyHighlighted;
        }

        // Diff ModelRows: only rebuild if the visible key sequence changed.
        var sameSequence = ModelRows.Count == visibleKeys.Count;
        for (var i = 0; sameSequence && i < visibleKeys.Count; i++)
        {
            if (!ReferenceEquals(ModelRows[i], _rowsByKey[visibleKeys[i]]))
                sameSequence = false;
        }

        if (!sameSequence && visibleKeys.Count == 0)
        {
            ModelRows.Clear();
        }
        else if (!sameSequence)
        {
            var newRows = new List<ModelRowViewModel>(visibleKeys.Count);
            foreach (var key in visibleKeys)
                newRows.Add(_rowsByKey[key]);
            ModelRows = new ObservableCollection<ModelRowViewModel>(newRows);
        }

        // Remove stale entries from _rowsByKey (rows no longer in any snapshot).
        if (_rowsByKey.Count > nextCostTexts.Count)
        {
            var stale = _rowsByKey.Keys.Except(nextCostTexts.Keys).ToList();
            foreach (var key in stale)
                _rowsByKey.Remove(key);
        }

        if (anyHighlight)
        {
            _highlightTimer.Stop();
            _highlightTimer.Start();
        }

        _lastModelCostTexts = nextCostTexts;

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