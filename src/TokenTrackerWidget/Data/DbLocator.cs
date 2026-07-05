using System.Globalization;
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

    public static string Resolve(WidgetSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.DatabasePathOverride)
            && File.Exists(settings.DatabasePathOverride))
        {
            return settings.DatabasePathOverride;
        }
        return DefaultPath();
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

internal static class DbStringExtensions
{
    public static string SafeTrim(this string? s) => s ?? string.Empty;
}

public static class DayKey
{
    public static string FromOffset(DateTimeOffset now)
        => now.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string FromStartMs(long startMs)
        => DateTimeOffset.FromUnixTimeMilliseconds(startMs).LocalDateTime
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}