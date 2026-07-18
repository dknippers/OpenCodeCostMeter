using OpenCodeCostMeter.Models;

namespace OpenCodeCostMeter.Data;

public interface IUsageRepository
{
    DayUsageSnapshot GetToday(long startOfTodayMs);
}