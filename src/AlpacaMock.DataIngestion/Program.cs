using AlpacaMock.Domain.Market;
using AlpacaMock.Infrastructure.Polygon;
using AlpacaMock.Infrastructure.Postgres;

namespace AlpacaMock.DataIngestion;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        var options = ParseOptions(args.Skip(1).ToArray());

        var connectionString = options.GetValueOrDefault("connection-string")
            ?? options.GetValueOrDefault("c")
            ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");

        var polygonKey = options.GetValueOrDefault("polygon-key")
            ?? options.GetValueOrDefault("k")
            ?? Environment.GetEnvironmentVariable("POLYGON_API_KEY");

        if (string.IsNullOrEmpty(connectionString) && command != "help")
        {
            Console.Error.WriteLine("Error: --connection-string or POSTGRES_CONNECTION_STRING is required");
            return 1;
        }

        try
        {
            return command switch
            {
                "init-db" => await InitDbAsync(connectionString!),
                "load-symbols" => await LoadSymbolsAsync(connectionString!, polygonKey!),
                "load-bars" => await LoadBarsAsync(connectionString!, polygonKey!, options),
                "stats" => await ShowStatsAsync(connectionString!),
                "help" or "--help" or "-h" => ShowHelp(),
                _ => ShowHelp()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static int ShowHelp()
    {
        Console.WriteLine(@"
Polygon Data Ingestion Tool for AlpacaMock

Usage: AlpacaMock.DataIngestion <command> [options]

Commands:
  init-db        Initialize the database schema
  load-symbols   Load all symbols from Polygon
  load-bars      Load historical bars for symbols
  stats          Show database statistics

Options:
  -c, --connection-string  PostgreSQL connection string (or POSTGRES_CONNECTION_STRING env var)
  -k, --polygon-key        Polygon.io API key (or POLYGON_API_KEY env var)

load-bars Options:
  -s, --symbol      Specific symbol to load (default: all)
  --from            Start date YYYY-MM-DD (default: 5 years ago)
  --to              End date YYYY-MM-DD (default: yesterday)
  -r, --resolution  Bar resolution: minute or daily (default: minute)

Examples:
  dotnet run -- init-db -c ""Host=localhost;Database=alpaca;User Id=postgres;Password=postgres""
  dotnet run -- load-symbols -c ""..."" -k ""your_polygon_key""
  dotnet run -- load-bars -c ""..."" -k ""..."" -s AAPL --from 2020-01-01 --to 2024-12-31
  dotnet run -- stats -c ""...""
");
        return 0;
    }

    static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--"))
            {
                var key = arg[2..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    options[key] = args[++i];
                }
                else
                {
                    options[key] = "true";
                }
            }
            else if (arg.StartsWith("-") && arg.Length == 2)
            {
                var key = arg[1..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    options[key] = args[++i];
                }
                else
                {
                    options[key] = "true";
                }
            }
        }

        return options;
    }

    static async Task<int> InitDbAsync(string connectionString)
    {
        Console.WriteLine("Initializing database...");
        await DatabaseSetup.InitializeAsync(connectionString);
        Console.WriteLine("Database initialized successfully.");
        return 0;
    }

    static async Task<int> LoadSymbolsAsync(string connectionString, string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            Console.Error.WriteLine("Error: --polygon-key is required");
            return 1;
        }

        Console.WriteLine("Loading symbols from Polygon...");

        using var polygon = new PolygonClient(apiKey);
        var tickers = await polygon.GetTickersAsync();

        Console.WriteLine($"Found {tickers.Count} symbols");

        await using var dataSource = Npgsql.NpgsqlDataSource.Create(connectionString);
        await using var conn = await dataSource.OpenConnectionAsync();

        var inserted = 0;
        foreach (var ticker in tickers)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO symbols (symbol, name, exchange, status, updated_at)
                VALUES ($1, $2, $3, $4, NOW())
                ON CONFLICT (symbol) DO UPDATE SET
                    name = EXCLUDED.name,
                    exchange = EXCLUDED.exchange,
                    status = EXCLUDED.status,
                    updated_at = NOW()";

            cmd.Parameters.AddWithValue(ticker.Ticker);
            cmd.Parameters.AddWithValue(ticker.Name ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue(ticker.PrimaryExchange ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue(ticker.Active ? "active" : "inactive");

            await cmd.ExecuteNonQueryAsync();
            inserted++;

            if (inserted % 1000 == 0)
                Console.WriteLine($"  Inserted {inserted} symbols...");
        }

        Console.WriteLine($"Loaded {inserted} symbols successfully.");
        return 0;
    }

    static async Task<int> LoadBarsAsync(string connectionString, string apiKey, Dictionary<string, string> options)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            Console.Error.WriteLine("Error: --polygon-key is required");
            return 1;
        }

        var symbol = options.GetValueOrDefault("symbol") ?? options.GetValueOrDefault("s");
        var fromStr = options.GetValueOrDefault("from");
        var toStr = options.GetValueOrDefault("to");
        var resolution = options.GetValueOrDefault("resolution") ?? options.GetValueOrDefault("r") ?? "minute";

        var from = string.IsNullOrEmpty(fromStr)
            ? DateOnly.FromDateTime(DateTime.Now.AddYears(-5))
            : DateOnly.Parse(fromStr);

        var to = string.IsNullOrEmpty(toStr)
            ? DateOnly.FromDateTime(DateTime.Now.AddDays(-1))
            : DateOnly.Parse(toStr);

        Console.WriteLine($"Loading {resolution} bars from {from} to {to}...");

        using var polygon = new PolygonClient(apiKey);
        await using var barRepo = new BarRepository(connectionString);

        var symbols = new List<string>();

        if (!string.IsNullOrEmpty(symbol))
        {
            symbols.Add(symbol);
        }
        else
        {
            symbols.AddRange(await barRepo.GetSymbolsAsync());
            Console.WriteLine($"Found {symbols.Count} symbols to process");
        }

        var barResolution = resolution.ToLowerInvariant() == "daily"
            ? BarResolution.Day
            : BarResolution.Minute;

        var processed = 0;
        var totalBars = 0L;
        var errors = new List<string>();

        foreach (var sym in symbols)
        {
            try
            {
                Console.Write($"  {sym}... ");

                IReadOnlyList<Bar> bars = barResolution == BarResolution.Day
                    ? await polygon.GetDailyBarsAsync(sym, from, to)
                    : await polygon.GetMinuteBarsAsync(sym, from, to);

                if (bars.Count > 0)
                {
                    await barRepo.InsertBarsAsync(bars, barResolution);

                    await barRepo.UpdateCoverageAsync(new DataCoverage
                    {
                        Symbol = sym,
                        Resolution = barResolution,
                        StartDate = from,
                        EndDate = to,
                        LastUpdated = DateTime.UtcNow
                    });

                    Console.WriteLine($"{bars.Count} bars");
                    totalBars += bars.Count;
                }
                else
                {
                    Console.WriteLine("no data");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                errors.Add($"{sym}: {ex.Message}");
            }

            processed++;

            // Small delay to be nice to the API
            if (processed % 10 == 0)
                await Task.Delay(100);
        }

        Console.WriteLine();
        Console.WriteLine($"Completed: {processed} symbols, {totalBars:N0} bars loaded");

        if (errors.Count > 0)
        {
            Console.WriteLine($"Errors ({errors.Count}):");
            foreach (var error in errors.Take(10))
                Console.WriteLine($"  {error}");
            if (errors.Count > 10)
                Console.WriteLine($"  ... and {errors.Count - 10} more");
        }

        return 0;
    }

    static async Task<int> ShowStatsAsync(string connectionString)
    {
        Console.WriteLine("Database Statistics:");
        Console.WriteLine("====================");

        var stats = await DatabaseSetup.GetStatsAsync(connectionString);

        Console.WriteLine($"Symbols: {stats.SymbolCount:N0}");
        Console.WriteLine();
        Console.WriteLine("Minute Bars:");
        Console.WriteLine($"  Count: {stats.MinuteBarCount:N0}");
        if (stats.MinuteDataStart.HasValue && stats.MinuteDataEnd.HasValue)
            Console.WriteLine($"  Range: {stats.MinuteDataStart:yyyy-MM-dd} to {stats.MinuteDataEnd:yyyy-MM-dd}");
        Console.WriteLine();
        Console.WriteLine("Daily Bars:");
        Console.WriteLine($"  Count: {stats.DailyBarCount:N0}");
        if (stats.DailyDataStart.HasValue && stats.DailyDataEnd.HasValue)
            Console.WriteLine($"  Range: {stats.DailyDataStart:yyyy-MM-dd} to {stats.DailyDataEnd:yyyy-MM-dd}");

        return 0;
    }
}
