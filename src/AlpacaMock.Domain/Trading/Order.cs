namespace AlpacaMock.Domain.Trading;

/// <summary>
/// Represents a trading order following Alpaca API structure.
///
/// Alpaca Order Lifecycle:
/// 1. New -> Order received, not yet acknowledged by exchange
/// 2. Accepted -> Order acknowledged by exchange, eligible for matching
/// 3. PartiallyFilled -> Part of order quantity has been filled
/// 4. Filled -> Entire order quantity has been filled (terminal state)
/// 5. Cancelled -> Order cancelled by user or system (terminal state)
/// 6. Expired -> Order expired due to time-in-force rules (terminal state)
/// 7. Rejected -> Order rejected for validation failure (terminal state)
///
/// Reference: https://alpaca.markets/docs/trading/orders/
/// </summary>
public class Order
{
    /// <summary>Unique order identifier assigned by the system.</summary>
    public required string Id { get; init; }

    /// <summary>Session this order belongs to (for backtesting isolation).</summary>
    public required string SessionId { get; init; }

    /// <summary>Account that placed this order.</summary>
    public required string AccountId { get; init; }

    /// <summary>
    /// Client-provided order ID for idempotency and tracking.
    /// Alpaca uses this for order deduplication within a 24-hour window.
    /// </summary>
    public string? ClientOrderId { get; init; }

    /// <summary>Ticker symbol (e.g., "AAPL", "TSLA").</summary>
    public required string Symbol { get; init; }

    /// <summary>Alpaca asset UUID if resolved.</summary>
    public string? AssetId { get; init; }

    /// <summary>Asset class: us_equity, crypto, or us_option.</summary>
    public AssetClass AssetClass { get; init; } = AssetClass.UsEquity;

    /// <summary>
    /// Number of shares to trade. Mutually exclusive with Notional for market orders.
    /// Fractional shares are supported for eligible securities.
    /// </summary>
    public required decimal Qty { get; set; }

    /// <summary>
    /// Dollar amount for notional orders. Only valid for market orders.
    /// Alpaca calculates quantity based on current price.
    /// </summary>
    public decimal? Notional { get; init; }

    /// <summary>Quantity that has been filled so far.</summary>
    public decimal FilledQty { get; set; }

    /// <summary>Volume-weighted average price of all fills.</summary>
    public decimal? FilledAvgPrice { get; set; }

    /// <summary>Order type: market, limit, stop, stop_limit, or trailing_stop.</summary>
    public OrderType Type { get; init; } = OrderType.Market;

    /// <summary>Order side: buy or sell.</summary>
    public OrderSide Side { get; init; }

    /// <summary>
    /// Time-in-force controls how long the order remains active.
    /// See TimeInForce enum for detailed behavior of each value.
    /// </summary>
    public TimeInForce TimeInForce { get; init; } = TimeInForce.Day;

    /// <summary>
    /// Limit price for limit and stop-limit orders.
    /// Order will only fill at this price or better.
    /// Decimal precision: max 2 for ≥$1, max 4 for <$1.
    /// </summary>
    public decimal? LimitPrice { get; init; }

    /// <summary>
    /// Stop/trigger price for stop and stop-limit orders.
    /// Buy stops trigger when price rises to this level.
    /// Sell stops trigger when price falls to this level.
    /// </summary>
    public decimal? StopPrice { get; init; }

    /// <summary>Dollar offset for trailing stop orders.</summary>
    public decimal? TrailPrice { get; init; }

    /// <summary>Percentage offset for trailing stop orders (0.01 = 1%).</summary>
    public decimal? TrailPercent { get; init; }

    /// <summary>Current order status in the lifecycle.</summary>
    public OrderStatus Status { get; set; } = OrderStatus.New;

    /// <summary>
    /// If true, order can execute during extended hours (pre-market/after-hours).
    /// Only limit orders with TIF=DAY are allowed for extended hours.
    /// </summary>
    public bool ExtendedHours { get; init; }

    /// <summary>When the order was submitted to the system.</summary>
    public DateTimeOffset SubmittedAt { get; init; }

