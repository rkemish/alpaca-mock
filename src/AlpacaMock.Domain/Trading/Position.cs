namespace AlpacaMock.Domain.Trading;

/// <summary>
/// Represents a position in a security following Alpaca API structure.
///
/// Position Lifecycle:
/// 1. Created when first shares are bought/shorted
/// 2. Updated on each fill (quantity and avg price adjust)
/// 3. Closed when quantity reaches zero
///
/// Average Price Calculation:
/// - Adding to position: weighted average of existing and new shares
/// - Reducing position: avg price remains unchanged (cost basis method)
/// - Flipping position: new avg price from the flip trade
///
/// P&L Calculation:
/// - UnrealizedPnL = (CurrentPrice - AvgEntryPrice) × Qty (for long)
/// - UnrealizedPnL = (AvgEntryPrice - CurrentPrice) × |Qty| (for short)
///
/// Reference: https://alpaca.markets/docs/trading/positions/
/// </summary>
public class Position
{
    /// <summary>Unique position identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Session this position belongs to (for backtesting isolation).</summary>
    public required string SessionId { get; init; }

    /// <summary>Account that holds this position.</summary>
    public required string AccountId { get; init; }

    /// <summary>Ticker symbol (e.g., "AAPL").</summary>
    public required string Symbol { get; init; }

    /// <summary>Alpaca asset UUID if resolved.</summary>
    public string? AssetId { get; init; }

    /// <summary>Primary exchange where the asset trades.</summary>
    public string Exchange { get; init; } = "NASDAQ";

    /// <summary>Asset class: us_equity, crypto, or us_option.</summary>
    public AssetClass AssetClass { get; init; } = AssetClass.UsEquity;

    /// <summary>
    /// Volume-weighted average entry price.
    /// Updated when adding to position, maintained when reducing.
    /// Reset when position is closed and reopened.
    /// </summary>
    public decimal AvgEntryPrice { get; set; }

    /// <summary>
    /// Total quantity held.
    /// Positive = long position (own shares).
    /// Negative = short position (owe shares).
    /// Zero = no position (closed).
    /// </summary>
    public decimal Qty { get; set; }

    /// <summary>
    /// Position direction derived from quantity sign.
    /// Long if qty ≥ 0, Short if qty &lt; 0.
    /// </summary>
    public PositionSide Side => Qty >= 0 ? PositionSide.Long : PositionSide.Short;

    /// <summary>
    /// Current market value of the position.
    /// For long: |Qty| × CurrentPrice (positive value).
    /// For short: |Qty| × CurrentPrice (shown as negative).
    /// </summary>
    public decimal MarketValue { get; set; }

    /// <summary>
    /// Original cost to acquire the position.
    /// Calculated as: |Qty| × AvgEntryPrice.
    /// </summary>
    public decimal CostBasis => Math.Abs(Qty) * AvgEntryPrice;

    /// <summary>
    /// Unrealized profit/loss since position was opened.
    /// Calculated as MarketValue - CostBasis (with sign adjustment for shorts).
    /// </summary>
    public decimal UnrealizedPnL { get; set; }

    /// <summary>
    /// Unrealized P&L as a decimal percentage of cost basis.
    /// Example: 0.05 = 5% gain.
    /// </summary>
    public decimal UnrealizedPnLPercent => CostBasis != 0
        ? UnrealizedPnL / CostBasis
        : 0;

    /// <summary>
    /// Unrealized P&L for today only (since last close).
    /// Calculated using LastDayPrice as reference.
    /// </summary>
    public decimal UnrealizedIntradayPnL { get; set; }

    /// <summary>
    /// Current market price of the underlying asset.
    /// Updated from latest bar or quote data.
    /// </summary>
    public decimal CurrentPrice { get; set; }

    /// <summary>
    /// Previous trading day's closing price.
    /// Used to calculate intraday P&L and daily change.
    /// </summary>
    public decimal LastDayPrice { get; set; }

    /// <summary>
    /// Percentage change from previous close.
    /// Calculated as: (CurrentPrice - LastDayPrice) / LastDayPrice.
    /// Example: 0.02 = 2% up from yesterday's close.
    /// </summary>
    public decimal ChangeToday => LastDayPrice != 0
        ? (CurrentPrice - LastDayPrice) / LastDayPrice
        : 0;

