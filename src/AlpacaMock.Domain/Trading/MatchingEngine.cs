using AlpacaMock.Domain.Market;

namespace AlpacaMock.Domain.Trading;

/// <summary>
/// Simulates order matching and execution against historical bar (OHLCV) data.
///
/// This engine provides realistic trade simulation by:
/// - Matching orders against bar price ranges (not just close prices)
/// - Applying slippage based on bar volatility
/// - Limiting fills based on volume participation
/// - Handling time-in-force rules (IOC, FOK, Day, GTC)
///
/// Execution Model:
/// - Market orders: Fill at bar open with slippage
/// - Limit orders: Fill at limit price if bar range touches limit
/// - Stop orders: Trigger when bar range touches stop, fill as market
/// - Stop-limit orders: Trigger at stop, then fill at limit if price available
///
/// Slippage Model:
/// Uses 10% of bar's high-low range as adverse slippage:
/// - Buys execute higher than theoretical price
/// - Sells execute lower than theoretical price
///
/// Volume Constraint:
/// Orders are limited to 1% of bar volume to simulate market impact.
/// Large orders may receive partial fills across multiple bars.
/// </summary>
public class MatchingEngine
{
    /// <summary>
    /// Attempts to fill an order against the given bar.
    /// </summary>
    /// <param name="order">The order to attempt filling</param>
    /// <param name="bar">The OHLCV bar data to match against</param>
    /// <returns>FillResult indicating success/failure and fill details</returns>
    public FillResult TryFill(Order order, Bar bar)
    {
        // Cannot fill orders that are already in terminal state
        if (order.Status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Expired)
            return FillResult.NoFill("Order is already closed");

        // Check if price conditions allow execution
        // Limit orders need price to touch limit, stops need trigger price hit
        if (!order.CanFillAtPrice(bar.Close, bar.High, bar.Low))
            return FillResult.NoFill("Price conditions not met");

        // Calculate execution price based on order type and bar OHLC
        var executionPrice = order.GetExecutionPrice(bar.Open, bar.High, bar.Low, bar.Close);

        // Apply slippage - buys execute higher, sells execute lower
        // This models the bid-ask spread and market impact
        executionPrice = ApplySlippage(executionPrice, bar, order.Side);

        var fillQty = order.RemainingQty;

        // Volume constraint: limit order to 1% of bar volume
        // This prevents unrealistic fills that would move the market
        var maxFillFromVolume = CalculateMaxFillFromVolume(bar.Volume, order.Side);
        if (fillQty > maxFillFromVolume && maxFillFromVolume > 0)
        {
            fillQty = maxFillFromVolume;
        }

        return new FillResult(
            Filled: true,
            FillQty: fillQty,
            FillPrice: executionPrice,
            Timestamp: bar.Timestamp,
            IsPartial: fillQty < order.RemainingQty
        );
    }

    /// <summary>
    /// Applies a realistic slippage model based on bar volatility.
    ///
    /// Slippage represents the difference between expected price and actual execution price,
    /// caused by bid-ask spread and market impact. Uses 10% of bar's high-low range.
    ///
    /// Slippage is always adverse to the trader:
    /// - Buys: execute at a higher price than expected
    /// - Sells: execute at a lower price than expected
    /// </summary>
    /// <param name="price">The theoretical execution price</param>
    /// <param name="bar">Bar data used to calculate volatility-based slippage</param>
    /// <param name="side">Order side determines slippage direction</param>
    /// <returns>Adjusted price after slippage</returns>
    private static decimal ApplySlippage(decimal price, Bar bar, OrderSide side)
    {
        // Calculate bar's trading range as a proxy for volatility/liquidity
        var range = bar.High - bar.Low;
        if (range <= 0) return price;

        // Slippage factor: 10% of bar range
        // Higher volatility bars = more slippage (wider effective spread)
        const decimal slippageFactor = 0.1m;
        var slippage = range * slippageFactor;

        // Apply slippage adversely based on order direction
        // Ensure we stay within the bar's actual trading range
        return side == OrderSide.Buy
            ? Math.Min(bar.High, price + slippage)
            : Math.Max(bar.Low, price - slippage);
    }

