namespace TokenTrackerWidget.Models;

public sealed record DayUsageSnapshot(
    string DayKey,
    long Input,
    long Output,
    long Reasoning,
    long CacheRead,
    long CacheWrite,
    double Cost,
    IReadOnlyList<ModelBreakdown> Models,
    DateTimeOffset TakenAt);