    /// <summary>
    /// Applies a trade fill to update position quantity and average price.
    ///
    /// Average Price Logic:
    /// - Opening new position: avg price = fill price
    /// - Adding to position: weighted average of existing + new shares
    /// - Reducing position: avg price unchanged (FIFO cost basis)
    /// - Closing position: avg price reset to 0
    /// - Flipping position (long→short or short→long): avg price = fill price
    ///
    /// Example: Own 100 @ $50, buy 100 @ $60 = 200 @ $55 avg
    /// Example: Own 100 @ $50, sell 50 @ $60 = 50 @ $50 avg (unchanged)
    /// </summary>
    /// <param name="fillQty">Number of shares filled (always positive)</param>
    /// <param name="fillPrice">Execution price of the fill</param>
    /// <param name="side">Buy or Sell</param>
    public void ApplyFill(decimal fillQty, decimal fillPrice, OrderSide side)
    {
        // Convert fill to signed quantity: buys add, sells subtract
        var signedQty = side == OrderSide.Buy ? fillQty : -fillQty;
        var newQty = Qty + signedQty;

        if (newQty == 0)
        {
            // Position fully closed - reset average price
            // Realized P&L would be calculated separately
            AvgEntryPrice = 0;
            Qty = 0;
        }
        else if (Qty == 0)
        {
            // Opening a new position from zero
            // Set avg price to fill price
            Qty = newQty;
            AvgEntryPrice = fillPrice;
        }
        else if (Math.Sign(newQty) != Math.Sign(Qty))
        {
            // Flipping from long to short or vice versa
            // The flip quantity uses the new fill price
            // Example: Long 100, sell 150 = Short 50 @ fill price
            Qty = newQty;
            AvgEntryPrice = fillPrice;
        }
        else if (IsAddingToPosition(side))
        {
            // Adding to existing position (buy on long, sell short on short)
            // Calculate weighted average price
            var totalCost = (Math.Abs(Qty) * AvgEntryPrice) + (fillQty * fillPrice);
            Qty = newQty;
            AvgEntryPrice = totalCost / Math.Abs(Qty);
        }
        else
        {
            // Reducing position (sell on long, buy to cover on short)
            // Average entry price remains unchanged (FIFO method)
            Qty = newQty;
            // AvgEntryPrice stays the same - this is important for P&L tracking
        }
    }

    /// <summary>
    /// Determines if the given order side adds to the current position.
    ///
    /// Adding to position:
    /// - Long position + Buy = adding (increases long exposure)
    /// - Short position + Sell = adding (increases short exposure)
    ///
    /// Reducing position:
    /// - Long position + Sell = reducing (decreasing long exposure)
    /// - Short position + Buy = reducing (covering short)
    /// </summary>
    private bool IsAddingToPosition(OrderSide side)
    {
        return (Side == PositionSide.Long && side == OrderSide.Buy) ||
               (Side == PositionSide.Short && side == OrderSide.Sell);
    }

    /// <summary>
    /// Updates current price and recalculates P&L metrics.
    ///
    /// Call this method whenever price data is updated (new bar, quote, etc.)
    /// to keep position valuations current.
    ///
    /// Calculations:
    /// - MarketValue = |Qty| × CurrentPrice (negative for shorts)
    /// - UnrealizedPnL = MarketValue - CostBasis (sign-adjusted)
    /// - UnrealizedIntradayPnL = |Qty| × (CurrentPrice - LastDayPrice)
    /// </summary>
    /// <param name="currentPrice">Latest market price</param>
    /// <param name="lastDayPrice">Previous day's close (optional update)</param>
    public void UpdatePrices(decimal currentPrice, decimal? lastDayPrice = null)
    {
        CurrentPrice = currentPrice;
        if (lastDayPrice.HasValue)
            LastDayPrice = lastDayPrice.Value;

        // Market value: positive for long, negative for short
        MarketValue = Math.Abs(Qty) * CurrentPrice * (Side == PositionSide.Short ? -1 : 1);

        // Unrealized P&L: difference between current value and cost
        // For shorts, cost basis is negative (liability), so signs work out
        UnrealizedPnL = MarketValue - (CostBasis * (Side == PositionSide.Short ? -1 : 1));

        // Intraday P&L: change from yesterday's close
        UnrealizedIntradayPnL = Math.Abs(Qty) * (CurrentPrice - LastDayPrice);
    }
}

/// <summary>
/// Indicates whether a position is long (owns shares) or short (owes shares).
/// </summary>
public enum PositionSide
{
    /// <summary>Long position - owns shares, profits when price rises.</summary>
    Long,

    /// <summary>Short position - owes shares, profits when price falls.</summary>
    Short
}
