using AlpacaMock.Domain.Market;
using AlpacaMock.Domain.Tests.Fixtures;
using AlpacaMock.Domain.Trading;
using FluentAssertions;

namespace AlpacaMock.Domain.Tests.Trading;

public class MatchingEngineTests
{
    private readonly MatchingEngine _engine = new();

    #region Market Order Tests

    [Fact]
    public void TryFill_MarketOrder_FillsAtOpen()
    {
        // Arrange
        var order = TestDataBuilder.CreateMarketOrder();
        var bar = TestDataBuilder.CreateBar(open: 150m, high: 152m, low: 149m, close: 151m);

        // Act
        var result = _engine.TryFill(order, bar);

        // Assert
        result.Filled.Should().BeTrue();
        // Market orders fill near open price (with slippage)
        result.FillPrice.Should().BeGreaterThanOrEqualTo(bar.Open);
        result.FillQty.Should().Be(order.Qty);
    }

    [Fact]
    public void TryFill_MarketOrder_AppliesSlippage()
    {
        // Arrange
        var order = TestDataBuilder.CreateMarketOrder(side: OrderSide.Buy);
        var bar = TestDataBuilder.CreateBar(open: 150m, high: 155m, low: 145m, close: 152m);

        // Act
        var result = _engine.TryFill(order, bar);

        // Assert
        result.Filled.Should().BeTrue();
        // Buy order should have adverse slippage (price pushed up)
        result.FillPrice.Should().BeGreaterThan(bar.Open);
        result.FillPrice.Should().BeLessThanOrEqualTo(bar.High);
    }

    [Fact]
    public void TryFill_MarketSellOrder_AppliesAdverseSlippage()
    {
        // Arrange
        var order = TestDataBuilder.CreateMarketOrder(side: OrderSide.Sell);
        var bar = TestDataBuilder.CreateBar(open: 150m, high: 155m, low: 145m, close: 148m);

        // Act
        var result = _engine.TryFill(order, bar);

        // Assert
        result.Filled.Should().BeTrue();
        // Sell order should have adverse slippage (price pushed down)
        result.FillPrice.Should().BeLessThan(bar.Open);
        result.FillPrice.Should().BeGreaterThanOrEqualTo(bar.Low);
    }

    #endregion

    #region Limit Order Tests

    [Fact]
    public void TryFill_BuyLimitOrder_FillsWhenLowTouchesLimit()
    {
        // Arrange
        var order = TestDataBuilder.CreateLimitOrder(side: OrderSide.Buy, limitPrice: 148m);
        var bar = TestDataBuilder.CreateBarThatTriggersBuyLimit("AAPL", 148m);

        // Act
        var result = _engine.TryFill(order, bar);

        // Assert
        result.Filled.Should().BeTrue();
    }

    [Fact]
    public void TryFill_BuyLimitOrder_DoesNotFillWhenLowAboveLimit()
    {
        // Arrange
        var order = TestDataBuilder.CreateLimitOrder(side: OrderSide.Buy, limitPrice: 145m);
        var bar = TestDataBuilder.CreateBarThatMissesBuyLimit("AAPL", 145m);

        // Act
        var result = _engine.TryFill(order, bar);

        // Assert
        result.Filled.Should().BeFalse();
    }

    [Fact]
    public void TryFill_SellLimitOrder_FillsWhenHighTouchesLimit()
    {
        // Arrange
        var order = TestDataBuilder.CreateLimitOrder(side: OrderSide.Sell, limitPrice: 155m);
        var bar = TestDataBuilder.CreateBar(open: 150m, high: 156m, low: 149m, close: 154m);

        // Act
        var result = _engine.TryFill(order, bar);

        // Assert
        result.Filled.Should().BeTrue();
    }

    [Fact]
    public void TryFill_SellLimitOrder_DoesNotFillWhenHighBelowLimit()
    {
        // Arrange
        var order = TestDataBuilder.CreateLimitOrder(side: OrderSide.Sell, limitPrice: 160m);
        var bar = TestDataBuilder.CreateBar(open: 150m, high: 155m, low: 149m, close: 154m);

        // Act
        var result = _engine.TryFill(order, bar);

        // Assert
        result.Filled.Should().BeFalse();
    }

    #endregion

    #region Stop Order Tests

