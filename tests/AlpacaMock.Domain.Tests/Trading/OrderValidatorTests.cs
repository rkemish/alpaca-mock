using AlpacaMock.Domain.Accounts;
using AlpacaMock.Domain.Tests.Fixtures;
using AlpacaMock.Domain.Trading;
using FluentAssertions;
using Xunit;

namespace AlpacaMock.Domain.Tests.Trading;

public class OrderValidatorTests
{
    private readonly OrderValidator _validator = new();

    #region Price Decimal Precision Tests

    [Theory]
    [InlineData(100.12, true)]      // 2 decimals for $100 - valid
    [InlineData(100.1, true)]       // 1 decimal for $100 - valid
    [InlineData(100.123, false)]    // 3 decimals for $100 - invalid
    [InlineData(100.1234, false)]   // 4 decimals for $100 - invalid
    public void ValidateLimitPrice_AboveDollar_EnforcesMaxTwoDecimals(decimal price, bool shouldBeValid)
    {
        // Arrange
        var order = TestDataBuilder.CreateLimitOrder(limitPrice: price);
        var account = TestDataBuilder.CreateAccount();

        // Act
        var result = _validator.Validate(order, account);

        // Assert
        if (shouldBeValid)
        {
            result.Errors.Should().NotContain(e => e.Field == "limit_price" && e.Message.Contains("decimal"));
        }
        else
        {
            result.Errors.Should().Contain(e => e.Field == "limit_price");
        }
    }

    [Theory]
    [InlineData(0.5012, true)]      // 4 decimals for $0.50 - valid
    [InlineData(0.50123, false)]    // 5 decimals for $0.50 - invalid
    [InlineData(0.501234, false)]   // 6 decimals for $0.50 - invalid
    public void ValidateLimitPrice_BelowDollar_EnforcesMaxFourDecimals(decimal price, bool shouldBeValid)
    {
        // Arrange
        var order = TestDataBuilder.CreateLimitOrder(limitPrice: price);
        var account = TestDataBuilder.CreateAccount();

        // Act
        var result = _validator.Validate(order, account);

        // Assert
        if (shouldBeValid)
        {
            result.Errors.Should().NotContain(e => e.Field == "limit_price" && e.Message.Contains("decimal"));
        }
        else
        {
            result.Errors.Should().Contain(e => e.Field == "limit_price");
        }
    }

    [Fact]
    public void ValidateLimitPrice_BoundaryAtOneDollar_UsesToDecimals()
    {
        // Arrange - exactly $1.00 should use 2 decimal rule
        var order = TestDataBuilder.CreateLimitOrder(limitPrice: 1.001m);
        var account = TestDataBuilder.CreateAccount();

        // Act
        var result = _validator.Validate(order, account);

        // Assert - 3 decimals at $1+ should fail
        result.Errors.Should().Contain(e => e.Field == "limit_price");
    }

    [Fact]
    public void ValidateLimitPrice_JustBelowDollar_UsesFourDecimals()
    {
        // Arrange - $0.9999 should use 4 decimal rule
        var order = TestDataBuilder.CreateLimitOrder(limitPrice: 0.99991m);
        var account = TestDataBuilder.CreateAccount();

        // Act
        var result = _validator.Validate(order, account);

        // Assert - 5 decimals below $1 should fail
        result.Errors.Should().Contain(e => e.Field == "limit_price");
    }

    #endregion

    #region Buying Power Tests

