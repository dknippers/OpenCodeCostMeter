using Microsoft.Data.Sqlite;

namespace TokenTrackerWidget.Data;

public sealed class SessionTableSanityChecker : ISanityChecker
{
    private readonly string _connectionString;

    public SessionTableSanityChecker(string dbPath)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            DefaultTimeout = 2
        };
        _connectionString = cs.ToString();
    }

    public SanityReport CrossCheckToday(long startOfTodayMs)
    {
        long sIn = 0, sOut = 0, sCr = 0, sCost = 0;
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    COALESCE(SUM(tokens_input),0),
    COALESCE(SUM(tokens_output),0),
    COALESCE(SUM(tokens_cache_read),0),
    COALESCE(SUM(cost),0)
FROM session
WHERE time_updated >= @start;";
        cmd.Parameters.AddWithValue("@start", startOfTodayMs);
        using var r = cmd.ExecuteReader();
        if (r.Read())
        {
            sIn = r.IsDBNull(0) ? 0 : r.GetInt64(0);
            sOut = r.IsDBNull(1) ? 0 : r.GetInt64(1);
            sCr = r.IsDBNull(2) ? 0 : r.GetInt64(2);
            sCost = FormatUtil.ToCents(r.IsDBNull(3) ? 0.0 : r.GetDouble(3));
        }

        long mIn = 0, mOut = 0, mCr = 0, mCost = 0;
        cmd.CommandText = @"
SELECT
    COALESCE(SUM(json_extract(data,'$.tokens.input')),0),
    COALESCE(SUM(json_extract(data,'$.tokens.output')),0),
    COALESCE(SUM(json_extract(data,'$.tokens.cache.read')),0),
    COALESCE(SUM(json_extract(data,'$.cost')),0)
FROM message
WHERE json_extract(data,'$.role')='assistant'
  AND json_extract(data,'$.time.completed') IS NOT NULL
  AND CAST(json_extract(data,'$.time.completed') AS INTEGER) >= @start;";
        using var r2 = cmd.ExecuteReader();
        if (r2.Read())
        {
            mIn = r2.IsDBNull(0) ? 0 : r2.GetInt64(0);
            mOut = r2.IsDBNull(1) ? 0 : r2.GetInt64(1);
            mCr = r2.IsDBNull(2) ? 0 : r2.GetInt64(2);
            mCost = FormatUtil.ToCents(r2.IsDBNull(3) ? 0.0 : r2.GetDouble(3));
        }

        return new SanityReport(mIn, mOut, mCr, mCost, sIn, sOut, sCr, sCost);
    }
}