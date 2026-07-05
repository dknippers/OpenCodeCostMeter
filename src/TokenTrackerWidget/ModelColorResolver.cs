using System.Globalization;
using System.Windows.Media;

namespace TokenTrackerWidget;

public static class ModelColorResolver
{
    public static readonly Color Gpt = Color.FromRgb(0x10, 0xA3, 0x7F);
    public static readonly Color Claude = Color.FromRgb(0x8E, 0x4E, 0xC6);
    public static readonly Color Other = Color.FromRgb(0x00, 0x78, 0xD4);

    public static Color For(string? providerId, string? modelId)
    {
        var m = (modelId ?? string.Empty).ToLowerInvariant();
        if (m.Contains("gpt") || m.StartsWith("o1") || m.StartsWith("o3") || m.StartsWith("o4"))
            return Gpt;
        if (m.Contains("claude"))
            return Claude;
        return Other;
    }

    public static string Hex(Color c)
        => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}