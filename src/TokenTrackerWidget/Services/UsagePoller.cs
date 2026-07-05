using System.Windows.Threading;
using TokenTrackerWidget.Data;
using TokenTrackerWidget.Models;

namespace TokenTrackerWidget.Services;

public sealed class UsagePoller : IDisposable
{
    private readonly IUsageRepository _repo;
    private readonly DispatcherTimer _timer;
    private DayUsageSnapshot? _last;
    private bool _running;
    private bool _disposed;

    public UsagePoller(IUsageRepository repo, double intervalSeconds)
    {
        _repo = repo;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(Math.Max(0.25, intervalSeconds))
        };
        _timer.Tick += OnTick;
    }

    public DayUsageSnapshot? LastSnapshot => _last;

    public event EventHandler<DayUsageSnapshot>? Updated;
    public event EventHandler<Exception>? Error;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _timer.Start();
        _ = OnTickAsync();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _timer.Stop();
    }

    public void SetInterval(double seconds)
    {
        var wasRunning = _running;
        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(0.25, seconds));
        if (wasRunning) _timer.Start();
    }

    public void Force()
        => _ = OnTickAsync();

    private void OnTick(object? sender, EventArgs e)
        => _ = OnTickAsync();

    private async Task OnTickAsync()
    {
        if (_disposed) return;
        var start = StartOfTodayMs();
        try
        {
            var snap = await Task.Run(() => _repo.GetToday(start));
            _last = snap;
            Updated?.Invoke(this, snap);
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex);
        }
    }

    public static long StartOfTodayMs()
    {
        var now = DateTimeOffset.Now;
        var startLocal = new DateTimeOffset(now.Date, now.Offset);
        return startLocal.ToUnixTimeMilliseconds();
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}