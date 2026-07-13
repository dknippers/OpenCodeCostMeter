using System.Text.Json.Serialization;

namespace OpenCodeCostMeter.Models;

public sealed class WidgetSettings
{
    [JsonPropertyName("x")] public double X { get; set; } = double.NaN;
    [JsonPropertyName("y")] public double Y { get; set; } = double.NaN;
    [JsonPropertyName("alwaysOnTop")] public bool AlwaysOnTop { get; set; } = true;
    [JsonPropertyName("pollIntervalSeconds")] public double PollIntervalSeconds { get; set; } = 10.0;
    [JsonPropertyName("opacity")] public double Opacity { get; set; } = 1.0;
    [JsonPropertyName("isExpanded")] public bool IsExpanded { get; set; }
}