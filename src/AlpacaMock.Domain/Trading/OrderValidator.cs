using AlpacaMock.Domain.Accounts;

namespace AlpacaMock.Domain.Trading;

/// <summary>
/// Validates orders against Alpaca API rules for fidelity.
/// </summary>
public class OrderValidator
{
    private const int MaxDecimalPlacesForPriceAboveDollar = 2;
    private const int MaxDecimalPlacesForPriceBelowDollar = 4;
    private const decimal BuyStopPremiumPercentLow = 0.04m;   // 4% for prices < $50
    private const decimal BuyStopPremiumPercentHigh = 0.025m; // 2.5% for prices >= $50
    private const decimal StopPriceThreshold = 50m;

    /// <summary>
    /// Validates an order against all Alpaca API rules.
    /// </summary>
    public ValidationResult Validate(
        Order order,
        Account account,
        decimal? currentPrice = null,
        bool isMarketOpen = true)
    {
        var errors = new List<ValidationError>();

        // Price decimal precision validation
        ValidatePriceDecimalPrecision(order, errors);

        // Buying power validation
        ValidateBuyingPower(order, account, currentPrice, errors);

        // Extended hours validation
        ValidateExtendedHours(order, errors);

        // Time-in-force validation
        ValidateTimeInForce(order, isMarketOpen, errors);

        // Stop order validation
        ValidateStopOrder(order, currentPrice, errors);

        // Order type specific validations
        ValidateOrderTypeRequirements(order, errors);

        return new ValidationResult(errors.Count == 0, errors);
    }

    /// <summary>
    /// Validates price decimal precision per Alpaca rules.
    /// Max 2 decimals for prices >= $1, max 4 decimals for prices < $1.
    /// </summary>
    private void ValidatePriceDecimalPrecision(Order order, List<ValidationError> errors)
    {
        if (order.LimitPrice.HasValue)
        {
            var maxDecimals = order.LimitPrice.Value >= 1m
                ? MaxDecimalPlacesForPriceAboveDollar
                : MaxDecimalPlacesForPriceBelowDollar;

            if (GetDecimalPlaces(order.LimitPrice.Value) > maxDecimals)
            {
                errors.Add(new ValidationError(
                    "limit_price",
                    $"Limit price has too many decimal places. Max {maxDecimals} for prices " +
                    (order.LimitPrice.Value >= 1m ? ">= $1" : "< $1")));
            }
        }

        if (order.StopPrice.HasValue)
        {
            var maxDecimals = order.StopPrice.Value >= 1m
                ? MaxDecimalPlacesForPriceAboveDollar
                : MaxDecimalPlacesForPriceBelowDollar;

            if (GetDecimalPlaces(order.StopPrice.Value) > maxDecimals)
            {
                errors.Add(new ValidationError(
                    "stop_price",
                    $"Stop price has too many decimal places. Max {maxDecimals} for prices " +
                    (order.StopPrice.Value >= 1m ? ">= $1" : "< $1")));
            }
        }
    }

    /// <summary>
    /// Validates that the account has sufficient buying power for the order.
    /// </summary>
    private void ValidateBuyingPower(
        Order order,
        Account account,
        decimal? currentPrice,
        List<ValidationError> errors)
    {
        if (order.Side != OrderSide.Buy) return;

        var estimatedCost = CalculateEstimatedCost(order, currentPrice);

        if (estimatedCost > account.BuyingPower)
        {
            errors.Add(new ValidationError(
                "buying_power",
                $"Insufficient buying power. Required: {estimatedCost:C}, Available: {account.BuyingPower:C}"));
        }
    }

    /// <summary>
    /// Validates extended hours trading restrictions.
    /// Only limit orders with TIF=DAY are allowed for extended hours.
    /// </summary>
    private void ValidateExtendedHours(Order order, List<ValidationError> errors)
    {
        if (!order.ExtendedHours) return;

        if (order.Type != OrderType.Limit)
        {
            errors.Add(new ValidationError(
                "type",
                "Extended hours trading only supports limit orders"));
        }

        if (order.TimeInForce != TimeInForce.Day)
        {
            errors.Add(new ValidationError(
                "time_in_force",
                "Extended hours trading only supports day orders"));
        }
    }

