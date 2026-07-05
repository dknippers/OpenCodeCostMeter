namespace TokenTrackerWidget.Models;

public sealed record DayUsageSnapshot(
    string DayKey,
    long Input,
    long Output,
    long Reasoning,
    long CacheRead,
    long CacheWrite,
    double Cost,
    int Calls,
    IReadOnlyList<ModelBreakdown> Models,
    string? ActiveSessionTitle,
    string? ActiveModel,
    bool IsLive,
    DateTimeOffset TakenAt)
{
    public static DayUsageSnapshot Empty(string? dayKey = null)
        => new(
            DayKey: dayKey ?? DateTimeOffset.Now.ToString("yyyy-MM-dd"),
            Input: 0, Output: 0, Reasoning: 0, CacheRead: 0, CacheWrite: 0,
            Cost: 0, Calls: 0,
            Models: Array.Empty<ModelBreakdown>(),
            ActiveSessionTitle: null, ActiveModel: null,
            IsLive: false,
            TakenAt: DateTimeOffset.Now);
}