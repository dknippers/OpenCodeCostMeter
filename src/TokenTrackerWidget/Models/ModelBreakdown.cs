namespace TokenTrackerWidget.Models;

public sealed record ModelBreakdown(
    string Provider,
    string Model,
    int Calls,
    double Cost,
    long Input,
    long Output,
    long CacheRead)
{
    public string Header => string.IsNullOrEmpty(Provider)
        ? Model
        : $"{Model} ({Provider})";
}