    [Fact]
    public void TryFill_BuyStopOrder_TriggersWhenHighTouchesStop()
    {
        // Arrange
        var order = TestDataBuilder.CreateStopOrder(side: OrderSide.Buy, stopPrice: 155m);
        var bar = TestDataBuilder.CreateBar(open: 150m, high: 156m, low: 149m, close: 154m);

        // Act
        var result = _engine.TryFill(order, bar);

        // Assert
        result.Filled.Should().BeTrue();
    }

    [Fact]
    public void TryFill_BuyStopOrder_DoesNotTriggerWhenHighBelowStop()
    {
        // Arrange
        var order = TestDataBuilder.CreateStopOrder(side: OrderSide.Buy, stopPrice: 160m);
        var bar = TestDataBuilder.CreateBar(open: 150m, high: 155m, low: 149m, close: 154m);

        // Act
        var result = _engine.TryFill(order, bar);

        // Assert
        result.Filled.Should().BeFalse();
    }

    [Fact]
    public void TryFill_SellStopOrder_TriggersWhenLowTouchesStop()
    {
        // Arrange
        var order = TestDataBuilder.CreateStopOrder(side: OrderSide.Sell, stopPrice: 148m);
        var bar = TestDataBuilder.CreateBarThatTriggersSellStop("AAPL", 148m);

        // Act
        var result = _engine.TryFill(order, bar);

        // Assert
        result.Filled.Should().BeTrue();
    }

    [Fact]
    public void TryFill_SellStopOrder_DoesNotTriggerWhenLowAboveStop()
    {
        // Arrange
        var order = TestDataBuilder.CreateStopOrder(side: OrderSide.Sell, stopPrice: 140m);
        var bar = TestDataBuilder.CreateBar(open: 150m, high: 155m, low: 145m, close: 152m);

        // Act
        var result = _engine.TryFill(order, bar);

        // Assert
        result.Filled.Should().BeFalse();
    }

    #endregion

    #region Stop-Limit Order Tests

    [Fact]
    public void TryFill_StopLimitOrder_RequiresBothConditions()
    {
        // Arrange - sell stop-limit: stop at 148, limit at 147
        var order = TestDataBuilder.CreateStopLimitOrder(
            side: OrderSide.Sell,
            stopPrice: 148m,
            limitPrice: 147m);

        // Bar that triggers stop (low touches 147) and fills limit (high above 147)
        var bar = TestDataBuilder.CreateBar(open: 150m, high: 151m, low: 146m, close: 147m);

        // Act
        var result = _engine.TryFill(order, bar);

        // Assert
        result.Filled.Should().BeTrue();
    }

    [Fact]
    public void TryFill_StopLimitOrder_DoesNotFillIfOnlyStopMet()
    {
        // Arrange - buy stop-limit: stop at 155, limit at 154
        var order = TestDataBuilder.CreateStopLimitOrder(
            side: OrderSide.Buy,
            stopPrice: 155m,
            limitPrice: 154m);

        // Bar that triggers stop (high at 156) but doesn't meet limit (low at 155.5)
        var bar = TestDataBuilder.CreateBar(open: 153m, high: 156m, low: 155.5m, close: 155.8m);

        // Act
        var result = _engine.TryFill(order, bar);

        // Assert
        result.Filled.Should().BeFalse();
    }

    #endregion

    #region IOC Order Tests

    [Fact]
    public void ProcessIocOrder_CanFill_ReturnsFill()
    {
        // Arrange
        var order = TestDataBuilder.CreateIocOrder(limitPrice: 152m);
        var bar = TestDataBuilder.CreateBar(open: 150m, high: 153m, low: 149m, close: 151m);
        var currentTime = DateTimeOffset.UtcNow;

        // Act
        var result = _engine.ProcessIocOrder(order, bar, currentTime);

        // Assert
        result.Filled.Should().BeTrue();
    }

    [Fact]
    public void ProcessIocOrder_CannotFill_CancelsOrder()
    {
        // Arrange
        var order = TestDataBuilder.CreateIocOrder(limitPrice: 145m); // Below market
        var bar = TestDataBuilder.CreateBar(open: 150m, high: 153m, low: 149m, close: 151m);
        var currentTime = DateTimeOffset.UtcNow;

        // Act
        var result = _engine.ProcessIocOrder(order, bar, currentTime);

        // Assert
        result.Filled.Should().BeFalse();
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancelledAt.Should().Be(currentTime);
    }

