namespace OpenCodeCostMeter.Models;

public sealed record DayUsageSnapshot(
    string DayKey,
    double Cost,
    IReadOnlyList<ModelBreakdown> Models,
    DateTimeOffset TakenAt);