    /// <summary>
    /// Calculates the maximum fill quantity based on bar volume.
    ///
    /// Participation rate limits prevent unrealistic simulation where
    /// a single order could consume all of a bar's volume. The 1% limit
    /// represents a conservative estimate of market impact avoidance.
    ///
    /// In practice, institutional traders aim for 10-20% participation,
    /// but lower rates are safer for backtesting to avoid overfitting.
    /// </summary>
    /// <param name="volume">Bar's total trading volume</param>
    /// <param name="side">Order side (for future asymmetric models)</param>
    /// <returns>Maximum quantity that can be filled from this bar</returns>
    private static decimal CalculateMaxFillFromVolume(long volume, OrderSide side)
    {
        // Conservative 1% participation rate
        // Larger orders will need multiple bars to fill completely
        const decimal volumeParticipationRate = 0.01m;
        return volume * volumeParticipationRate;
    }

    /// <summary>
    /// Processes all pending orders for a session as time advances through bars.
    ///
    /// This is the main order processing loop called during simulation time advancement.
    /// It handles order expiration, time-in-force rules, and fill attempts.
    ///
    /// Processing order:
    /// 1. Skip terminal orders (already filled/cancelled/expired)
    /// 2. Handle missing bar data (IOC/FOK fail immediately)
    /// 3. Check Day order expiration
    /// 4. Check GTC order expiration (90 days per Alpaca rules)
    /// 5. Apply time-in-force specific logic (IOC, FOK, etc.)
    /// 6. Attempt fill against bar data
    /// </summary>
    /// <param name="pendingOrders">Orders to process (typically filtered to active orders)</param>
    /// <param name="currentBars">Current bar data keyed by symbol</param>
    /// <param name="currentTime">Current simulation time for expiration checks</param>
    /// <returns>Enumerable of orders that received fills with their fill details</returns>
    public IEnumerable<(Order Order, FillResult Fill)> ProcessOrders(
        IEnumerable<Order> pendingOrders,
        IReadOnlyDictionary<string, Bar> currentBars,
        DateTimeOffset currentTime)
    {
        foreach (var order in pendingOrders)
        {
            // Skip orders that are already in terminal state
            // These cannot receive additional fills
            if (order.IsTerminal)
                continue;

            // Check if we have bar data for this symbol
            // No bar data = no trading activity = cannot fill
            if (!currentBars.TryGetValue(order.Symbol, out var bar))
            {
                // IOC/FOK orders must execute immediately or fail
                // Without bar data, they cannot execute
                if (order.TimeInForce == TimeInForce.Ioc || order.TimeInForce == TimeInForce.Fok)
                {
                    HandleIocFokNoData(order, currentTime);
                }
                continue;
            }

            // Day orders expire at end of trading session
            // If simulation time is past submission date, order has expired
            if (order.IsDayOrderExpired(currentTime))
            {
                order.Status = OrderStatus.Expired;
                order.ExpiredAt = currentTime;
                continue;
            }

            // GTC orders auto-expire after 90 calendar days per Alpaca rules
            if (order.IsGtcExpired(currentTime))
            {
                order.Status = OrderStatus.Expired;
                order.ExpiredAt = currentTime;
                continue;
            }

            // Apply time-in-force specific processing logic
            // IOC and FOK have special immediate execution requirements
            var fill = order.TimeInForce switch
            {
                TimeInForce.Ioc => ProcessIocOrder(order, bar, currentTime),
                TimeInForce.Fok => ProcessFokOrder(order, bar, currentTime),
                _ => TryFill(order, bar)
            };

            if (fill.Filled)
            {
                yield return (order, fill);
            }
        }
    }

    /// <summary>
    /// Processes IOC (Immediate or Cancel) order.
    /// Fills what's available immediately, cancels any remaining quantity.
    /// </summary>
    public FillResult ProcessIocOrder(Order order, Bar bar, DateTimeOffset currentTime)
    {
        var fill = TryFill(order, bar);

        if (!fill.Filled)
        {
            // No fill possible - cancel the entire order
            order.Status = OrderStatus.Cancelled;
            order.CancelledAt = currentTime;
            return FillResult.NoFill("IOC order could not be filled immediately");
        }

        // If partial fill, the remaining will be cancelled after applying the fill
        // The caller should check order.ShouldCancelRemainingIoc() after applying the fill

        return fill;
    }

