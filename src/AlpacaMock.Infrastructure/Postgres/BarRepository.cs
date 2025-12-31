using AlpacaMock.Domain.Market;
using Npgsql;

namespace AlpacaMock.Infrastructure.Postgres;

/// <summary>
/// Repository for accessing Polygon bar data stored in TimescaleDB.
/// </summary>
public class BarRepository : IAsyncDisposable
{
    private readonly string _connectionString;
    private NpgsqlDataSource? _dataSource;

    public BarRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private NpgsqlDataSource DataSource =>
        _dataSource ??= NpgsqlDataSource.Create(_connectionString);

    /// <summary>
    /// Gets a single bar for a symbol at a specific time.
    /// </summary>
    public async Task<Bar?> GetBarAsync(
        string symbol,
        DateTimeOffset timestamp,
        BarResolution resolution = BarResolution.Minute,
        CancellationToken cancellationToken = default)
    {
        var tableName = GetTableName(resolution);

        await using var cmd = DataSource.CreateCommand($@"
            SELECT time, symbol, open, high, low, close, volume, vwap, transactions
            FROM {tableName}
            WHERE symbol = $1 AND time <= $2
            ORDER BY time DESC
            LIMIT 1");

        cmd.Parameters.AddWithValue(symbol.ToUpperInvariant());
        cmd.Parameters.AddWithValue(timestamp);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadBar(reader);
        }

        return null;
    }

    /// <summary>
    /// Gets bars for a symbol within a time range.
    /// </summary>
    public async Task<IReadOnlyList<Bar>> GetBarsAsync(
        string symbol,
        DateTimeOffset start,
        DateTimeOffset end,
        BarResolution resolution = BarResolution.Minute,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var tableName = GetTableName(resolution);
        var limitClause = limit.HasValue ? $"LIMIT {limit.Value}" : "";

        await using var cmd = DataSource.CreateCommand($@"
            SELECT time, symbol, open, high, low, close, volume, vwap, transactions
            FROM {tableName}
            WHERE symbol = $1 AND time >= $2 AND time < $3
            ORDER BY time ASC
            {limitClause}");

        cmd.Parameters.AddWithValue(symbol.ToUpperInvariant());
        cmd.Parameters.AddWithValue(start);
        cmd.Parameters.AddWithValue(end);

        var bars = new List<Bar>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            bars.Add(ReadBar(reader));
        }

        return bars;
    }