    /// <summary>When the order was completely filled (null if not filled).</summary>
    public DateTimeOffset? FilledAt { get; set; }

    /// <summary>When the order expired due to TIF rules (null if not expired).</summary>
    public DateTimeOffset? ExpiredAt { get; set; }

    /// <summary>When the order was cancelled (null if not cancelled).</summary>
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>When the order was rejected (null if not failed).</summary>
    public DateTimeOffset? FailedAt { get; set; }

    /// <summary>ID of order that replaced this one (for order modifications).</summary>
    public string? ReplacedBy { get; set; }

    /// <summary>ID of order this one replaced (for order modifications).</summary>
    public string? Replaces { get; init; }

    /// <summary>
    /// Calculates the remaining quantity to fill.
    /// </summary>
    public decimal RemainingQty => Qty - FilledQty;

    /// <summary>
    /// Checks if this order is in a terminal state (cannot be modified).
    /// </summary>
    public bool IsTerminal => Status is OrderStatus.Filled or OrderStatus.Cancelled
        or OrderStatus.Expired or OrderStatus.Rejected or OrderStatus.Replaced;

    /// <summary>
    /// Checks if this order is active and can receive fills.
    /// </summary>
    public bool IsActive => Status is OrderStatus.New or OrderStatus.Accepted
        or OrderStatus.PendingNew or OrderStatus.PartiallyFilled;

    /// <summary>
    /// Checks if a GTC order has expired (90 days from submission).
    /// Alpaca automatically cancels GTC orders after 90 days.
    /// </summary>
    public bool IsGtcExpired(DateTimeOffset currentTime)
    {
        if (TimeInForce != TimeInForce.Gtc) return false;
        return currentTime >= SubmittedAt.AddDays(90);
    }

    /// <summary>
    /// For IOC (Immediate or Cancel) orders: determines if remaining quantity should be cancelled.
    /// IOC orders cancel any unfilled portion immediately after partial fill.
    /// </summary>
    public bool ShouldCancelRemainingIoc()
    {
        return TimeInForce == TimeInForce.Ioc && RemainingQty > 0;
    }

    /// <summary>
    /// For FOK (Fill or Kill) orders: determines if order should be rejected.
    /// FOK orders must be filled entirely in a single fill or rejected.
    /// </summary>
    public bool ShouldRejectFok(decimal availableQty)
    {
        return TimeInForce == TimeInForce.Fok && availableQty < Qty;
    }

    /// <summary>
    /// Checks if this is a day order that should expire at end of trading session.
    /// </summary>
    public bool IsDayOrderExpired(DateTimeOffset currentTime)
    {
        if (TimeInForce != TimeInForce.Day) return false;
        // Day orders expire if current date is past submission date
        return currentTime.Date > SubmittedAt.Date;
    }

    /// <summary>
    /// Checks if this order can be filled at the given price.
    /// </summary>
    public bool CanFillAtPrice(decimal price, decimal high, decimal low)
    {
        return Type switch
        {
            OrderType.Market => true,
            OrderType.Limit => Side == OrderSide.Buy
                ? low <= LimitPrice
                : high >= LimitPrice,
            OrderType.Stop => Side == OrderSide.Buy
                ? high >= StopPrice
                : low <= StopPrice,
            OrderType.StopLimit => Side == OrderSide.Buy
                ? high >= StopPrice && low <= LimitPrice
                : low <= StopPrice && high >= LimitPrice,
            _ => false
        };
    }

    /// <summary>
    /// Gets the execution price for this order given bar data.
    /// </summary>
    public decimal GetExecutionPrice(decimal open, decimal high, decimal low, decimal close)
    {
        return Type switch
        {
            // Market orders fill at open
            OrderType.Market => open,

            // Limit orders fill at limit price if touched
            OrderType.Limit => LimitPrice!.Value,

            // Stop orders become market orders, fill at stop price or worse
            OrderType.Stop => Side == OrderSide.Buy
                ? Math.Max(open, StopPrice!.Value)
                : Math.Min(open, StopPrice!.Value),

            // Stop-limit fills at limit after stop triggered
            OrderType.StopLimit => LimitPrice!.Value,

            _ => open
        };
    }
}

