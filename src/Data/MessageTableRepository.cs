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
    COALESCE(SUM(cost), 0),
    COALESCE(SUM(tokens_input), 0),
    COALESCE(SUM(tokens_output), 0),
    COALESCE(SUM(tokens_reasoning), 0),
    COALESCE(SUM(tokens_cache_read), 0),
    COALESCE(SUM(tokens_cache_write), 0)
FROM (
    SELECT
        json_extract(data, '$.providerID') AS providerID,
        json_extract(data, '$.modelID') AS modelID,
        json_extract(data, '$.cost') AS cost,
        json_extract(data, '$.tokens.input') AS tokens_input,
        json_extract(data, '$.tokens.output') AS tokens_output,
        json_extract(data, '$.tokens.reasoning') AS tokens_reasoning,
        json_extract(data, '$.tokens.cache.read') AS tokens_cache_read,
        json_extract(data, '$.tokens.cache.write') AS tokens_cache_write
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
        long input = 0, output = 0, reasoning = 0, cacheRead = 0, cacheWrite = 0;
        double costUsd = 0;
        List<ModelBreakdown> breakdowns = new();

        using (var conn = new SqliteConnection(_connectionString))
        {
            conn.Open();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = PerModelSql;
                cmd.Parameters.AddWithValue("@start", startOfTodayMs);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var mCost = GetDouble(r, 2);
                    var mInput = GetInt64(r, 3);
                    var mOutput = GetInt64(r, 4);
                    var mReasoning = GetInt64(r, 5);
                    var mCacheRead = GetInt64(r, 6);
                    var mCacheWrite = GetInt64(r, 7);

                    costUsd += mCost;
                    input += mInput;
                    output += mOutput;
                    reasoning += mReasoning;
                    cacheRead += mCacheRead;
                    cacheWrite += mCacheWrite;

                    breakdowns.Add(new ModelBreakdown(
                        Provider: GetString(r, 0),
                        Model: GetString(r, 1),
                        Cost: mCost,
                        Input: mInput,
                        Output: mOutput,
                        CacheRead: mCacheRead));
                }
            }
        }

        return new DayUsageSnapshot(
            DayKey: DayKey.FromStartMs(startOfTodayMs),
            Input: input, Output: output, Reasoning: reasoning,
            CacheRead: cacheRead, CacheWrite: cacheWrite,
            Cost: costUsd,
            Models: breakdowns,
            TakenAt: DateTimeOffset.Now);
    }

    private static long GetInt64(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetInt64(i);
    private static double GetDouble(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0.0 : r.GetDouble(i);
    private static string GetString(SqliteDataReader r, int i) => r.IsDBNull(i) ? string.Empty : r.GetString(i);
}