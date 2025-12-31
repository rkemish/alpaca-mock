namespace AlpacaMock.Domain.Accounts;

/// <summary>
/// Represents a trading account following Alpaca API structure.
///
/// Alpaca Account Model:
/// - Supports cash and margin accounts (simplified to cash for backtesting)
/// - Tracks buying power, equity, and margin requirements
/// - Enforces Pattern Day Trader (PDT) rules per FINRA regulations
///
/// Key Financial Metrics:
/// - Equity = Cash + LongMarketValue - ShortMarketValue
/// - BuyingPower = Available funds for new positions (2x equity for margin accounts)
/// - DayTradingBuyingPower = 4x maintenance margin excess for PDT accounts
///
/// PDT Rules (FINRA):
/// - 4+ day trades in 5 business days = PDT flag
/// - PDT accounts require $25,000 minimum equity
/// - PDT accounts get 4x day trading buying power
///
/// Reference: https://alpaca.markets/docs/trading/account/
/// </summary>
public class Account
{
    /// <summary>Unique account identifier assigned by the system.</summary>
    public required string Id { get; init; }

    /// <summary>Session this account belongs to (for backtesting isolation).</summary>
    public required string SessionId { get; init; }

    /// <summary>Alpaca-style account number (e.g., "A12345678").</summary>
    public string AccountNumber { get; init; } = GenerateAccountNumber();

    /// <summary>Account status for trading (Active, Suspended, etc.).</summary>
    public AccountStatus Status { get; set; } = AccountStatus.Active;

    /// <summary>Separate status for crypto trading.</summary>
    public AccountStatus CryptoStatus { get; set; } = AccountStatus.Active;

    /// <summary>Account's base currency (default USD).</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>
    /// Cash balance available for trading.
    /// Reduced when buying, increased when selling.
    /// </summary>
    public decimal Cash { get; set; }

    /// <summary>
    /// Cash that can be withdrawn without affecting margin requirements.
    /// Calculated as: Cash - InitialMargin.
    /// </summary>
    public decimal CashWithdrawable { get; set; }

    /// <summary>
    /// Total portfolio value including all positions.
    /// Equals Equity for this simplified model.
    /// </summary>
    public decimal PortfolioValue { get; set; }

    /// <summary>
    /// Available funds for opening new positions.
    /// For cash accounts: equals Cash.
    /// For margin accounts: 2x equity minus existing margin use.
    /// </summary>
    public decimal BuyingPower { get; set; }

    /// <summary>
    /// Buying power specifically for day trades.
    /// PDT accounts get 4x maintenance margin excess.
    /// Non-PDT accounts get same as regular buying power.
    /// </summary>
    public decimal DayTradingBuyingPower { get; set; }

    /// <summary>
    /// Initial margin required to open positions.
    /// Typically 50% of position value per Regulation T.
    /// </summary>
    public decimal InitialMargin { get; set; }

    /// <summary>
    /// Minimum equity required to maintain positions.
    /// Typically 25% of position value. Margin call if equity falls below.
    /// </summary>
    public decimal MaintenanceMargin { get; set; }

    /// <summary>
    /// Total market value of long positions.
    /// Positive value representing owned shares.
    /// </summary>
    public decimal LongMarketValue { get; set; }

    /// <summary>
    /// Total market value of short positions.
    /// Absolute value of shorted shares (shown as positive or negative per convention).
    /// </summary>
    public decimal ShortMarketValue { get; set; }

    /// <summary>
    /// Total account equity: Cash + LongMarketValue - |ShortMarketValue|.
    /// This is the net liquidation value of the account.
    /// </summary>
    public decimal Equity { get; set; }

    /// <summary>
    /// Previous trading day's closing equity.
    /// Used to calculate daily P&L.
    /// </summary>
    public decimal LastEquity { get; set; }

    /// <summary>
    /// True if account is flagged as Pattern Day Trader.
    /// Triggered by 4+ day trades in 5 business days.
    /// Requires $25,000 minimum equity to continue day trading.
    /// </summary>
    public bool PatternDayTrader { get; set; }

