using TokenTrackerWidget.Models;

namespace TokenTrackerWidget.Data;

public interface IUsageRepository
{
    DayUsageSnapshot GetToday(long startOfTodayMs);
}

public interface ISanityChecker
{
    SanityReport CrossCheckToday(long startOfTodayMs);
}

public sealed record SanityReport(
    long MessageTableInput,
    long MessageTableOutput,
    long MessageTableCacheRead,
    long MessageTableCostCents,
    long SessionTableInput,
    long SessionTableOutput,
    long SessionTableCacheRead,
    long SessionTableCostCents);