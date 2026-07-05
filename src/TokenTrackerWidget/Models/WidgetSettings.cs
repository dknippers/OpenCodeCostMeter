using System.Text.Json.Serialization;

namespace TokenTrackerWidget.Models;

public sealed class WidgetSettings
{
    public const double DefaultWidth = 240;
    public const double DefaultHeight = 220;

    [JsonPropertyName("x")] public double X { get; set; } = double.NaN;
    [JsonPropertyName("y")] public double Y { get; set; } = double.NaN;
    [JsonPropertyName("width")] public double Width { get; set; } = DefaultWidth;
    [JsonPropertyName("height")] public double Height { get; set; } = DefaultHeight;
    [JsonPropertyName("alwaysOnTop")] public bool AlwaysOnTop { get; set; } = true;
    [JsonPropertyName("pollIntervalSeconds")] public double PollIntervalSeconds { get; set; } = 2.5;
    [JsonPropertyName("opacity")] public double Opacity { get; set; } = 0.88;
    [JsonPropertyName("displayMode")] public DisplayMode DisplayMode { get; set; } = DisplayMode.Tokens;
    [JsonPropertyName("collapsed")] public bool Collapsed { get; set; } = false;
    [JsonPropertyName("runAtStartup")] public bool RunAtStartup { get; set; } = false;
    [JsonPropertyName("dbPathOverride")] public string? DatabasePathOverride { get; set; }
}