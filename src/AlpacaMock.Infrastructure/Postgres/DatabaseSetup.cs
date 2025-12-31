using Npgsql;

namespace AlpacaMock.Infrastructure.Postgres;

/// <summary>
/// Sets up the PostgreSQL/TimescaleDB schema for bar data.
/// </summary>
public static class DatabaseSetup
{
    /// <summary>
    /// Creates all required tables and indexes.
    /// </summary>
    public static async Task InitializeAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);

        // Enable TimescaleDB extension
        await ExecuteAsync(conn, "CREATE EXTENSION IF NOT EXISTS timescaledb CASCADE;", cancellationToken);

        // Create minute bars table
        await ExecuteAsync(conn, @"
            CREATE TABLE IF NOT EXISTS bars_minute (
                time        TIMESTAMPTZ NOT NULL,
                symbol      TEXT NOT NULL,
                open        NUMERIC(12,4) NOT NULL,
                high        NUMERIC(12,4) NOT NULL,
                low         NUMERIC(12,4) NOT NULL,
                close       NUMERIC(12,4) NOT NULL,
                volume      BIGINT NOT NULL,
                vwap        NUMERIC(12,4),
                transactions INT
            );", cancellationToken);

        // Convert to hypertable if not already
        await ExecuteAsync(conn, @"
            SELECT create_hypertable('bars_minute', 'time',
                chunk_time_interval => INTERVAL '1 month',
                if_not_exists => TRUE);", cancellationToken);

        // Create daily bars table
        await ExecuteAsync(conn, @"
            CREATE TABLE IF NOT EXISTS bars_daily (
                time        TIMESTAMPTZ NOT NULL,
                symbol      TEXT NOT NULL,
                open        NUMERIC(12,4) NOT NULL,
                high        NUMERIC(12,4) NOT NULL,
                low         NUMERIC(12,4) NOT NULL,
                close       NUMERIC(12,4) NOT NULL,
                volume      BIGINT NOT NULL,
                vwap        NUMERIC(12,4),
                transactions INT
            );", cancellationToken);

        await ExecuteAsync(conn, @"
            SELECT create_hypertable('bars_daily', 'time',
                chunk_time_interval => INTERVAL '1 year',
                if_not_exists => TRUE);", cancellationToken);

        // Create indexes for efficient lookups
        await ExecuteAsync(conn, @"
            CREATE INDEX IF NOT EXISTS idx_bars_minute_symbol_time
            ON bars_minute (symbol, time DESC);", cancellationToken);

        await ExecuteAsync(conn, @"
            CREATE INDEX IF NOT EXISTS idx_bars_daily_symbol_time
            ON bars_daily (symbol, time DESC);", cancellationToken);

        // Create symbols reference table
        await ExecuteAsync(conn, @"
            CREATE TABLE IF NOT EXISTS symbols (
                symbol      TEXT PRIMARY KEY,
                name        TEXT,
                exchange    TEXT,
                asset_class TEXT DEFAULT 'us_equity',
                status      TEXT DEFAULT 'active',
                tradable    BOOLEAN DEFAULT TRUE,
                marginable  BOOLEAN DEFAULT TRUE,
                shortable   BOOLEAN DEFAULT TRUE,
                fractionable BOOLEAN DEFAULT TRUE,
                updated_at  TIMESTAMPTZ DEFAULT NOW()
            );", cancellationToken);

        // Create data coverage tracking table
        await ExecuteAsync(conn, @"
            CREATE TABLE IF NOT EXISTS data_coverage (
                symbol      TEXT NOT NULL,
                resolution  TEXT NOT NULL,
                start_date  DATE NOT NULL,
                end_date    DATE NOT NULL,
                last_updated TIMESTAMPTZ DEFAULT NOW(),
                PRIMARY KEY (symbol, resolution)
            );", cancellationToken);

        // Enable compression on hypertables (for cost savings)
        await ExecuteAsync(conn, @"
            ALTER TABLE bars_minute SET (
                timescaledb.compress,
                timescaledb.compress_segmentby = 'symbol'
            );", cancellationToken);

        await ExecuteAsync(conn, @"
            ALTER TABLE bars_daily SET (
                timescaledb.compress,
                timescaledb.compress_segmentby = 'symbol'
            );", cancellationToken);

        // Add compression policy (compress chunks older than 7 days)
        try
        {
            await ExecuteAsync(conn, @"
                SELECT add_compression_policy('bars_minute', INTERVAL '7 days', if_not_exists => TRUE);",
                cancellationToken);

            await ExecuteAsync(conn, @"
                SELECT add_compression_policy('bars_daily', INTERVAL '30 days', if_not_exists => TRUE);",
                cancellationToken);
        }
        catch
        {
            // Compression policies may fail if already exist or not supported
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection conn, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Gets database statistics for monitoring.
    /// </summary>
    public static async Task<DatabaseStats> GetStatsAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);

        var stats = new DatabaseStats();

        // Get row counts
        await using (var cmd = new NpgsqlCommand(
            "SELECT (SELECT COUNT(*) FROM bars_minute), (SELECT COUNT(*) FROM bars_daily)", conn))
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                stats.MinuteBarCount = reader.GetInt64(0);
                stats.DailyBarCount = reader.GetInt64(1);
            }
        }

        // Get date range
        await using (var cmd = new NpgsqlCommand(@"
            SELECT MIN(time), MAX(time) FROM bars_minute
            UNION ALL
            SELECT MIN(time), MAX(time) FROM bars_daily", conn))
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0)) stats.MinuteDataStart = reader.GetDateTime(0);
                if (!reader.IsDBNull(1)) stats.MinuteDataEnd = reader.GetDateTime(1);
            }
            if (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0)) stats.DailyDataStart = reader.GetDateTime(0);
                if (!reader.IsDBNull(1)) stats.DailyDataEnd = reader.GetDateTime(1);
            }
        }

        // Get symbol count
        await using (var cmd = new NpgsqlCommand("SELECT COUNT(DISTINCT symbol) FROM symbols", conn))
        {
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            stats.SymbolCount = Convert.ToInt32(result);
        }

        return stats;
    }
}

public class DatabaseStats
{
    public long MinuteBarCount { get; set; }
    public long DailyBarCount { get; set; }
    public DateTime? MinuteDataStart { get; set; }
    public DateTime? MinuteDataEnd { get; set; }
    public DateTime? DailyDataStart { get; set; }
    public DateTime? DailyDataEnd { get; set; }
    public int SymbolCount { get; set; }
}
