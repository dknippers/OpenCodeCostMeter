namespace OpenCodeCostMeter.Platform;

/// <summary>
/// Minimal UI-thread timer abstraction so core logic (polling, highlight
/// resets) stays free of UI-framework dependencies. Implementations must
/// raise <see cref="Tick"/> on the UI thread.
/// </summary>
public interface IUiTimer
{
    TimeSpan Interval { get; set; }

    event EventHandler? Tick;

    void Start();

    void Stop();
}