    #endregion

    #region FOK Order Tests

    [Fact]
    public void ProcessFokOrder_SufficientVolume_FillsEntirely()
    {
        // Arrange
        var order = TestDataBuilder.CreateFokOrder(qty: 100, limitPrice: 152m);
        var bar = TestDataBuilder.CreateBar(
            open: 150m, high: 153m, low: 149m, close: 151m,
            volume: 1_000_000); // 1% = 10,000 shares available
        var currentTime = DateTimeOffset.UtcNow;

        // Act
        var result = _engine.ProcessFokOrder(order, bar, currentTime);

        // Assert
        result.Filled.Should().BeTrue();
        result.FillQty.Should().Be(order.Qty);
        result.IsPartial.Should().BeFalse();
    }

    [Fact]
    public void ProcessFokOrder_InsufficientVolume_Rejects()
    {
        // Arrange
        var order = TestDataBuilder.CreateFokOrder(qty: 10000, limitPrice: 152m);
        var bar = TestDataBuilder.CreateLowVolumeBar("AAPL", 150m, volume: 100); // Only 1 share available
        var currentTime = DateTimeOffset.UtcNow;

        // Act
        var result = _engine.ProcessFokOrder(order, bar, currentTime);

        // Assert
        result.Filled.Should().BeFalse();
        order.Status.Should().Be(OrderStatus.Rejected);
    }

    [Fact]
    public void ProcessFokOrder_PriceNotMet_Rejects()
    {
        // Arrange
        var order = TestDataBuilder.CreateFokOrder(qty: 10, limitPrice: 145m); // Below market
        var bar = TestDataBuilder.CreateBar(open: 150m, high: 153m, low: 149m, close: 151m);
        var currentTime = DateTimeOffset.UtcNow;

        // Act
        var result = _engine.ProcessFokOrder(order, bar, currentTime);

        // Assert
        result.Filled.Should().BeFalse();
        order.Status.Should().Be(OrderStatus.Rejected);
    }

    #endregion

    #region GTC Expiration Tests

    [Fact]
    public void ExpireGtcOrders_OrderOver90Days_MarksExpired()
    {
        // Arrange
        var submittedAt = DateTimeOffset.UtcNow.AddDays(-91);
        var order = TestDataBuilder.CreateGtcOrder(submittedAt: submittedAt);
        var orders = new[] { order };
        var currentTime = DateTimeOffset.UtcNow;

        // Act
        _engine.ExpireGtcOrders(orders, currentTime);

        // Assert
        order.Status.Should().Be(OrderStatus.Expired);
        order.ExpiredAt.Should().Be(currentTime);
    }

    [Fact]
    public void ExpireGtcOrders_OrderUnder90Days_RemainsActive()
    {
        // Arrange
        var submittedAt = DateTimeOffset.UtcNow.AddDays(-89);
        var order = TestDataBuilder.CreateGtcOrder(submittedAt: submittedAt);
        var orders = new[] { order };
        var currentTime = DateTimeOffset.UtcNow;

        // Act
        _engine.ExpireGtcOrders(orders, currentTime);

        // Assert
        order.Status.Should().Be(OrderStatus.Accepted); // Unchanged
        order.ExpiredAt.Should().BeNull();
    }

    #endregion

    #region Day Order Expiration Tests