    /// <summary>
    /// Validates time-in-force restrictions.
    /// </summary>
    private void ValidateTimeInForce(Order order, bool isMarketOpen, List<ValidationError> errors)
    {
        // OPG orders only valid before market open
        if (order.TimeInForce == TimeInForce.Opg && isMarketOpen)
        {
            errors.Add(new ValidationError(
                "time_in_force",
                "OPG orders can only be submitted before market open"));
        }

        // CLS orders only valid before market close (simplified - just check market is open)
        if (order.TimeInForce == TimeInForce.Cls && !isMarketOpen)
        {
            errors.Add(new ValidationError(
                "time_in_force",
                "CLS orders can only be submitted during market hours"));
        }
    }

    /// <summary>
    /// Validates stop order price direction.
    /// Buy stops must be above current price, sell stops must be below.
    /// </summary>
    private void ValidateStopOrder(Order order, decimal? currentPrice, List<ValidationError> errors)
    {
        if (order.Type != OrderType.Stop && order.Type != OrderType.StopLimit) return;
        if (!currentPrice.HasValue || !order.StopPrice.HasValue) return;

        // Buy stop must be above current price
        if (order.Side == OrderSide.Buy && order.StopPrice <= currentPrice)
        {
            errors.Add(new ValidationError(
                "stop_price",
                $"Buy stop price ({order.StopPrice:C}) must be above current market price ({currentPrice:C})"));
        }

        // Sell stop must be below current price
        if (order.Side == OrderSide.Sell && order.StopPrice >= currentPrice)
        {
            errors.Add(new ValidationError(
                "stop_price",
                $"Sell stop price ({order.StopPrice:C}) must be below current market price ({currentPrice:C})"));
        }
    }

    /// <summary>
    /// Validates order type-specific requirements.
    /// </summary>
    private void ValidateOrderTypeRequirements(Order order, List<ValidationError> errors)
    {
        switch (order.Type)
        {
            case OrderType.Limit:
                if (!order.LimitPrice.HasValue)
                {
                    errors.Add(new ValidationError("limit_price", "Limit price is required for limit orders"));
                }
                break;

            case OrderType.Stop:
                if (!order.StopPrice.HasValue)
                {
                    errors.Add(new ValidationError("stop_price", "Stop price is required for stop orders"));
                }
                break;

            case OrderType.StopLimit:
                if (!order.StopPrice.HasValue)
                {
                    errors.Add(new ValidationError("stop_price", "Stop price is required for stop-limit orders"));
                }
                if (!order.LimitPrice.HasValue)
                {
                    errors.Add(new ValidationError("limit_price", "Limit price is required for stop-limit orders"));
                }
                break;

            case OrderType.TrailingStop:
                if (!order.TrailPrice.HasValue && !order.TrailPercent.HasValue)
                {
                    errors.Add(new ValidationError(
                        "trail_price",
                        "Either trail_price or trail_percent is required for trailing stop orders"));
                }
                break;
        }
    }

    /// <summary>
    /// Calculates the stop-limit premium for buy stop orders.
    /// Alpaca converts buy stops to stop-limit with 4% premium (price < $50) or 2.5% premium (price >= $50).
    /// </summary>
    public decimal CalculateStopLimitPremium(decimal stopPrice)
    {
        var premiumPercent = stopPrice < StopPriceThreshold
            ? BuyStopPremiumPercentLow
            : BuyStopPremiumPercentHigh;

        return stopPrice * (1 + premiumPercent);
    }

    /// <summary>
    /// Calculates the estimated cost of an order.
    /// </summary>
    private static decimal CalculateEstimatedCost(Order order, decimal? currentPrice)
    {
        var price = order.Type switch
        {
            OrderType.Limit => order.LimitPrice ?? 0,
            OrderType.StopLimit => order.LimitPrice ?? 0,
            OrderType.Stop => order.StopPrice ?? currentPrice ?? 0,
            _ => currentPrice ?? 0
        };

        // Use notional if specified, otherwise calculate from qty
        if (order.Notional.HasValue)
        {
            return order.Notional.Value;
        }

        return order.Qty * price;
    }

    /// <summary>
    /// Gets the number of decimal places in a value.
    /// </summary>
    public static int GetDecimalPlaces(decimal value)
    {
        value = Math.Abs(value);
        var count = 0;
        while (value != Math.Floor(value) && count < 10)
        {
            value *= 10;
            count++;
        }
        return count;
    }
}

/// <summary>
/// Result of order validation.
/// </summary>
public record ValidationResult(bool IsValid, IReadOnlyList<ValidationError> Errors)
{
    public static ValidationResult Valid() => new(true, Array.Empty<ValidationError>());

    public static ValidationResult Invalid(params ValidationError[] errors) =>
        new(false, errors);
}

/// <summary>
/// A single validation error.
/// </summary>
public record ValidationError(string Field, string Message);
