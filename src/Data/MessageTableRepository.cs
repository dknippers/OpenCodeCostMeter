using Microsoft.Data.Sqlite;
using OpenCodeCostMeter.Models;

namespace OpenCodeCostMeter.Data;

public sealed class MessageTableRepository : IUsageRepository
{
    // v1 stores assistant messages in `message` with flat providerID/modelID fields.
    // v2 stores them in `session_message` (type = 'assistant') with a nested model object.
    //
    // Table existence says nothing about the version: late v1 builds already create
    // `session_message` (holding only non-assistant projections such as
    // 'model-switched'), and a fresh v2 database has no `message` table at all.
    // So instead of detecting the version, we read both sources and merge them:
    //  - v2 imports v1 sessions into `session_message` keeping the original message
    //    id, so legacy rows whose id already exists in `session_message` are skipped.
    //  - Anything else in `message` (pure v1, or v1 used after v2 on the same day)
    //    is counted from there.
    //
    // The middle GROUP BY (time.created, time.completed) deduplicates forked messages
    // across both sources — forking clones messages verbatim (same timestamps, same
    // cost), so without this, forked sessions would double-count their history.
    private const string LegacyRowsSql = @"
    SELECT
        json_extract(m.data, '$.providerID') AS providerID,
        json_extract(m.data, '$.modelID') AS modelID,
        json_extract(m.data, '$.cost') AS cost,
        json_extract(m.data, '$.time.created') AS created,
        json_extract(m.data, '$.time.completed') AS completed
    FROM message m
    WHERE json_extract(m.data, '$.role') = 'assistant'
      AND json_extract(m.data, '$.time.completed') IS NOT NULL
      AND CAST(json_extract(m.data, '$.time.completed') AS INTEGER) >= @start
      {0}";

    private const string LegacyNotInSessionMessageSql =
        "AND NOT EXISTS (SELECT 1 FROM session_message sm WHERE sm.id = m.id)";

    private const string SessionMessageRowsSql = @"
    SELECT
        json_extract(data, '$.model.providerID') AS providerID,
        json_extract(data, '$.model.id') AS modelID,
        json_extract(data, '$.cost') AS cost,
        json_extract(data, '$.time.created') AS created,
        json_extract(data, '$.time.completed') AS completed
    FROM session_message
    WHERE type = 'assistant'
      AND json_extract(data, '$.time.completed') IS NOT NULL
      AND CAST(json_extract(data, '$.time.completed') AS INTEGER) >= @start";

    private const string PerModelSql = @"
SELECT
    COALESCE(providerID, ''),
    COALESCE(modelID, ''),
    COALESCE(SUM(cost), 0)
FROM (
    SELECT providerID, modelID, cost
    FROM ({0})
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

            var sql = BuildPerModelSql(conn);
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

    // Checked on every poll rather than cached: opencode can migrate the database to v2
    // (or drop the legacy table) while the widget is running.
    private static string? BuildPerModelSql(SqliteConnection conn)
    {
        var hasLegacy = TableExists(conn, "message");
        var hasSessionMessage = TableExists(conn, "session_message");

        List<string> sources = [];
        if (hasSessionMessage)
            sources.Add(SessionMessageRowsSql);
        if (hasLegacy)
            sources.Add(string.Format(LegacyRowsSql, hasSessionMessage ? LegacyNotInSessionMessageSql : string.Empty));

        return sources.Count == 0
            ? null
            : string.Format(PerModelSql, string.Join("\n    UNION ALL\n", sources));
    }

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
