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

    [ObservableProperty]
    public partial string TodayCostText { get; set; } = "$0.00";

    [ObservableProperty]
    public partial bool IsTodayCostHighlighted { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string ErrorText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool HasModels { get; set; }

    public event EventHandler? FirstResultReceived;

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    private string _lastCostText = "$0.00";
    private bool _isFirstUpdate = true;
    private Dictionary<string, string> _lastModelCostTexts = [];
    private readonly Dictionary<string, ModelRowViewModel> _rowsByKey = [];

    [ObservableProperty]
    public partial ObservableCollection<ModelRowViewModel> ModelRows { get; set; } = [];

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
            FirstResultReceived?.Invoke(this, EventArgs.Empty);
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

        // Truncate any surplus rows at the tail.
        while (ModelRows.Count > visibleKeys.Count)
            ModelRows.RemoveAt(ModelRows.Count - 1);

        // In-place diff: emit the minimum number of Move/Insert operations
        // so WPF's ItemContainerGenerator can reuse containers instead of rebuilding.
        for (var i = 0; i < visibleKeys.Count; i++)
        {
            var desiredRow = _rowsByKey[visibleKeys[i]];
            if (i < ModelRows.Count && ReferenceEquals(ModelRows[i], desiredRow))
                continue;

            if (i < ModelRows.Count)
            {
                var currentIndex = -1;
                for (var j = 0; j < ModelRows.Count; j++)
                {
                    if (ReferenceEquals(ModelRows[j], desiredRow))
                    {
                        currentIndex = j;
                        break;
                    }
                }
                if (currentIndex >= 0)
                {
                    ModelRows.Move(currentIndex, i);
                    continue;
                }
            }
            ModelRows.Insert(i, desiredRow);
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
        HasError = false;
        ErrorText = string.Empty;
    }

    private static string ModelKey(ModelBreakdown b)
        => string.IsNullOrEmpty(b.Provider) ? b.Model : $"{b.Provider}/{b.Model}";

    private void OnError(object? sender, Exception ex)
    {
        if (_isFirstUpdate)
        {
            _isFirstUpdate = false;
            FirstResultReceived?.Invoke(this, EventArgs.Empty);
        }
        HasError = true;
        ErrorText = ex.Message;
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
        GC.SuppressFinalize(this);
    }
}