    /// <summary>
    /// Gets the latest bar for each of the specified symbols.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, Bar>> GetLatestBarsAsync(
        IEnumerable<string> symbols,
        DateTimeOffset asOf,
        BarResolution resolution = BarResolution.Minute,
        CancellationToken cancellationToken = default)
    {
        var tableName = GetTableName(resolution);
        var symbolList = symbols.Select(s => s.ToUpperInvariant()).ToList();

        if (symbolList.Count == 0)
            return new Dictionary<string, Bar>();

        // Use DISTINCT ON for efficient latest-per-symbol query
        await using var cmd = DataSource.CreateCommand($@"
            SELECT DISTINCT ON (symbol)
                time, symbol, open, high, low, close, volume, vwap, transactions
            FROM {tableName}
            WHERE symbol = ANY($1) AND time <= $2
            ORDER BY symbol, time DESC");

        cmd.Parameters.AddWithValue(symbolList.ToArray());
        cmd.Parameters.AddWithValue(asOf);

        var result = new Dictionary<string, Bar>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var bar = ReadBar(reader);
            result[bar.Symbol] = bar;
        }

        return result;
    }

    /// <summary>
    /// Inserts bars in batch.
    /// </summary>
    public async Task InsertBarsAsync(
        IEnumerable<Bar> bars,
        BarResolution resolution = BarResolution.Minute,
        CancellationToken cancellationToken = default)
    {
        var tableName = GetTableName(resolution);
        var barList = bars.ToList();

        if (barList.Count == 0) return;

        await using var conn = await DataSource.OpenConnectionAsync(cancellationToken);

        // Use binary COPY for high-performance bulk insert
        await using var writer = await conn.BeginBinaryImportAsync(
            $"COPY {tableName} (time, symbol, open, high, low, close, volume, vwap, transactions) FROM STDIN (FORMAT BINARY)",
            cancellationToken);

        foreach (var bar in barList)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(bar.Timestamp, NpgsqlTypes.NpgsqlDbType.TimestampTz, cancellationToken);
            await writer.WriteAsync(bar.Symbol.ToUpperInvariant(), cancellationToken);
            await writer.WriteAsync(bar.Open, cancellationToken);
            await writer.WriteAsync(bar.High, cancellationToken);
            await writer.WriteAsync(bar.Low, cancellationToken);
            await writer.WriteAsync(bar.Close, cancellationToken);
            await writer.WriteAsync(bar.Volume, cancellationToken);
            await writer.WriteAsync(bar.Vwap, cancellationToken);
            await writer.WriteAsync(bar.Transactions, cancellationToken);
        }

        await writer.CompleteAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the data coverage for a symbol.
    /// </summary>
    public async Task<DataCoverage?> GetCoverageAsync(
        string symbol,
        BarResolution resolution = BarResolution.Minute,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = DataSource.CreateCommand(@"
            SELECT symbol, resolution, start_date, end_date, last_updated
            FROM data_coverage
            WHERE symbol = $1 AND resolution = $2");

        cmd.Parameters.AddWithValue(symbol.ToUpperInvariant());
        cmd.Parameters.AddWithValue(resolution.ToString().ToLowerInvariant());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            return new DataCoverage
            {
                Symbol = reader.GetString(0),
                Resolution = Enum.Parse<BarResolution>(reader.GetString(1), ignoreCase: true),
                StartDate = DateOnly.FromDateTime(reader.GetDateTime(2)),
                EndDate = DateOnly.FromDateTime(reader.GetDateTime(3)),
                LastUpdated = reader.GetDateTime(4)
            };
        }

        return null;
    }

    /// <summary>
    /// Updates data coverage tracking.
    /// </summary>
    public async Task UpdateCoverageAsync(
        DataCoverage coverage,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = DataSource.CreateCommand(@"
            INSERT INTO data_coverage (symbol, resolution, start_date, end_date, last_updated)
            VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (symbol, resolution) DO UPDATE SET
                start_date = LEAST(data_coverage.start_date, EXCLUDED.start_date),
                end_date = GREATEST(data_coverage.end_date, EXCLUDED.end_date),
                last_updated = EXCLUDED.last_updated");

        cmd.Parameters.AddWithValue(coverage.Symbol.ToUpperInvariant());
        cmd.Parameters.AddWithValue(coverage.Resolution.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue(coverage.StartDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue(coverage.EndDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue(coverage.LastUpdated);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Gets all available symbols.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetSymbolsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var cmd = DataSource.CreateCommand(
            "SELECT DISTINCT symbol FROM symbols WHERE status = 'active' ORDER BY symbol");

        var symbols = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            symbols.Add(reader.GetString(0));
        }

        return symbols;
    }

    private static string GetTableName(BarResolution resolution) => resolution switch
    {
        BarResolution.Minute => "bars_minute",
        BarResolution.Hour => "bars_hour",
        BarResolution.Day => "bars_daily",
        BarResolution.Week => "bars_weekly",
        BarResolution.Month => "bars_monthly",
        _ => "bars_minute"
    };

    private static Bar ReadBar(NpgsqlDataReader reader) => new()
    {
        Timestamp = reader.GetDateTime(0),
        Symbol = reader.GetString(1),
        Open = reader.GetDecimal(2),
        High = reader.GetDecimal(3),
        Low = reader.GetDecimal(4),
        Close = reader.GetDecimal(5),
        Volume = reader.GetInt64(6),
        Vwap = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
        Transactions = reader.IsDBNull(8) ? null : reader.GetInt32(8)
    };

    public async ValueTask DisposeAsync()
    {
        if (_dataSource != null)
        {
            await _dataSource.DisposeAsync();
        }
    }
}

public class DataCoverage
{
    public required string Symbol { get; init; }
    public BarResolution Resolution { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public DateTime LastUpdated { get; init; }
}
