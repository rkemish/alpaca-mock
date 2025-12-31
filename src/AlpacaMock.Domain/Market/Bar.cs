namespace AlpacaMock.Domain.Market;

/// <summary>
/// Represents an OHLCV bar from Polygon data.
/// </summary>
public record Bar
{
    public required string Symbol { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required decimal Open { get; init; }
    public required decimal High { get; init; }
    public required decimal Low { get; init; }
    public required decimal Close { get; init; }
    public required long Volume { get; init; }
    public decimal? Vwap { get; init; }
    public int? Transactions { get; init; }
}

/// <summary>
/// Bar resolution/timeframe.
/// </summary>
public enum BarResolution
{
    Minute,
    Hour,
    Day,
    Week,
    Month
}
