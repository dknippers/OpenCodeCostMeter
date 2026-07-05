using System.Globalization;
using Microsoft.Data.Sqlite;
using TokenTrackerWidget.Models;

namespace TokenTrackerWidget.Data;

public sealed class MessageTableRepository : IUsageRepository
{
    private const string TotalsSql = @"
SELECT
    COUNT(*),
    COALESCE(SUM(json_extract(data, '$.tokens.input')), 0),
    COALESCE(SUM(json_extract(data, '$.tokens.output')), 0),
    COALESCE(SUM(json_extract(data, '$.tokens.reasoning')), 0),
    COALESCE(SUM(json_extract(data, '$.tokens.cache.read')), 0),
    COALESCE(SUM(json_extract(data, '$.tokens.cache.write')), 0),
    COALESCE(SUM(json_extract(data, '$.cost')), 0)
FROM message
WHERE json_extract(data, '$.role') = 'assistant'
  AND json_extract(data, '$.time.completed') IS NOT NULL
  AND CAST(json_extract(data, '$.time.completed') AS INTEGER) >= @start;";

    private const string PerModelSql = @"
SELECT
    COALESCE(json_extract(data, '$.providerID'), ''),
    COALESCE(json_extract(data, '$.modelID'), ''),
    COUNT(*),
    COALESCE(SUM(json_extract(data, '$.cost')), 0),
    COALESCE(SUM(json_extract(data, '$.tokens.input')), 0),
    COALESCE(SUM(json_extract(data, '$.tokens.output')), 0),
    COALESCE(SUM(json_extract(data, '$.tokens.cache.read')), 0)
FROM message
WHERE json_extract(data, '$.role') = 'assistant'
  AND json_extract(data, '$.time.completed') IS NOT NULL
  AND CAST(json_extract(data, '$.time.completed') AS INTEGER) >= @start
GROUP BY COALESCE(json_extract(data, '$.providerID'), ''),
         COALESCE(json_extract(data, '$.modelID'), '')
ORDER BY SUM(json_extract(data, '$.cost')) DESC;";

    private const string ActiveSessionSql = @"
SELECT title, model, time_updated
FROM session
ORDER BY time_updated DESC
LIMIT 1;";

    private readonly string _connectionString;

    public MessageTableRepository(string dbPath)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            DefaultTimeout = 2
        };
        _connectionString = cs.ToString();
    }

    public DayUsageSnapshot GetToday(long startOfTodayMs)
    {
        long input = 0, output = 0, reasoning = 0, cacheRead = 0, cacheWrite = 0;
        long costCents = 0;
        int calls = 0;
        List<ModelBreakdown> breakdowns = new();
        string? activeTitle = null;
        string? activeModel = null;
        bool isLive = false;

        using (var conn = new SqliteConnection(_connectionString))
        {
            conn.Open();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = TotalsSql;
                cmd.Parameters.AddWithValue("@start", startOfTodayMs);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    calls = r.GetInt32(0);
                    input = GetInt64(r, 1);
                    output = GetInt64(r, 2);
                    reasoning = GetInt64(r, 3);
                    cacheRead = GetInt64(r, 4);
                    cacheWrite = GetInt64(r, 5);
                    costCents = FormatUtil.ToCents(GetDouble(r, 6));
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = PerModelSql;
                cmd.Parameters.AddWithValue("@start", startOfTodayMs);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    breakdowns.Add(new ModelBreakdown(
                        Provider: GetString(r, 0),
                        Model: GetString(r, 1),
                        Calls: r.GetInt32(2),
                        Cost: GetDouble(r, 3),
                        Input: GetInt64(r, 4),
                        Output: GetInt64(r, 5),
                        CacheRead: GetInt64(r, 6)));
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = ActiveSessionSql;
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    activeTitle = GetString(r, 0);
                    var modelJson = GetString(r, 1);
                    if (!string.IsNullOrEmpty(modelJson))
                    {
                        activeModel = ExtractModelId(modelJson);
                    }
                    var updatedMs = GetInt64(r, 2);
                    isLive = IsLive(updatedMs);
                }
            }
        }

        return new DayUsageSnapshot(
            DayKey: DayKey.FromStartMs(startOfTodayMs),
            Input: input, Output: output, Reasoning: reasoning,
            CacheRead: cacheRead, CacheWrite: cacheWrite,
            Cost: costCents / 100.0,
            Calls: calls,
            Models: breakdowns,
            ActiveSessionTitle: activeTitle,
            ActiveModel: activeModel,
            IsLive: isLive,
            TakenAt: DateTimeOffset.Now);
    }

    private static bool IsLive(long updatedMs)
    {
        if (updatedMs <= 0) return false;
        var updatedAt = DateTimeOffset.FromUnixTimeMilliseconds(updatedMs);
        return (DateTimeOffset.Now - updatedAt).TotalSeconds <= 60;
    }

    private static string ExtractModelId(string modelJson)
    {
        try
        {
            using var d = System.Text.Json.JsonDocument.Parse(modelJson);
            if (d.RootElement.TryGetProperty("id", out var id))
            {
                var provider = d.RootElement.TryGetProperty("providerID", out var p)
                    ? " (" + p.GetString() + ")"
                    : string.Empty;
                return (id.GetString() ?? "?") + provider;
            }
            return modelJson;
        }
        catch
        {
            return modelJson;
        }
    }

    private static long GetInt64(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetInt64(i);
    private static double GetDouble(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0.0 : r.GetDouble(i);
    private static string GetString(SqliteDataReader r, int i) => r.IsDBNull(i) ? string.Empty : r.GetString(i);
}