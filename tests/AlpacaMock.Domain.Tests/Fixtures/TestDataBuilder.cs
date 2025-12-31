using AlpacaMock.Domain.Accounts;
using AlpacaMock.Domain.Market;
using AlpacaMock.Domain.Trading;

namespace AlpacaMock.Domain.Tests.Fixtures;

/// <summary>
/// Helper class for creating test data.
/// </summary>
public static class TestDataBuilder
{
    private const string DefaultSessionId = "test-session";
    private const string DefaultAccountId = "test-account";

    public static Order CreateMarketOrder(
        string symbol = "AAPL",
        OrderSide side = OrderSide.Buy,
        decimal qty = 10,
        string? sessionId = null,
        string? accountId = null)
    {
        return new Order
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId ?? DefaultSessionId,
            AccountId = accountId ?? DefaultAccountId,
            Symbol = symbol,
            Qty = qty,
            Side = side,
            Type = OrderType.Market,
            TimeInForce = TimeInForce.Day,
            SubmittedAt = DateTimeOffset.UtcNow,
            Status = OrderStatus.New
        };
    }

    public static Order CreateLimitOrder(
        string symbol = "AAPL",
        OrderSide side = OrderSide.Buy,
        decimal qty = 10,
        decimal limitPrice = 150m,
        TimeInForce tif = TimeInForce.Day,
        bool extendedHours = false,
        string? sessionId = null,
        string? accountId = null)
    {
        return new Order
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId ?? DefaultSessionId,
            AccountId = accountId ?? DefaultAccountId,
            Symbol = symbol,
            Qty = qty,
            Side = side,
            Type = OrderType.Limit,
            LimitPrice = limitPrice,
            TimeInForce = tif,
            ExtendedHours = extendedHours,
            SubmittedAt = DateTimeOffset.UtcNow,
            Status = OrderStatus.New
        };
    }

    public static Order CreateStopOrder(
        string symbol = "AAPL",
        OrderSide side = OrderSide.Sell,
        decimal qty = 10,
        decimal stopPrice = 145m,
        string? sessionId = null,
        string? accountId = null)
    {
        return new Order
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId ?? DefaultSessionId,
            AccountId = accountId ?? DefaultAccountId,
            Symbol = symbol,
            Qty = qty,
            Side = side,
            Type = OrderType.Stop,
            StopPrice = stopPrice,
            TimeInForce = TimeInForce.Day,
            SubmittedAt = DateTimeOffset.UtcNow,
            Status = OrderStatus.New
        };
    }

    public static Order CreateStopLimitOrder(
        string symbol = "AAPL",
        OrderSide side = OrderSide.Sell,
        decimal qty = 10,
        decimal stopPrice = 145m,
        decimal limitPrice = 144.50m,
        string? sessionId = null,
        string? accountId = null)
    {
        return new Order
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId ?? DefaultSessionId,
            AccountId = accountId ?? DefaultAccountId,
            Symbol = symbol,
            Qty = qty,
            Side = side,
            Type = OrderType.StopLimit,
            StopPrice = stopPrice,
            LimitPrice = limitPrice,
            TimeInForce = TimeInForce.Day,
            SubmittedAt = DateTimeOffset.UtcNow,
            Status = OrderStatus.New
        };
    }

    public static Order CreateGtcOrder(
        string symbol = "AAPL",
        OrderSide side = OrderSide.Buy,
        decimal qty = 10,
        decimal limitPrice = 150m,
        DateTimeOffset? submittedAt = null)
    {
        return new Order
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = DefaultSessionId,
            AccountId = DefaultAccountId,
            Symbol = symbol,
            Qty = qty,
            Side = side,
            Type = OrderType.Limit,
            LimitPrice = limitPrice,
            TimeInForce = TimeInForce.Gtc,
            SubmittedAt = submittedAt ?? DateTimeOffset.UtcNow,
            Status = OrderStatus.Accepted
        };
    }

    public static Order CreateIocOrder(
        string symbol = "AAPL",
        OrderSide side = OrderSide.Buy,
        decimal qty = 10,
        decimal limitPrice = 150m)
    {
        return new Order
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = DefaultSessionId,
            AccountId = DefaultAccountId,
            Symbol = symbol,
            Qty = qty,
            Side = side,
            Type = OrderType.Limit,
            LimitPrice = limitPrice,
            TimeInForce = TimeInForce.Ioc,
            SubmittedAt = DateTimeOffset.UtcNow,
            Status = OrderStatus.Accepted
        };
    }

    public static Order CreateFokOrder(
        string symbol = "AAPL",
        OrderSide side = OrderSide.Buy,
        decimal qty = 10,
        decimal limitPrice = 150m)
    {
        return new Order
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = DefaultSessionId,
            AccountId = DefaultAccountId,
            Symbol = symbol,
            Qty = qty,
            Side = side,
            Type = OrderType.Limit,
            LimitPrice = limitPrice,
            TimeInForce = TimeInForce.Fok,
            SubmittedAt = DateTimeOffset.UtcNow,
            Status = OrderStatus.Accepted
        };
    }

    public static Account CreateAccount(
        decimal cash = 100_000m,
        bool pdt = false,
        string? sessionId = null,
        string? accountId = null)
    {
        var account = new Account
        {
            Id = accountId ?? DefaultAccountId,
            SessionId = sessionId ?? DefaultSessionId,
            Cash = cash,
            BuyingPower = cash,
            Equity = cash,
            PortfolioValue = cash,
            PatternDayTrader = pdt,
            DayTradingBuyingPower = cash * 4
        };
        return account;
    }

    public static Position CreatePosition(
        string symbol = "AAPL",
        decimal qty = 100,
        decimal avgPrice = 150m,
        string? sessionId = null,
        string? accountId = null)
    {
        return new Position
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId ?? DefaultSessionId,
            AccountId = accountId ?? DefaultAccountId,
            Symbol = symbol,
            Qty = qty,
            AvgEntryPrice = avgPrice,
            CurrentPrice = avgPrice,
            LastDayPrice = avgPrice
        };
    }

    public static Bar CreateBar(
        string symbol = "AAPL",
        decimal open = 150m,
        decimal high = 152m,
        decimal low = 149m,
        decimal close = 151m,
        long volume = 1_000_000,
        DateTimeOffset? timestamp = null)
    {
        return new Bar
        {
            Symbol = symbol,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = volume
        };
    }

    /// <summary>
    /// Creates a bar that would trigger a buy limit order at the given price.
    /// </summary>
    public static Bar CreateBarThatTriggersBuyLimit(string symbol, decimal limitPrice)
    {
        return new Bar
        {
            Symbol = symbol,
            Timestamp = DateTimeOffset.UtcNow,
            Open = limitPrice + 1,
            High = limitPrice + 2,
            Low = limitPrice - 1, // Low touches limit
            Close = limitPrice + 0.5m,
            Volume = 1_000_000
        };
    }

    /// <summary>
    /// Creates a bar that would NOT trigger a buy limit order at the given price.
    /// </summary>
    public static Bar CreateBarThatMissesBuyLimit(string symbol, decimal limitPrice)
    {
        return new Bar
        {
            Symbol = symbol,
            Timestamp = DateTimeOffset.UtcNow,
            Open = limitPrice + 2,
            High = limitPrice + 3,
            Low = limitPrice + 1, // Low doesn't reach limit
            Close = limitPrice + 2,
            Volume = 1_000_000
        };
    }

    /// <summary>
    /// Creates a bar that would trigger a sell stop order at the given price.
    /// </summary>
    public static Bar CreateBarThatTriggersSellStop(string symbol, decimal stopPrice)
    {
        return new Bar
        {
            Symbol = symbol,
            Timestamp = DateTimeOffset.UtcNow,
            Open = stopPrice + 2,
            High = stopPrice + 3,
            Low = stopPrice - 1, // Low touches stop
            Close = stopPrice - 0.5m,
            Volume = 1_000_000
        };
    }

    /// <summary>
    /// Creates a bar with very low volume to test partial fills.
    /// </summary>
    public static Bar CreateLowVolumeBar(string symbol, decimal price, long volume = 100)
    {
        return new Bar
        {
            Symbol = symbol,
            Timestamp = DateTimeOffset.UtcNow,
            Open = price,
            High = price + 0.5m,
            Low = price - 0.5m,
            Close = price,
            Volume = volume
        };
    }
}
