using TokenTrackerWidget.Models;

namespace TokenTrackerWidget.Data;

public interface IUsageRepository
{
    DayUsageSnapshot GetToday(long startOfTodayMs);
}