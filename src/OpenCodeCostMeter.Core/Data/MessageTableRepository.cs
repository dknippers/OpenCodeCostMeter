using Microsoft.Data.Sqlite;
using OpenCodeCostMeter.Models;

namespace OpenCodeCostMeter.Data;

public sealed class MessageTableRepository : IUsageRepository
{
    // Single query: extract all JSON fields once per row in the inner subquery,
    // then aggregate per provider/model in the outer query.
    // Inner GROUP BY (time.created, time.completed) deduplicates forked messages
    // — forking clones messages verbatim (same timestamps, same cost), so without
    // this, forked sessions would double-count their entire message history.
    private const string PerModelSql = @"
SELECT
    COALESCE(providerID, ''),
    COALESCE(modelID, ''),
    COALESCE(SUM(cost), 0)
FROM (
    SELECT
        json_extract(data, '$.providerID') AS providerID,
        json_extract(data, '$.modelID') AS modelID,
        json_extract(data, '$.cost') AS cost
    FROM message
    WHERE json_extract(data, '$.role') = 'assistant'
      AND json_extract(data, '$.time.completed') IS NOT NULL
      AND CAST(json_extract(data, '$.time.completed') AS INTEGER) >= @start
    GROUP BY json_extract(data, '$.time.created'),
             json_extract(data, '$.time.completed')
)
GROUP BY providerID, modelID
ORDER BY SUM(cost) DESC, modelID ASC;";

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
        double costUsd = 0;
        List<ModelBreakdown> breakdowns = [];

        using (var conn = new SqliteConnection(_connectionString))
        {
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = PerModelSql;
            cmd.Parameters.AddWithValue("@start", startOfTodayMs);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var mCost = GetDouble(r, 2);

                costUsd += mCost;

                breakdowns.Add(new ModelBreakdown(
                    Provider: GetString(r, 0),
                    Model: GetString(r, 1),
                    Cost: mCost));
            }
        }

        return new DayUsageSnapshot(
            DayKey: DayKey.FromStartMs(startOfTodayMs),
            Cost: costUsd,
            Models: breakdowns,
            TakenAt: DateTimeOffset.Now);
    }

    private static double GetDouble(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0.0 : r.GetDouble(i);
    private static string GetString(SqliteDataReader r, int i) => r.IsDBNull(i) ? string.Empty : r.GetString(i);
}