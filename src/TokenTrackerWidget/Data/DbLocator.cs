using System.IO;
using TokenTrackerWidget.Models;

namespace TokenTrackerWidget.Data;

public static class DbLocator
{
    public static string DefaultPath()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, ".local", "share", "opencode", "opencode.db");
    }

    public static bool TryResolveDatabasePath(WidgetSettings settings, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out string path, out bool overrideExists)
    {
        overrideExists = false;
        if (!string.IsNullOrWhiteSpace(settings.DatabasePathOverride))
        {
            overrideExists = File.Exists(settings.DatabasePathOverride);
            if (overrideExists)
            {
                path = settings.DatabasePathOverride!;
                return true;
            }
        }
        var def = DefaultPath();
        if (File.Exists(def))
        {
            path = def;
            return true;
        }
        path = null!;
        return false;
    }
}

public static class DayKey
{
    public static string FromStartMs(long startMs)
        => DateTimeOffset.FromUnixTimeMilliseconds(startMs).LocalDateTime
            .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}