    /// <summary>
    /// Rolling count of day trades in the last 5 business days.
    /// Updated by DayTradeTracker.
    /// </summary>
    public int DayTradeCount { get; set; }

    /// <summary>
    /// True if account is blocked from trading (e.g., margin call, compliance issue).
    /// </summary>
    public bool TradingBlocked { get; set; }

    /// <summary>
    /// True if account is blocked from money transfers.
    /// </summary>
    public bool TransfersBlocked { get; set; }

    /// <summary>
    /// True if user has voluntarily suspended trading.
    /// </summary>
    public bool TradeSuspendedByUser { get; set; }

    /// <summary>When the account was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Optional contact information for the account holder.</summary>
    public ContactInfo? Contact { get; set; }

    /// <summary>Optional identity information for the account holder.</summary>
    public IdentityInfo? Identity { get; set; }

    /// <summary>
    /// Generates a random Alpaca-style account number.
    /// </summary>
    private static string GenerateAccountNumber()
    {
        return $"A{Random.Shared.Next(10000000, 99999999)}";
    }

    /// <summary>
    /// Recalculates all derived account values after position changes.
    ///
    /// Call this method after any position update (fill, price change, etc.)
    /// to ensure all account metrics are consistent.
    ///
    /// Calculations:
    /// - Equity = Cash + LongMarketValue - |ShortMarketValue|
    /// - PortfolioValue = Equity (simplified)
    /// - BuyingPower = Cash (simplified cash account model)
    /// - DayTradingBuyingPower = 4x margin excess for PDT, else equals BuyingPower
    /// - CashWithdrawable = Max(0, Cash - InitialMargin)
    /// </summary>
    /// <param name="totalLongValue">Sum of all long position market values</param>
    /// <param name="totalShortValue">Sum of all short position market values (as positive)</param>
    /// <param name="totalUnrealizedPnL">Sum of all unrealized P&L (for future use)</param>
    public void RecalculateValues(decimal totalLongValue, decimal totalShortValue, decimal totalUnrealizedPnL)
    {
        LongMarketValue = totalLongValue;
        ShortMarketValue = totalShortValue;

        // Equity is net liquidation value: cash + longs - shorts
        Equity = Cash + LongMarketValue - Math.Abs(ShortMarketValue);
        PortfolioValue = Equity;

        // Simplified cash account: buying power equals cash
        // Margin accounts would use: Equity * 2 - InitialMargin
        BuyingPower = Cash;

        // Day trading power depends on PDT status
        DayTradingBuyingPower = CalculateDayTradingBuyingPower();

        // Withdrawable cash excludes margin requirements
        CashWithdrawable = Math.Max(0, Cash - InitialMargin);
    }

    /// <summary>
    /// Calculates day trading buying power per FINRA rules.
    ///
    /// PDT accounts (flagged as Pattern Day Trader) receive 4x leverage
    /// on their maintenance margin excess for day trades. This allows
    /// more aggressive intraday trading but increases risk.
    ///
    /// Non-PDT accounts have no special day trading power - they use
    /// regular buying power for all trades.
    ///
    /// Formula for PDT: 4 × (Equity - MaintenanceMargin)
    /// </summary>
    /// <returns>Available buying power for day trades</returns>
    public decimal CalculateDayTradingBuyingPower()
    {
        if (!PatternDayTrader)
        {
            // Non-PDT accounts use regular buying power
            return BuyingPower;
        }

        // PDT accounts get 4x the maintenance margin excess
        // This is the "special memorandum account" (SMA) multiplied by 4
        var maintenanceExcess = Equity - MaintenanceMargin;
        return Math.Max(0, maintenanceExcess * 4);
    }

