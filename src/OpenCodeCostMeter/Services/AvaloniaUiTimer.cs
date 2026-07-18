using Avalonia.Threading;
using OpenCodeCostMeter.Platform;

namespace OpenCodeCostMeter.Services;

/// <summary>
/// <see cref="IUiTimer"/> over Avalonia's <see cref="DispatcherTimer"/>;
/// ticks on the UI thread.
/// </summary>
public sealed class AvaloniaUiTimer : IUiTimer
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public event EventHandler? Tick
    {
        add => _timer.Tick += value;
        remove => _timer.Tick -= value;
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();
}
