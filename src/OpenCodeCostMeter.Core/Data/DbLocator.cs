namespace OpenCodeCostMeter.Data;

public static class DbLocator
{
    public static string DefaultPath()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, ".local", "share", "opencode", "opencode.db");
    }

    public static string? ResolveDatabasePath(string? commandLinePath)
    {
        if (!string.IsNullOrWhiteSpace(commandLinePath))
        {
            if (File.Exists(commandLinePath))
                return commandLinePath;
            return null;
        }

        var def = DefaultPath();
        return File.Exists(def) ? def : null;
    }
}

public static class DayKey
{
    public static string FromStartMs(long startMs)
        => DateTimeOffset.FromUnixTimeMilliseconds(startMs).LocalDateTime
            .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}