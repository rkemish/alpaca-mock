namespace AlpacaMock.Domain.Trading;

/// <summary>
/// Tracks day trades and enforces Pattern Day Trader (PDT) rules per FINRA regulations.
///
/// Key Alpaca/FINRA PDT Rules:
/// - A day trade is opening and closing the same security position within the same trading day
/// - PDT status is triggered by 4+ day trades in a rolling 5 business day period
/// - Accounts with equity below $25,000 are limited to 3 day trades per 5 business days
/// - PDT accounts must maintain $25,000 minimum equity to continue day trading
/// - PDT accounts get 4x day trading buying power (vs 2x overnight buying power)
///
/// Reference: https://alpaca.markets/docs/trading/pattern-day-trading/
/// </summary>
public class DayTradeTracker
{
    /// <summary>
    /// Minimum account equity required for unrestricted day trading under PDT rules.
    /// </summary>
    private const decimal PdtMinimumEquity = 25_000m;

    /// <summary>
    /// Maximum number of day trades allowed for non-PDT accounts (accounts under $25k).
    /// A 4th day trade triggers PDT status and restrictions if equity is below minimum.
    /// </summary>
    private const int DayTradeLimit = 3;

    /// <summary>
    /// Number of business days used for the rolling day trade count window.
    /// </summary>
    private const int RollingWindowDays = 5;

    /// <summary>
    /// In-memory storage for trade records. In production, this would be persisted.
    /// </summary>
    private readonly List<TradeRecord> _trades = new();

    /// <summary>
    /// Records a trade for day trade tracking purposes.
    /// Call this method after every order fill to maintain accurate PDT tracking.
    /// </summary>
    /// <param name="accountId">The account that executed the trade</param>
    /// <param name="symbol">The security symbol (e.g., "AAPL")</param>
    /// <param name="side">Whether this was a buy or sell</param>
    /// <param name="qty">The quantity traded</param>
    /// <param name="timestamp">When the trade occurred</param>
    public void RecordTrade(
        string accountId,
        string symbol,
        OrderSide side,
        decimal qty,
        DateTimeOffset timestamp)
    {
        _trades.Add(new TradeRecord(accountId, symbol, side, qty, timestamp));
    }

    /// <summary>
    /// Gets the number of day trades in the rolling 5-day window.
    /// A day trade occurs when you buy and sell (or sell short and cover)
    /// the same security in the same trading day.
    /// </summary>
    /// <param name="accountId">The account to check</param>
    /// <param name="asOf">The reference date for the 5-day window</param>
    /// <returns>Number of day trades in the window</returns>
    public int GetDayTradeCount(string accountId, DateTimeOffset asOf)
    {
        // Calculate the start of the 5-day rolling window
        var windowStart = asOf.AddDays(-RollingWindowDays);
        return CountDayTrades(accountId, windowStart, asOf);
    }

    /// <summary>
    /// Determines if an account should be flagged as a Pattern Day Trader.
    /// PDT status is triggered when 4 or more day trades occur in 5 business days.
    /// Once flagged as PDT, accounts must maintain $25k minimum equity.
    /// </summary>
    /// <param name="accountId">The account to check</param>
    /// <param name="asOf">The reference date for checking PDT status</param>
    /// <returns>True if account qualifies as PDT (4+ day trades)</returns>
    public bool IsPdt(string accountId, DateTimeOffset asOf)
    {
        return GetDayTradeCount(accountId, asOf) >= 4;
    }

    /// <summary>
    /// Checks if executing a new trade would create a day trade.
    /// A day trade occurs when closing a position opened the same day.
    /// Use this to warn users before they execute a potentially flagging trade.
    /// </summary>
    /// <param name="accountId">The account executing the trade</param>
    /// <param name="symbol">The security being traded</param>
    /// <param name="side">The side of the new trade</param>
    /// <param name="timestamp">When the new trade would occur</param>
    /// <returns>True if this trade would constitute a day trade</returns>
    public bool WouldBeDayTrade(
        string accountId,
        string symbol,
        OrderSide side,
        DateTimeOffset timestamp)
    {
        var today = timestamp.Date;

        // A day trade requires an opposite-side trade on the same day for the same symbol
        // e.g., Buy AAPL at 10am, then Sell AAPL at 2pm = 1 day trade
        var oppositeSide = side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;

        return _trades.Any(t =>
            t.AccountId == accountId &&
            t.Symbol == symbol &&
            t.Side == oppositeSide &&
            t.Timestamp.Date == today);
    }

