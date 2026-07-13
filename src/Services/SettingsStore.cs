using OpenCodeCostMeter.Models;
using System.IO;
using System.Text;
using System.Text.Json;

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

    public Settings Load()
    {
        if (!File.Exists(_path))
            return new Settings();
        try
        {
            var text = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(text))
                return new Settings();
            return JsonSerializer.Deserialize<Settings>(text, JsonOpts) ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    public void Save(Settings settings)
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