    /// <summary>
    /// Processes FOK (Fill or Kill) order.
    /// Must fill the entire order quantity or reject completely.
    /// </summary>
    public FillResult ProcessFokOrder(Order order, Bar bar, DateTimeOffset currentTime)
    {
        // First check if we can fill at all
        if (!order.CanFillAtPrice(bar.Close, bar.High, bar.Low))
        {
            order.Status = OrderStatus.Rejected;
            order.FailedAt = currentTime;
            return FillResult.NoFill("FOK order price conditions not met");
        }

        // Check if volume supports full fill
        var maxFillFromVolume = CalculateMaxFillFromVolume(bar.Volume, order.Side);
        if (maxFillFromVolume < order.Qty)
        {
            order.Status = OrderStatus.Rejected;
            order.FailedAt = currentTime;
            return FillResult.NoFill("FOK order could not be filled entirely due to volume constraints");
        }

        // Can fill completely
        var executionPrice = order.GetExecutionPrice(bar.Open, bar.High, bar.Low, bar.Close);
        executionPrice = ApplySlippage(executionPrice, bar, order.Side);

        return new FillResult(
            Filled: true,
            FillQty: order.Qty,
            FillPrice: executionPrice,
            Timestamp: bar.Timestamp,
            IsPartial: false
        );
    }

    /// <summary>
    /// Handles IOC/FOK orders when no bar data is available.
    /// </summary>
    private static void HandleIocFokNoData(Order order, DateTimeOffset currentTime)
    {
        if (order.TimeInForce == TimeInForce.Fok)
        {
            order.Status = OrderStatus.Rejected;
            order.FailedAt = currentTime;
        }
        else if (order.TimeInForce == TimeInForce.Ioc)
        {
            order.Status = OrderStatus.Cancelled;
            order.CancelledAt = currentTime;
        }
    }

    /// <summary>
    /// Expires all GTC orders that have passed their 90-day limit.
    /// </summary>
    public void ExpireGtcOrders(IEnumerable<Order> orders, DateTimeOffset currentTime)
    {
        foreach (var order in orders.Where(o => o.IsGtcExpired(currentTime) && o.IsActive))
        {
            order.Status = OrderStatus.Expired;
            order.ExpiredAt = currentTime;
        }
    }

    /// <summary>
    /// Expires all day orders at end of trading session.
    /// </summary>
    public void ExpireDayOrders(IEnumerable<Order> orders, DateTimeOffset currentTime)
    {
        foreach (var order in orders.Where(o => o.IsDayOrderExpired(currentTime) && o.IsActive))
        {
            order.Status = OrderStatus.Expired;
            order.ExpiredAt = currentTime;
        }
    }
}

/// <summary>
/// Represents the result of an order fill attempt.
///
/// A FillResult captures whether an order was filled, and if so, the details of the execution.
/// Partial fills are indicated by IsPartial=true, meaning only part of the order quantity was filled.
///
/// For failed fills (Filled=false), the Error property contains the reason for failure.
/// </summary>
/// <param name="Filled">True if any quantity was filled</param>
/// <param name="FillQty">Quantity that was filled (0 if not filled)</param>
/// <param name="FillPrice">Execution price including slippage (0 if not filled)</param>
/// <param name="Timestamp">When the fill occurred</param>
/// <param name="IsPartial">True if only part of remaining quantity was filled</param>
/// <param name="Error">Error message if fill failed</param>
public record FillResult(
    bool Filled,
    decimal FillQty = 0,
    decimal FillPrice = 0,
    DateTimeOffset? Timestamp = null,
    bool IsPartial = false,
    string? Error = null)
{
    /// <summary>
    /// Creates a failed fill result with an optional reason.
    /// </summary>
    public static FillResult NoFill(string? reason = null) =>
        new(Filled: false, Error: reason);
}