    /// <summary>
    /// Validates if an order can be placed given current buying power.
    ///
    /// Day trades use the special day trading buying power (4x for PDT),
    /// while regular trades use standard buying power.
    /// </summary>
    /// <param name="orderCost">Estimated cost of the order (qty × price)</param>
    /// <param name="isDayTrade">True if this would be a day trade</param>
    /// <returns>Validation result with error message if insufficient funds</returns>
    public BuyingPowerValidation ValidateBuyingPower(decimal orderCost, bool isDayTrade = false)
    {
        // Use day trading buying power if this is a day trade
        var availablePower = isDayTrade
            ? CalculateDayTradingBuyingPower()
            : BuyingPower;

        if (orderCost > availablePower)
        {
            return new BuyingPowerValidation(
                false,
                $"Insufficient buying power. Required: {orderCost:C}, Available: {availablePower:C}");
        }

        return new BuyingPowerValidation(true, null);
    }

    /// <summary>
    /// Calculates the buying power required for a short sale.
    ///
    /// Per Alpaca rules, short selling requires collateral based on
    /// MAX(limit_price, 3% above current ask price). This ensures
    /// adequate margin for the short position.
    ///
    /// Example: Short 100 shares at $50 ask → requires 100 × $51.50 = $5,150
    /// </summary>
    /// <param name="qty">Number of shares to short</param>
    /// <param name="limitPrice">Limit price for the order (if any)</param>
    /// <param name="currentAsk">Current ask price of the security</param>
    /// <returns>Required buying power for the short sale</returns>
    public decimal CalculateShortSellingRequirement(decimal qty, decimal? limitPrice, decimal currentAsk)
    {
        // 3% above current ask as minimum margin price
        var priceAboveAsk = currentAsk * 1.03m;

        // Use the higher of limit price or 3% above ask
        var effectivePrice = Math.Max(limitPrice ?? 0, priceAboveAsk);

        return qty * effectivePrice;
    }

    /// <summary>
    /// Checks if the account meets the $25,000 PDT minimum equity requirement.
    ///
    /// Per FINRA rules, Pattern Day Traders must maintain at least $25,000
    /// in account equity to continue day trading. Falling below this
    /// threshold restricts the account to 3 day trades per 5 business days.
    /// </summary>
    /// <returns>True if equity ≥ $25,000</returns>
    public bool MeetsPdtMinimum()
    {
        const decimal pdtMinimum = 25_000m;
        return Equity >= pdtMinimum;
    }

    /// <summary>
    /// Deducts cash for a buy order.
    ///
    /// This is called when an order is filled to reduce available cash.
    /// Returns false if there isn't enough cash (order should not have been filled).
    /// </summary>
    /// <param name="amount">Amount to deduct from cash balance</param>
    /// <returns>True if successful, false if insufficient funds</returns>
    public bool DeductCash(decimal amount)
    {
        if (amount > Cash)
            return false;

        Cash -= amount;
        BuyingPower = Cash; // Simplified cash account
        return true;
    }

    /// <summary>
    /// Adds cash from a sell order or dividend.
    ///
    /// Called when a sell order fills to credit proceeds to the account.
    /// </summary>
    /// <param name="amount">Amount to add to cash balance</param>
    public void AddCash(decimal amount)
    {
        Cash += amount;
        BuyingPower = Cash; // Simplified cash account
    }
}

/// <summary>
/// Result of buying power validation.
/// </summary>
public record BuyingPowerValidation(bool IsValid, string? Error);

public enum AccountStatus
{
    Onboarding,
    SubmissionFailed,
    Submitted,
    AccountUpdated,
    ApprovalPending,
    Active,
    Rejected,
    Disabled,
    AccountClosed
}

public class ContactInfo
{
    public string? EmailAddress { get; set; }
    public string? PhoneNumber { get; set; }
    public List<string> StreetAddress { get; set; } = new();
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string Country { get; set; } = "USA";
}

public class IdentityInfo
{
    public string? GivenName { get; set; }
    public string? MiddleName { get; set; }
    public string? FamilyName { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? TaxIdType { get; set; }
    public string? CountryOfCitizenship { get; set; }
    public string? CountryOfBirth { get; set; }
    public string? CountryOfTaxResidence { get; set; }
}
