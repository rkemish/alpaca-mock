using AlpacaMock.Domain.Trading;
using FluentAssertions;

namespace AlpacaMock.Domain.Tests.Trading;

public class OrderTests
{
    [Theory]
    [InlineData(OrderStatus.Filled)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Expired)]
    [InlineData(OrderStatus.Rejected)]
    [InlineData(OrderStatus.Replaced)]
    public void IsTerminal_ReturnsTrue_ForTerminalStatuses(OrderStatus status)
    {
        var order = CreateOrder(status: status);
        order.IsTerminal.Should().BeTrue();
    }

    [Theory]
    [InlineData(OrderStatus.New)]
    [InlineData(OrderStatus.Accepted)]
    [InlineData(OrderStatus.PendingNew)]
    [InlineData(OrderStatus.PartiallyFilled)]
    public void IsTerminal_ReturnsFalse_ForActiveStatuses(OrderStatus status)
    {
        var order = CreateOrder(status: status);
        order.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void IsActive_ReturnsTrue_ForActiveStatuses()
    {
        var order = CreateOrder(status: OrderStatus.Accepted);
        order.IsActive.Should().BeTrue();
    }

    [Fact]
    public void ShouldCancelRemainingIoc_ReturnsTrue_WhenIocWithRemainingQty()
    {
        var order = CreateOrder(tif: TimeInForce.Ioc, qty: 100);
        order.FilledQty = 50;

        order.ShouldCancelRemainingIoc().Should().BeTrue();
    }

    [Fact]
    public void ShouldCancelRemainingIoc_ReturnsFalse_WhenNotIoc()
    {
        var order = CreateOrder(tif: TimeInForce.Day, qty: 100);
        order.FilledQty = 50;

        order.ShouldCancelRemainingIoc().Should().BeFalse();
    }

    [Fact]
    public void ShouldCancelRemainingIoc_ReturnsFalse_WhenFullyFilled()
    {
        var order = CreateOrder(tif: TimeInForce.Ioc, qty: 100);
        order.FilledQty = 100;

        order.ShouldCancelRemainingIoc().Should().BeFalse();
    }

    [Fact]
    public void ShouldRejectFok_ReturnsTrue_WhenFokAndInsufficientQty()
    {
        var order = CreateOrder(tif: TimeInForce.Fok, qty: 100);
        order.ShouldRejectFok(availableQty: 50).Should().BeTrue();
    }

    [Fact]
    public void ShouldRejectFok_ReturnsFalse_WhenFokAndSufficientQty()
    {
        var order = CreateOrder(tif: TimeInForce.Fok, qty: 100);
        order.ShouldRejectFok(availableQty: 100).Should().BeFalse();
    }

    [Fact]
    public void ShouldRejectFok_ReturnsFalse_WhenNotFok()
    {
        var order = CreateOrder(tif: TimeInForce.Day, qty: 100);
        order.ShouldRejectFok(availableQty: 50).Should().BeFalse();
    }

    [Fact]
    public void OptionalProperties_CanBeAccessed()
    {
        var order = new Order
        {
            Id = "test-id",
            SessionId = "test-session",
            AccountId = "test-account",
            Symbol = "AAPL",
            Qty = 10,
            ClientOrderId = "client-123",
            AssetId = "asset-uuid",
            FilledAvgPrice = 150.50m,
            TrailPrice = 5m,
            TrailPercent = 0.05m,
            FilledAt = DateTimeOffset.UtcNow,
            ReplacedBy = "new-order-id",
            Replaces = "old-order-id",
            SubmittedAt = DateTimeOffset.UtcNow
        };

        order.ClientOrderId.Should().Be("client-123");
        order.AssetId.Should().Be("asset-uuid");
        order.FilledAvgPrice.Should().Be(150.50m);
        order.TrailPrice.Should().Be(5m);
        order.TrailPercent.Should().Be(0.05m);
        order.FilledAt.Should().NotBeNull();
        order.ReplacedBy.Should().Be("new-order-id");
        order.Replaces.Should().Be("old-order-id");
    }

    [Fact]
    public void CanFillAtPrice_StopLimitBuy_ReturnsTrueWhenBothConditionsMet()
    {
        var order = CreateOrder(
            type: OrderType.StopLimit,
            side: OrderSide.Buy,
            stopPrice: 150m,
            limitPrice: 152m);

        // High >= StopPrice (triggered) AND Low <= LimitPrice (can fill)
        var result = order.CanFillAtPrice(price: 151m, high: 153m, low: 149m);
        result.Should().BeTrue();
    }

    [Fact]
    public void CanFillAtPrice_StopLimitBuy_ReturnsFalseWhenStopNotTriggered()
    {
        var order = CreateOrder(
            type: OrderType.StopLimit,
            side: OrderSide.Buy,
            stopPrice: 155m,
            limitPrice: 152m);

        // High < StopPrice (not triggered)
        var result = order.CanFillAtPrice(price: 151m, high: 153m, low: 149m);
        result.Should().BeFalse();
    }

    [Fact]
    public void CanFillAtPrice_StopLimitSell_ReturnsTrueWhenBothConditionsMet()
    {
        var order = CreateOrder(
            type: OrderType.StopLimit,
            side: OrderSide.Sell,
            stopPrice: 150m,
            limitPrice: 148m);

        // Low <= StopPrice (triggered) AND High >= LimitPrice (can fill)
        var result = order.CanFillAtPrice(price: 149m, high: 151m, low: 147m);
        result.Should().BeTrue();
    }

    [Fact]
    public void CanFillAtPrice_UnknownOrderType_ReturnsFalse()
    {
        var order = CreateOrder(type: OrderType.TrailingStop);
        var result = order.CanFillAtPrice(price: 150m, high: 155m, low: 145m);
        result.Should().BeFalse();
    }

    [Fact]
    public void GetExecutionPrice_StopOrder_Buy_ReturnsMaxOfOpenAndStopPrice()
    {
        var order = CreateOrder(
            type: OrderType.Stop,
            side: OrderSide.Buy,
            stopPrice: 155m);

        // When open is below stop, use stop price
        var price1 = order.GetExecutionPrice(open: 150m, high: 160m, low: 148m, close: 158m);
        price1.Should().Be(155m);

        // When open is above stop, use open price
        var order2 = CreateOrder(type: OrderType.Stop, side: OrderSide.Buy, stopPrice: 145m);
        var price2 = order2.GetExecutionPrice(open: 150m, high: 160m, low: 148m, close: 158m);
        price2.Should().Be(150m);
    }

    [Fact]
    public void GetExecutionPrice_StopOrder_Sell_ReturnsMinOfOpenAndStopPrice()
    {
        var order = CreateOrder(
            type: OrderType.Stop,
            side: OrderSide.Sell,
            stopPrice: 145m);

        // When open is above stop, use stop price
        var price1 = order.GetExecutionPrice(open: 150m, high: 155m, low: 143m, close: 144m);
        price1.Should().Be(145m);

        // When open is below stop, use open price
        var order2 = CreateOrder(type: OrderType.Stop, side: OrderSide.Sell, stopPrice: 155m);
        var price2 = order2.GetExecutionPrice(open: 150m, high: 160m, low: 148m, close: 152m);
        price2.Should().Be(150m);
    }

    [Fact]
    public void GetExecutionPrice_StopLimitOrder_ReturnsLimitPrice()
    {
        var order = CreateOrder(
            type: OrderType.StopLimit,
            side: OrderSide.Sell,
            stopPrice: 150m,
            limitPrice: 148m);

        var price = order.GetExecutionPrice(open: 149m, high: 151m, low: 147m, close: 148m);
        price.Should().Be(148m);
    }

    [Fact]
    public void GetExecutionPrice_UnknownOrderType_ReturnsOpen()
    {
        var order = CreateOrder(type: OrderType.TrailingStop);
        var price = order.GetExecutionPrice(open: 150m, high: 155m, low: 145m, close: 152m);
        price.Should().Be(150m);
    }

    [Fact]
    public void RemainingQty_CalculatesCorrectly()
    {
        var order = CreateOrder(qty: 100);
        order.FilledQty = 30;
        order.RemainingQty.Should().Be(70);
    }

    private static Order CreateOrder(
        OrderStatus status = OrderStatus.New,
        TimeInForce tif = TimeInForce.Day,
        OrderType type = OrderType.Market,
        OrderSide side = OrderSide.Buy,
        decimal qty = 100,
        decimal? stopPrice = null,
        decimal? limitPrice = null)
    {
        return new Order
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = "test-session",
            AccountId = "test-account",
            Symbol = "AAPL",
            Qty = qty,
            Side = side,
            Type = type,
            TimeInForce = tif,
            Status = status,
            StopPrice = stopPrice,
            LimitPrice = limitPrice,
            SubmittedAt = DateTimeOffset.UtcNow
        };
    }
}