    [Fact]
    public void ValidateBuyOrder_InsufficientBuyingPower_ReturnsError()
    {
        // Arrange
        var order = TestDataBuilder.CreateLimitOrder(qty: 1000, limitPrice: 150m); // $150,000 order
        var account = TestDataBuilder.CreateAccount(cash: 10_000m); // Only $10k

        // Act
        var result = _validator.Validate(order, account, currentPrice: 150m);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "buying_power");
    }

    [Fact]
    public void ValidateBuyOrder_SufficientBuyingPower_ReturnsValid()
    {
        // Arrange
        var order = TestDataBuilder.CreateLimitOrder(qty: 10, limitPrice: 150m); // $1,500 order
        var account = TestDataBuilder.CreateAccount(cash: 100_000m);

        // Act
        var result = _validator.Validate(order, account, currentPrice: 150m);

        // Assert
        result.Errors.Should().NotContain(e => e.Field == "buying_power");
    }

    [Fact]
    public void ValidateSellOrder_DoesNotCheckBuyingPower()
    {
        // Arrange - sell order should not check buying power
        var order = TestDataBuilder.CreateLimitOrder(
            side: OrderSide.Sell,
            qty: 1000,
            limitPrice: 150m);
        var account = TestDataBuilder.CreateAccount(cash: 0m); // No cash

        // Act
        var result = _validator.Validate(order, account, currentPrice: 150m);

        // Assert
        result.Errors.Should().NotContain(e => e.Field == "buying_power");
    }

    #endregion

    #region Extended Hours Tests

    [Fact]
    public void ValidateExtendedHours_MarketOrder_ReturnsError()
    {
        // Arrange
        var order = TestDataBuilder.CreateMarketOrder();
        // Manually set extended hours since CreateMarketOrder doesn't support it
        var extendedOrder = new Order
        {
            Id = order.Id,
            SessionId = order.SessionId,
            AccountId = order.AccountId,
            Symbol = order.Symbol,
            Qty = order.Qty,
            Side = order.Side,
            Type = OrderType.Market,
            ExtendedHours = true,
            TimeInForce = TimeInForce.Day,
            SubmittedAt = DateTimeOffset.UtcNow
        };
        var account = TestDataBuilder.CreateAccount();

        // Act
        var result = _validator.Validate(extendedOrder, account);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "type" && e.Message.Contains("limit"));
    }

    [Fact]
    public void ValidateExtendedHours_LimitOrderWithGtc_ReturnsError()
    {
        // Arrange
        var order = TestDataBuilder.CreateLimitOrder(tif: TimeInForce.Gtc, extendedHours: true);
        var account = TestDataBuilder.CreateAccount();

        // Act
        var result = _validator.Validate(order, account);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "time_in_force" && e.Message.Contains("day"));
    }

    [Fact]
    public void ValidateExtendedHours_LimitOrderWithDay_ReturnsValid()
    {
        // Arrange
        var order = TestDataBuilder.CreateLimitOrder(tif: TimeInForce.Day, extendedHours: true);
        var account = TestDataBuilder.CreateAccount();

        // Act
        var result = _validator.Validate(order, account);

        // Assert - should not have extended hours errors
        result.Errors.Should().NotContain(e => e.Message.Contains("Extended hours"));
    }

    #endregion

    #region Stop Order Tests

    [Fact]
    public void ValidateBuyStop_PriceBelowMarket_ReturnsError()
    {
        // Arrange - buy stop at $140 when market is at $150
        var order = TestDataBuilder.CreateStopOrder(side: OrderSide.Buy, stopPrice: 140m);
        var account = TestDataBuilder.CreateAccount();

        // Act
        var result = _validator.Validate(order, account, currentPrice: 150m);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "stop_price" && e.Message.Contains("above"));
    }

    [Fact]
    public void ValidateBuyStop_PriceAboveMarket_ReturnsValid()
    {
        // Arrange - buy stop at $160 when market is at $150
        var order = TestDataBuilder.CreateStopOrder(side: OrderSide.Buy, stopPrice: 160m);
        var account = TestDataBuilder.CreateAccount();

        // Act
        var result = _validator.Validate(order, account, currentPrice: 150m);

        // Assert
        result.Errors.Should().NotContain(e => e.Field == "stop_price");
    }

    [Fact]
    public void ValidateSellStop_PriceAboveMarket_ReturnsError()
    {
        // Arrange - sell stop at $160 when market is at $150
        var order = TestDataBuilder.CreateStopOrder(side: OrderSide.Sell, stopPrice: 160m);
        var account = TestDataBuilder.CreateAccount();

        // Act
        var result = _validator.Validate(order, account, currentPrice: 150m);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "stop_price" && e.Message.Contains("below"));
    }

    [Fact]
    public void ValidateSellStop_PriceBelowMarket_ReturnsValid()
    {
        // Arrange - sell stop at $140 when market is at $150
        var order = TestDataBuilder.CreateStopOrder(side: OrderSide.Sell, stopPrice: 140m);
        var account = TestDataBuilder.CreateAccount();

        // Act
        var result = _validator.Validate(order, account, currentPrice: 150m);

        // Assert
        result.Errors.Should().NotContain(e => e.Field == "stop_price");
    }

    #endregion

    #region Stop-Limit Premium Tests

    [Theory]
    [InlineData(30.00, 31.20)]    // 4% premium for price < $50: 30 * 1.04 = 31.20
    [InlineData(50.00, 51.25)]    // 2.5% premium for price >= $50: 50 * 1.025 = 51.25
    [InlineData(100.00, 102.50)]  // 2.5% premium for price >= $50: 100 * 1.025 = 102.50
    [InlineData(49.99, 51.99)]    // 4% premium for price < $50: 49.99 * 1.04 ≈ 51.99
    public void CalculateStopLimitPremium_ReturnsCorrectPremium(decimal stopPrice, decimal expectedLimit)
    {
        // Act
        var result = _validator.CalculateStopLimitPremium(stopPrice);

        // Assert
        result.Should().BeApproximately(expectedLimit, 0.01m);
    }

    #endregion

    #region Order Type Requirements Tests

    [Fact]
    public void ValidateLimitOrder_MissingLimitPrice_ReturnsError()
    {
        // Arrange - limit order without limit price
        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = "test",
            AccountId = "test",
            Symbol = "AAPL",
            Qty = 10,
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            LimitPrice = null, // Missing
            TimeInForce = TimeInForce.Day,
            SubmittedAt = DateTimeOffset.UtcNow
        };
        var account = TestDataBuilder.CreateAccount();

        // Act
        var result = _validator.Validate(order, account);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "limit_price" && e.Message.Contains("required"));
    }

    [Fact]
    public void ValidateStopOrder_MissingStopPrice_ReturnsError()
    {
        // Arrange - stop order without stop price
        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = "test",
            AccountId = "test",
            Symbol = "AAPL",
            Qty = 10,
            Side = OrderSide.Sell,
            Type = OrderType.Stop,
            StopPrice = null, // Missing
            TimeInForce = TimeInForce.Day,
            SubmittedAt = DateTimeOffset.UtcNow
        };
        var account = TestDataBuilder.CreateAccount();

        // Act
        var result = _validator.Validate(order, account);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "stop_price" && e.Message.Contains("required"));
    }

    [Fact]
    public void ValidateStopLimitOrder_MissingBothPrices_ReturnsMultipleErrors()
    {
        // Arrange
        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = "test",
            AccountId = "test",
            Symbol = "AAPL",
            Qty = 10,
            Side = OrderSide.Sell,
            Type = OrderType.StopLimit,
            StopPrice = null,
            LimitPrice = null,
            TimeInForce = TimeInForce.Day,
            SubmittedAt = DateTimeOffset.UtcNow
        };
        var account = TestDataBuilder.CreateAccount();

        // Act
        var result = _validator.Validate(order, account);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "stop_price");
        result.Errors.Should().Contain(e => e.Field == "limit_price");
    }

    #endregion

    #region GetDecimalPlaces Tests

    [Theory]
    [InlineData(100, 0)]
    [InlineData(100.1, 1)]
    [InlineData(100.12, 2)]
    [InlineData(100.123, 3)]
    [InlineData(0.1234, 4)]
    [InlineData(0.00001, 5)]
    public void GetDecimalPlaces_ReturnsCorrectCount(decimal value, int expected)
    {
        // Act
        var result = OrderValidator.GetDecimalPlaces(value);

        // Assert
        result.Should().Be(expected);
    }

    #endregion
}
