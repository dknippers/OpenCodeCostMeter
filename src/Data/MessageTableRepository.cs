using Microsoft.Data.Sqlite;
using OpenCodeCostMeter.Models;

namespace OpenCodeCostMeter.Data;

public sealed class MessageTableRepository : IUsageRepository
{
    // Only opencode v2 is supported: every message lives in `session_message`,
    // and assistant rows carry a nested model object in `data`.
    //
    // The inner GROUP BY (time.created, time.completed) deduplicates forked messages,
    // which are cloned verbatim with the same timestamps and cost.
    private const string PerModelSql = @"
SELECT
    COALESCE(providerID, ''),
    COALESCE(modelID, ''),
    COALESCE(SUM(cost), 0)
FROM (
    SELECT
        json_extract(data, '$.model.providerID') AS providerID,
        json_extract(data, '$.model.id') AS modelID,
        json_extract(data, '$.cost') AS cost,
        json_extract(data, '$.time.created') AS created,
        json_extract(data, '$.time.completed') AS completed
    FROM session_message
    WHERE type = 'assistant'
      -- Uses the indexed row timestamp to avoid parsing historical message JSON.
      -- A request created before midnight but completed today will not be included.
      AND time_created >= @start
      AND CAST(json_extract(data, '$.time.completed') AS INTEGER) >= @start
    GROUP BY created, completed
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

            var sql = SchemaIsSupported(conn) ? PerModelSql : null;
            if (sql is null)
                return new DayUsageSnapshot(
                    DayKey: DayKey.FromStartMs(startOfTodayMs),
                    Cost: 0,
                    Models: breakdowns,
                    TakenAt: DateTimeOffset.Now);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
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

    // Checked on every poll rather than cached so a live schema change is picked up
    // without restarting the widget.
    private static bool SchemaIsSupported(SqliteConnection conn) => TableExists(conn, "session_message");

    private static bool TableExists(SqliteConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @name LIMIT 1";
        cmd.Parameters.AddWithValue("@name", tableName);
        return cmd.ExecuteScalar() is not null;
    }

    private static double GetDouble(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0.0 : r.GetDouble(i);
    private static string GetString(SqliteDataReader r, int i) => r.IsDBNull(i) ? string.Empty : r.GetString(i);
}
