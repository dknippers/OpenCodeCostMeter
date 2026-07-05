using System.IO;
using System.Text;
using System.Text.Json;
using OpenCodeCostMeter.Models;

namespace OpenCodeCostMeter.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;

    public SettingsStore(string filePath)
    {
        _path = filePath;
    }

    public static string DefaultPath()
    {
        var dir = AppContext.BaseDirectory;
        return Path.Combine(dir, "OpenCodeCostMeter.settings.json");
    }

    public WidgetSettings Load()
    {
        if (!File.Exists(_path))
            return new WidgetSettings();
        try
        {
            var text = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(text))
                return new WidgetSettings();
            return JsonSerializer.Deserialize<WidgetSettings>(text, JsonOpts) ?? new WidgetSettings();
        }
        catch
        {
            return new WidgetSettings();
        }
    }

    public void Save(WidgetSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOpts);
            File.WriteAllText(_path, json, Encoding.UTF8);
        }
        catch
        {
            // Persistence failures should not crash the widget.
        }
    }
}