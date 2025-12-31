using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AlpacaMock.Domain.Market;

namespace AlpacaMock.Infrastructure.Polygon;

/// <summary>
/// Client for Polygon.io REST API.
/// </summary>
public class PolygonClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string BaseUrl = "https://api.polygon.io";

    public PolygonClient(string apiKey, HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress = new Uri(BaseUrl);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Gets aggregate bars for a symbol.
    /// </summary>
    public async Task<IReadOnlyList<Bar>> GetAggsAsync(
        string symbol,
        int multiplier,
        string timespan,
        DateOnly from,
        DateOnly to,
        bool adjusted = true,
        string sort = "asc",
        int limit = 50000,
        CancellationToken cancellationToken = default)
    {
        var url = $"/v2/aggs/ticker/{symbol.ToUpperInvariant()}/range/{multiplier}/{timespan}/{from:yyyy-MM-dd}/{to:yyyy-MM-dd}";
        url += $"?adjusted={adjusted.ToString().ToLower()}&sort={sort}&limit={limit}";

        var allBars = new List<Bar>();
        string? nextUrl = url;

        while (!string.IsNullOrEmpty(nextUrl))
        {
            var response = await _httpClient.GetFromJsonAsync<PolygonAggsResponse>(nextUrl, _jsonOptions, cancellationToken);

            if (response?.Results != null)
            {
                foreach (var agg in response.Results)
                {
                    allBars.Add(new Bar
                    {
                        Symbol = symbol.ToUpperInvariant(),
                        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(agg.T),
                        Open = agg.O,
                        High = agg.H,
                        Low = agg.L,
                        Close = agg.C,
                        Volume = (long)agg.V,
                        Vwap = agg.Vw,
                        Transactions = agg.N
                    });
                }
            }

            nextUrl = response?.NextUrl;
        }

        return allBars;
    }

    /// <summary>
    /// Gets minute bars for a symbol.
    /// </summary>
    public Task<IReadOnlyList<Bar>> GetMinuteBarsAsync(
        string symbol,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
        => GetAggsAsync(symbol, 1, "minute", from, to, cancellationToken: cancellationToken);

    /// <summary>
    /// Gets daily bars for a symbol.
    /// </summary>
    public Task<IReadOnlyList<Bar>> GetDailyBarsAsync(
        string symbol,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
        => GetAggsAsync(symbol, 1, "day", from, to, cancellationToken: cancellationToken);

    /// <summary>
    /// Gets all tickers (symbols).
    /// </summary>
    public async Task<IReadOnlyList<PolygonTicker>> GetTickersAsync(
        string market = "stocks",
        bool active = true,
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        var allTickers = new List<PolygonTicker>();
        string? cursor = null;

        do
        {
            var url = $"/v3/reference/tickers?market={market}&active={active.ToString().ToLower()}&limit={limit}";
            if (!string.IsNullOrEmpty(cursor))
                url += $"&cursor={cursor}";

            var response = await _httpClient.GetFromJsonAsync<PolygonTickersResponse>(url, _jsonOptions, cancellationToken);

            if (response?.Results != null)
            {
                allTickers.AddRange(response.Results);
            }

            cursor = response?.NextUrl != null
                ? ExtractCursor(response.NextUrl)
                : null;

        } while (!string.IsNullOrEmpty(cursor));

        return allTickers;
    }

    /// <summary>
    /// Gets ticker details.
    /// </summary>
    public async Task<PolygonTickerDetails?> GetTickerDetailsAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        var url = $"/v3/reference/tickers/{symbol.ToUpperInvariant()}";
        var response = await _httpClient.GetFromJsonAsync<PolygonTickerDetailsResponse>(url, _jsonOptions, cancellationToken);
        return response?.Results;
    }

    /// <summary>
    /// Gets the previous day's bar for a symbol.
    /// </summary>
    public async Task<Bar?> GetPreviousCloseAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        var url = $"/v2/aggs/ticker/{symbol.ToUpperInvariant()}/prev";
        var response = await _httpClient.GetFromJsonAsync<PolygonAggsResponse>(url, _jsonOptions, cancellationToken);

        var agg = response?.Results?.FirstOrDefault();
        if (agg == null) return null;

        return new Bar
        {
            Symbol = symbol.ToUpperInvariant(),
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(agg.T),
            Open = agg.O,
            High = agg.H,
            Low = agg.L,
            Close = agg.C,
            Volume = (long)agg.V,
            Vwap = agg.Vw,
            Transactions = agg.N
        };
    }

    private static string? ExtractCursor(string url)
    {
        var uri = new Uri(url);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        return query["cursor"];
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

#region Response Models

public class PolygonAggsResponse
{
    public string? Ticker { get; set; }
    public int QueryCount { get; set; }
    public int ResultsCount { get; set; }
    public bool Adjusted { get; set; }
    public List<PolygonAgg>? Results { get; set; }
    public string? Status { get; set; }
    public string? RequestId { get; set; }
    public string? NextUrl { get; set; }
}

public class PolygonAgg
{
    [JsonPropertyName("t")]
    public long T { get; set; }  // Timestamp (Unix ms)

    [JsonPropertyName("o")]
    public decimal O { get; set; }  // Open

    [JsonPropertyName("h")]
    public decimal H { get; set; }  // High

    [JsonPropertyName("l")]
    public decimal L { get; set; }  // Low

    [JsonPropertyName("c")]
    public decimal C { get; set; }  // Close

    [JsonPropertyName("v")]
    public decimal V { get; set; }  // Volume (decimal because Polygon sometimes returns floats)

    [JsonPropertyName("vw")]
    public decimal? Vw { get; set; }  // VWAP

    [JsonPropertyName("n")]
    public int? N { get; set; }  // Number of transactions
}

public class PolygonTickersResponse
{
    public List<PolygonTicker>? Results { get; set; }
    public string? Status { get; set; }
    public int Count { get; set; }
    public string? NextUrl { get; set; }
}

public class PolygonTicker
{
    public string Ticker { get; set; } = "";
    public string? Name { get; set; }
    public string? Market { get; set; }
    public string? Locale { get; set; }
    public string? PrimaryExchange { get; set; }
    public string? Type { get; set; }
    public bool Active { get; set; }
    public string? CurrencyName { get; set; }
    public string? Cik { get; set; }
    public string? CompositeFigi { get; set; }
    public string? ShareClassFigi { get; set; }
    public string? LastUpdatedUtc { get; set; }
}

public class PolygonTickerDetailsResponse
{
    public PolygonTickerDetails? Results { get; set; }
    public string? Status { get; set; }
    public string? RequestId { get; set; }
}

public class PolygonTickerDetails
{
    public string Ticker { get; set; } = "";
    public string? Name { get; set; }
    public string? Market { get; set; }
    public string? Locale { get; set; }
    public string? PrimaryExchange { get; set; }
    public string? Type { get; set; }
    public bool Active { get; set; }
    public string? CurrencyName { get; set; }
    public string? Description { get; set; }
    public string? HomepageUrl { get; set; }
    public long? TotalEmployees { get; set; }
    public long? ListDate { get; set; }
    public long? ShareClassSharesOutstanding { get; set; }
    public long? WeightedSharesOutstanding { get; set; }
    public decimal? MarketCap { get; set; }
}

#endregion