    [Fact]
    public void ExpireDayOrders_OrderPastSubmissionDate_MarksExpired()
    {
        // Arrange
        var order = TestDataBuilder.CreateMarketOrder();
        // Manually set submission date to yesterday
        var yesterdayOrder = new Order
        {
            Id = order.Id,
            SessionId = order.SessionId,
            AccountId = order.AccountId,
            Symbol = order.Symbol,
            Qty = order.Qty,
            Side = order.Side,
            Type = OrderType.Market,
            TimeInForce = TimeInForce.Day,
            Status = OrderStatus.Accepted,
            SubmittedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        var orders = new[] { yesterdayOrder };
        var currentTime = DateTimeOffset.UtcNow;

        // Act
        _engine.ExpireDayOrders(orders, currentTime);

        // Assert
        yesterdayOrder.Status.Should().Be(OrderStatus.Expired);
    }

    #endregion

    #region Volume-Based Partial Fill Tests

    [Fact]
    public void TryFill_LargeOrder_PartialFillBasedOnVolume()
    {
        // Arrange
        var order = TestDataBuilder.CreateMarketOrder(qty: 100000); // Large order
        var bar = TestDataBuilder.CreateBar(volume: 10000); // Small volume: 1% = 100 shares

        // Act
        var result = _engine.TryFill(order, bar);

        // Assert
        result.Filled.Should().BeTrue();
        result.FillQty.Should().BeLessThan(order.Qty);
        result.IsPartial.Should().BeTrue();
    }

    [Fact]
    public void TryFill_SmallOrder_FullFill()
    {
        // Arrange
        var order = TestDataBuilder.CreateMarketOrder(qty: 10);
        var bar = TestDataBuilder.CreateBar(volume: 1_000_000); // Large volume

        // Act
        var result = _engine.TryFill(order, bar);

        // Assert
        result.Filled.Should().BeTrue();
        result.FillQty.Should().Be(order.Qty);
        result.IsPartial.Should().BeFalse();
    }

    #endregion

    #region Terminal Order Tests

    [Fact]
    public void TryFill_FilledOrder_ReturnsNoFill()
    {
        // Arrange
        var order = TestDataBuilder.CreateMarketOrder();
        order.Status = OrderStatus.Filled;
        var bar = TestDataBuilder.CreateBar();

        // Act
        var result = _engine.TryFill(order, bar);

        // Assert
        result.Filled.Should().BeFalse();
    }

    [Fact]
    public void TryFill_CancelledOrder_ReturnsNoFill()
    {
        // Arrange
        var order = TestDataBuilder.CreateMarketOrder();
        order.Status = OrderStatus.Cancelled;
        var bar = TestDataBuilder.CreateBar();

        // Act
        var result = _engine.TryFill(order, bar);

        // Assert
        result.Filled.Should().BeFalse();
    }

    #endregion

    #region ProcessOrders Tests

    [Fact]
    public void ProcessOrders_SkipsTerminalOrders()
    {
        // Arrange
        var order = TestDataBuilder.CreateMarketOrder();
        order.Status = OrderStatus.Filled;
        var bars = new Dictionary<string, Bar>
        {
            ["AAPL"] = TestDataBuilder.CreateBar()
        };

        // Act
        var fills = _engine.ProcessOrders(new[] { order }, bars, DateTimeOffset.UtcNow).ToList();

        // Assert
        fills.Should().BeEmpty();
    }

    [Fact]
    public void ProcessOrders_IocOrderWithNoBarData_CancelsOrder()
    {
        // Arrange
        var order = TestDataBuilder.CreateIocOrder(limitPrice: 150m);
        var bars = new Dictionary<string, Bar>(); // No bar data for AAPL
        var currentTime = DateTimeOffset.UtcNow;

        // Act
        var fills = _engine.ProcessOrders(new[] { order }, bars, currentTime).ToList();

        // Assert
        fills.Should().BeEmpty();
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancelledAt.Should().Be(currentTime);
    }

    [Fact]
    public void ProcessOrders_FokOrderWithNoBarData_RejectsOrder()
    {
        // Arrange
        var order = TestDataBuilder.CreateFokOrder(qty: 100, limitPrice: 150m);
        var bars = new Dictionary<string, Bar>(); // No bar data for AAPL
        var currentTime = DateTimeOffset.UtcNow;

        // Act
        var fills = _engine.ProcessOrders(new[] { order }, bars, currentTime).ToList();

        // Assert
        fills.Should().BeEmpty();
        order.Status.Should().Be(OrderStatus.Rejected);
        order.FailedAt.Should().Be(currentTime);
    }

    [Fact]
    public void ProcessOrders_DayOrderExpired_MarksExpired()
    {
        // Arrange - create order submitted yesterday
        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = "test-session",
            AccountId = "test-account",
            Symbol = "AAPL",
            Qty = 10,
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            TimeInForce = TimeInForce.Day,
            Status = OrderStatus.Accepted,
            SubmittedAt = DateTimeOffset.UtcNow.AddDays(-1) // Yesterday
        };
        var bars = new Dictionary<string, Bar>
        {
            ["AAPL"] = TestDataBuilder.CreateBar()
        };
        var currentTime = DateTimeOffset.UtcNow;

        // Act
        var fills = _engine.ProcessOrders(new[] { order }, bars, currentTime).ToList();

        // Assert
        fills.Should().BeEmpty();
        order.Status.Should().Be(OrderStatus.Expired);
        order.ExpiredAt.Should().Be(currentTime);
    }