/// <summary>
/// Order execution types supported by Alpaca.
/// </summary>
public enum OrderType
{
    /// <summary>Execute immediately at best available price.</summary>
    Market,

    /// <summary>Execute at specified price or better. Buys at limit or lower, sells at limit or higher.</summary>
    Limit,

    /// <summary>Trigger a market order when stop price is reached. Converts to market order on trigger.</summary>
    Stop,

    /// <summary>Trigger a limit order when stop price is reached. Converts to limit order on trigger.</summary>
    StopLimit,

    /// <summary>
    /// Stop price trails the market by a fixed amount or percentage.
    /// Adjusts stop price as the market moves favorably.
    /// </summary>
    TrailingStop
}

/// <summary>
/// Order direction (buy or sell).
/// </summary>
public enum OrderSide
{
    /// <summary>Open or add to a long position.</summary>
    Buy,

    /// <summary>Close a long position or open a short position.</summary>
    Sell
}

/// <summary>
/// Order lifecycle status values per Alpaca API.
/// Terminal states: Filled, Cancelled, Expired, Rejected, Replaced.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order received but not yet acknowledged by exchange.</summary>
    New,

    /// <summary>Order acknowledged by exchange and eligible for matching.</summary>
    Accepted,

    /// <summary>Order being sent to exchange (transient state).</summary>
    PendingNew,

    /// <summary>Part of the order has been filled, remainder still open.</summary>
    PartiallyFilled,

    /// <summary>Entire order quantity has been filled. Terminal state.</summary>
    Filled,

    /// <summary>Order completed for the day (for day orders with partial fills).</summary>
    DoneForDay,

    /// <summary>Order was cancelled by user or system. Terminal state.</summary>
    Cancelled,

    /// <summary>Order expired per time-in-force rules. Terminal state.</summary>
    Expired,

    /// <summary>Order was replaced by another order. Terminal state.</summary>
    Replaced,

    /// <summary>Cancel request is pending (transient state).</summary>
    PendingCancel,

    /// <summary>Replace request is pending (transient state).</summary>
    PendingReplace,

    /// <summary>Order rejected due to validation or risk failure. Terminal state.</summary>
    Rejected
}

/// <summary>
/// Time-in-force determines how long an order remains active.
/// Reference: https://alpaca.markets/docs/trading/orders/#time-in-force
/// </summary>
public enum TimeInForce
{
    /// <summary>
    /// Day order - expires at end of regular trading session (4 PM ET).
    /// If submitted after market close, good for next trading day.
    /// </summary>
    Day,

    /// <summary>
    /// Good Till Cancelled - remains active until filled or cancelled.
    /// Alpaca auto-expires GTC orders after 90 calendar days.
    /// </summary>
    Gtc,

    /// <summary>
    /// Market On Open - executes at market open price.
    /// Must be submitted before market opens. Limit/market orders only.
    /// </summary>
    Opg,

    /// <summary>
    /// Market On Close - executes at or near market close price.
    /// Must be submitted during trading hours. Limited availability.
    /// </summary>
    Cls,

    /// <summary>
    /// Immediate Or Cancel - execute available quantity immediately, cancel rest.
    /// Partial fills are possible. Any unfilled portion is immediately cancelled.
    /// </summary>
    Ioc,

    /// <summary>
    /// Fill Or Kill - must fill entire quantity or reject.
    /// No partial fills allowed. Order is rejected if not fully fillable.
    /// </summary>
    Fok
}

/// <summary>
/// Asset class categories supported by Alpaca.
/// </summary>
public enum AssetClass
{
    /// <summary>US equities (stocks) traded on major exchanges.</summary>
    UsEquity,

    /// <summary>Cryptocurrency pairs (e.g., BTC/USD).</summary>
    Crypto,

    /// <summary>US equity options contracts.</summary>
    UsOption
}