    /// <summary>
    /// Validates if a new trade would violate PDT rules.
    /// This is the main entry point for PDT rule enforcement before order submission.
    /// </summary>
    /// <param name="accountId">The account executing the trade</param>
    /// <param name="symbol">The security being traded</param>
    /// <param name="side">The side of the trade (buy/sell)</param>
    /// <param name="accountEquity">Current account equity for PDT minimum check</param>
    /// <param name="timestamp">When the trade would occur</param>
    /// <returns>
    /// Allowed: Trade can proceed without restriction
    /// Warning: Trade allowed but will use last available day trade
    /// Rejected: Trade would violate PDT rules and cannot proceed
    /// </returns>
    public PdtValidationResult ValidateTrade(
        string accountId,
        string symbol,
        OrderSide side,
        decimal accountEquity,
        DateTimeOffset timestamp)
    {
        // First check if this would even be a day trade
        var wouldBeDayTrade = WouldBeDayTrade(accountId, symbol, side, timestamp);

        if (!wouldBeDayTrade)
        {
            // Not a day trade - no PDT restrictions apply
            return PdtValidationResult.Allowed();
        }

        // Accounts with $25k+ equity can day trade without restriction
        // This is the PDT minimum equity requirement
        if (accountEquity >= PdtMinimumEquity)
        {
            return PdtValidationResult.Allowed();
        }

        // Get current day trade count in the 5-day window
        var currentCount = GetDayTradeCount(accountId, timestamp);

        // Check if this trade would exceed the 3-trade limit for accounts under $25k
        // Note: 4th day trade triggers PDT flag and restrictions
        if (currentCount >= DayTradeLimit)
        {
            return PdtValidationResult.Rejected(
                $"Day trade limit exceeded. {currentCount} day trades in last 5 days. " +
                $"Account equity ({accountEquity:C}) is below PDT minimum ({PdtMinimumEquity:C}).");
        }

        // Warn user if they're about to use their last allowed day trade
        // This gives them a chance to reconsider
        if (currentCount == DayTradeLimit - 1)
        {
            return PdtValidationResult.Warning(
                $"This trade will use your last allowed day trade. " +
                $"You have made {currentCount} day trades in the last 5 days.");
        }

        return PdtValidationResult.Allowed();
    }

    /// <summary>
    /// Counts day trades within a date range for a specific account.
    /// A day trade is counted when both a buy and sell of the same security
    /// occur on the same calendar day.
    /// </summary>
    /// <param name="accountId">The account to count trades for</param>
    /// <param name="from">Start of the date range</param>
    /// <param name="to">End of the date range</param>
    /// <returns>Number of day trades in the range</returns>
    private int CountDayTrades(string accountId, DateTimeOffset from, DateTimeOffset to)
    {
        // Group trades by symbol and date to identify round-trip trades
        var tradesInWindow = _trades
            .Where(t => t.AccountId == accountId && t.Timestamp >= from && t.Timestamp <= to)
            .GroupBy(t => new { t.Symbol, Date = t.Timestamp.Date })
            .ToList();

        // Count groups that have both a buy AND a sell - these are day trades
        // Note: Multiple round-trips of the same symbol on the same day
        // could count as multiple day trades, but this simplified model
        // counts each symbol/day pair as at most one day trade
        return tradesInWindow.Count(g =>
            g.Any(t => t.Side == OrderSide.Buy) &&
            g.Any(t => t.Side == OrderSide.Sell));
    }

    /// <summary>
    /// Removes trade records older than the tracking window.
    /// Call periodically (e.g., daily) to prevent unbounded memory growth.
    /// </summary>
    /// <param name="asOf">Current time reference for calculating cutoff</param>
    public void PurgeOldRecords(DateTimeOffset asOf)
    {
        // Keep one extra day beyond the window for safety
        var cutoff = asOf.AddDays(-RollingWindowDays - 1);
        _trades.RemoveAll(t => t.Timestamp < cutoff);
    }

    /// <summary>
    /// Gets all trades for an account. Useful for debugging and testing.
    /// </summary>
    /// <param name="accountId">The account to retrieve trades for</param>
    /// <returns>List of trade records for the account</returns>
    public IReadOnlyList<TradeRecord> GetTrades(string accountId)
    {
        return _trades.Where(t => t.AccountId == accountId).ToList();
    }

    /// <summary>
    /// Clears all recorded trades. Use only for testing purposes.
    /// </summary>
    public void Clear()
    {
        _trades.Clear();
    }
}

/// <summary>
/// Immutable record of a single trade execution for PDT tracking.
/// Captures the essential information needed to determine if trades
/// constitute a day trade (same symbol, opposite sides, same day).
/// </summary>
/// <param name="AccountId">The account that executed this trade</param>
/// <param name="Symbol">The security symbol traded (e.g., "AAPL")</param>
/// <param name="Side">Whether this was a buy or sell</param>
/// <param name="Qty">The quantity traded (for future use in partial day trade logic)</param>
/// <param name="Timestamp">When the trade was executed</param>
public record TradeRecord(
    string AccountId,
    string Symbol,
    OrderSide Side,
    decimal Qty,
    DateTimeOffset Timestamp);

/// <summary>
/// Result of PDT rule validation for a proposed trade.
/// Contains three possible outcomes:
/// - Allowed: Trade can proceed without restriction
/// - Warning: Trade is allowed but uses the last available day trade
/// - Rejected: Trade violates PDT rules and cannot proceed
/// </summary>
/// <param name="IsAllowed">True if the trade can be executed</param>
/// <param name="HasWarning">True if a warning should be shown (last day trade)</param>
/// <param name="Message">Human-readable explanation of the validation result</param>
public record PdtValidationResult(bool IsAllowed, bool HasWarning, string? Message)
{
    /// <summary>Creates an allowed result with no restrictions.</summary>
    public static PdtValidationResult Allowed() => new(true, false, null);

    /// <summary>Creates an allowed result with a warning message.</summary>
    public static PdtValidationResult Warning(string message) => new(true, true, message);

    /// <summary>Creates a rejected result indicating PDT violation.</summary>
    public static PdtValidationResult Rejected(string message) => new(false, false, message);
}
