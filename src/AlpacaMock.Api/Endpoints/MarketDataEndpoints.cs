using AlpacaMock.Domain.Market;
using AlpacaMock.Infrastructure.Cosmos;
using AlpacaMock.Infrastructure.Postgres;
using Microsoft.Azure.Cosmos;

namespace AlpacaMock.Api.Endpoints;

public static class MarketDataEndpoints
{
    public static void MapMarketDataEndpoints(this WebApplication app)
    {
        var assetsGroup = app.MapGroup("/v1/assets")
            .WithTags("Assets");

        assetsGroup.MapGet("/", ListAssets);
        assetsGroup.MapGet("/{symbolOrId}", GetAsset);
        assetsGroup.MapGet("/{symbol}/bars", GetBars);
        assetsGroup.MapGet("/{symbol}/quotes/latest", GetLatestQuote);
    }

    private static async Task<IResult> ListAssets(
        BarRepository barRepo,
        string? status = null,
        string? asset_class = null)
    {
        var symbols = await barRepo.GetSymbolsAsync();

        var assets = symbols.Select(s => new
        {
            id = Guid.NewGuid().ToString(),
            @class = asset_class ?? "us_equity",
            exchange = "NASDAQ",
            symbol = s,
            name = s,
            status = status ?? "active",
            tradable = true,
            marginable = true,
            shortable = true,
            easy_to_borrow = true,
            fractionable = true
        });

        return Results.Ok(assets);
    }

    private static async Task<IResult> GetAsset(
        string symbolOrId,
        BarRepository barRepo)
    {
        // For simplicity, treat as symbol
        var symbols = await barRepo.GetSymbolsAsync();
        var symbol = symbolOrId.ToUpperInvariant();

        if (!symbols.Contains(symbol))
            return Results.NotFound(new { code = 40410000, message = "Asset not found" });

        return Results.Ok(new
        {
            id = Guid.NewGuid().ToString(),
            @class = "us_equity",
            exchange = "NASDAQ",
            symbol = symbol,
            name = symbol,
            status = "active",
            tradable = true,
            marginable = true,
            shortable = true,
            easy_to_borrow = true,
            fractionable = true
        });
    }

    private static async Task<IResult> GetBars(
        HttpContext context,
        string symbol,
        BarRepository barRepo,
        CosmosDbContext cosmos,
        string? timeframe = null,
        string? start = null,
        string? end = null,
        int? limit = null)
    {
        var sessionId = context.Request.Headers["X-Session-Id"].FirstOrDefault();

        // Determine time range
        DateTimeOffset startTime, endTime;

        if (!string.IsNullOrEmpty(sessionId))
        {
            // Use session's current simulation time
            var session = await GetSessionAsync(cosmos, sessionId);
            if (session != null)
            {
                endTime = session.CurrentSimulationTime;
                startTime = endTime.AddDays(-1);  // Default to last day

                if (!string.IsNullOrEmpty(start))
                    startTime = DateTimeOffset.Parse(start);
            }
            else
            {
                return Results.BadRequest(new { code = 40010000, message = "Invalid session" });
            }
        }
        else
        {
            // Use real dates
            endTime = string.IsNullOrEmpty(end)
                ? DateTimeOffset.UtcNow
                : DateTimeOffset.Parse(end);

            startTime = string.IsNullOrEmpty(start)
                ? endTime.AddDays(-1)
                : DateTimeOffset.Parse(start);
        }

        var resolution = ParseTimeframe(timeframe ?? "1Min");

        var bars = await barRepo.GetBarsAsync(
            symbol.ToUpperInvariant(),
            startTime,
            endTime,
            resolution,
            limit ?? 1000);

        return Results.Ok(new
        {
            bars = bars.Select(b => new
            {
                t = b.Timestamp,
                o = b.Open,
                h = b.High,
                l = b.Low,
                c = b.Close,
                v = b.Volume,
                vw = b.Vwap,
                n = b.Transactions
            }),
            symbol = symbol.ToUpperInvariant(),
            next_page_token = (string?)null
        });
    }

    private static async Task<IResult> GetLatestQuote(
        HttpContext context,
        string symbol,
        BarRepository barRepo,
        CosmosDbContext cosmos)
    {
        var sessionId = context.Request.Headers["X-Session-Id"].FirstOrDefault();

        DateTimeOffset asOf;

        if (!string.IsNullOrEmpty(sessionId))
        {
            var session = await GetSessionAsync(cosmos, sessionId);
            if (session == null)
                return Results.BadRequest(new { code = 40010000, message = "Invalid session" });

            asOf = session.CurrentSimulationTime;
        }
        else
        {
            asOf = DateTimeOffset.UtcNow;
        }

        var bar = await barRepo.GetBarAsync(symbol.ToUpperInvariant(), asOf);

        if (bar == null)
            return Results.NotFound(new { code = 40410000, message = "No quote data available" });

        // Synthesize quote from bar data
        var spread = (bar.High - bar.Low) * 0.001m;  // 0.1% spread estimate

        return Results.Ok(new
        {
            symbol = bar.Symbol,
            quote = new
            {
                t = bar.Timestamp,
                ax = "Q",  // Ask exchange
                ap = bar.Close + spread / 2,  // Ask price
                @as = 100,  // Ask size
                bx = "Q",  // Bid exchange
                bp = bar.Close - spread / 2,  // Bid price
                bs = 100,  // Bid size
                c = new[] { "R" },  // Conditions
                z = "C"  // Tape
            }
        });
    }

    private static async Task<Domain.Sessions.Session?> GetSessionAsync(CosmosDbContext cosmos, string sessionId)
    {
        try
        {
            var response = await cosmos.Sessions.ReadItemAsync<Domain.Sessions.Session>(
                sessionId, new PartitionKey(sessionId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static BarResolution ParseTimeframe(string timeframe)
    {
        return timeframe.ToUpperInvariant() switch
        {
            "1MIN" or "1MINUTE" => BarResolution.Minute,
            "1HOUR" or "1H" => BarResolution.Hour,
            "1DAY" or "1D" => BarResolution.Day,
            "1WEEK" or "1W" => BarResolution.Week,
            "1MONTH" or "1M" => BarResolution.Month,
            _ => BarResolution.Minute
        };
    }
}