    [Fact]
    public void ProcessOrders_GtcOrderExpired_MarksExpired()
    {
        // Arrange
        var order = TestDataBuilder.CreateGtcOrder(submittedAt: DateTimeOffset.UtcNow.AddDays(-91));
        var bars = new Dictionary<string, Bar>
        {
            ["AAPL"] = TestDataBuilder.CreateBar()
        };
        var currentTime = DateTimeOffset.UtcNow;

        // Act
        var fills = _engine.ProcessOrders(new[] { order }, bars, currentTime).ToList();

        // Assert
        fills.Should().BeEmpty();
        order.Status.Should().Be(OrderStatus.Expired);
        order.ExpiredAt.Should().Be(currentTime);
    }

    [Fact]
    public void ProcessOrders_ValidOrder_ReturnsFill()
    {
        // Arrange
        var order = TestDataBuilder.CreateMarketOrder();
        var bars = new Dictionary<string, Bar>
        {
            ["AAPL"] = TestDataBuilder.CreateBar(open: 150m, high: 152m, low: 148m, close: 151m, volume: 1_000_000)
        };
        var currentTime = DateTimeOffset.UtcNow;

        // Act
        var fills = _engine.ProcessOrders(new[] { order }, bars, currentTime).ToList();

        // Assert
        fills.Should().HaveCount(1);
        fills[0].Order.Should().Be(order);
        fills[0].Fill.Filled.Should().BeTrue();
    }

    [Fact]
    public void ProcessOrders_IocOrder_ProcessesWithIocLogic()
    {
        // Arrange
        var order = TestDataBuilder.CreateIocOrder(limitPrice: 152m);
        var bars = new Dictionary<string, Bar>
        {
            ["AAPL"] = TestDataBuilder.CreateBar(open: 150m, high: 153m, low: 149m, close: 151m, volume: 1_000_000)
        };
        var currentTime = DateTimeOffset.UtcNow;

        // Act
        var fills = _engine.ProcessOrders(new[] { order }, bars, currentTime).ToList();

        // Assert
        fills.Should().HaveCount(1);
        fills[0].Fill.Filled.Should().BeTrue();
    }

    [Fact]
    public void ProcessOrders_FokOrder_ProcessesWithFokLogic()
    {
        // Arrange
        var order = TestDataBuilder.CreateFokOrder(qty: 100, limitPrice: 152m);
        var bars = new Dictionary<string, Bar>
        {
            ["AAPL"] = TestDataBuilder.CreateBar(open: 150m, high: 153m, low: 149m, close: 151m, volume: 1_000_000)
        };
        var currentTime = DateTimeOffset.UtcNow;

        // Act
        var fills = _engine.ProcessOrders(new[] { order }, bars, currentTime).ToList();

        // Assert
        fills.Should().HaveCount(1);
        fills[0].Fill.Filled.Should().BeTrue();
        fills[0].Fill.IsPartial.Should().BeFalse();
    }

    [Fact]
    public void ProcessOrders_OrderDoesNotMeetPriceCondition_NoFill()
    {
        // Arrange - limit order that won't fill
        var order = TestDataBuilder.CreateLimitOrder(side: OrderSide.Buy, limitPrice: 145m);
        var bars = new Dictionary<string, Bar>
        {
            ["AAPL"] = TestDataBuilder.CreateBar(open: 150m, high: 155m, low: 149m, close: 152m)
        };
        var currentTime = DateTimeOffset.UtcNow;

        // Act
        var fills = _engine.ProcessOrders(new[] { order }, bars, currentTime).ToList();

        // Assert
        fills.Should().BeEmpty();
    }

    #endregion

    #region Slippage Edge Cases

    [Fact]
    public void TryFill_ZeroRange_NoSlippageApplied()
    {
        // Arrange - flat bar with no price movement
        var order = TestDataBuilder.CreateMarketOrder(side: OrderSide.Buy);
        var bar = TestDataBuilder.CreateBar(open: 150m, high: 150m, low: 150m, close: 150m);

        // Act
        var result = _engine.TryFill(order, bar);

        // Assert
        result.Filled.Should().BeTrue();
        result.FillPrice.Should().Be(150m); // No slippage when range is zero
    }

    #endregion
}
