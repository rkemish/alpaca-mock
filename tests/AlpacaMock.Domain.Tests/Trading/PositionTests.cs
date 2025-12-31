using AlpacaMock.Domain.Tests.Fixtures;
using AlpacaMock.Domain.Trading;
using FluentAssertions;

namespace AlpacaMock.Domain.Tests.Trading;

public class PositionTests
{
    #region ApplyFill Tests

    [Fact]
    public void ApplyFill_OpeningLongPosition_SetsCorrectAvgPrice()
    {
        // Arrange
        var position = TestDataBuilder.CreatePosition(qty: 0, avgPrice: 0);

        // Act
        position.ApplyFill(fillQty: 100, fillPrice: 150m, side: OrderSide.Buy);

        // Assert
        position.Qty.Should().Be(100);
        position.AvgEntryPrice.Should().Be(150m);
        position.Side.Should().Be(PositionSide.Long);
    }

    [Fact]
    public void ApplyFill_AddingToLongPosition_CalculatesWeightedAvgPrice()
    {
        // Arrange - existing 100 shares at $150
        var position = TestDataBuilder.CreatePosition(qty: 100, avgPrice: 150m);

        // Act - buy 100 more at $160
        position.ApplyFill(fillQty: 100, fillPrice: 160m, side: OrderSide.Buy);

        // Assert - weighted average: (100*150 + 100*160) / 200 = 155
        position.Qty.Should().Be(200);
        position.AvgEntryPrice.Should().Be(155m);
    }

    [Fact]
    public void ApplyFill_ClosingPosition_ZerosOutQtyAndAvgPrice()
    {
        // Arrange - existing 100 shares
        var position = TestDataBuilder.CreatePosition(qty: 100, avgPrice: 150m);

        // Act - sell all 100 shares
        position.ApplyFill(fillQty: 100, fillPrice: 160m, side: OrderSide.Sell);

        // Assert
        position.Qty.Should().Be(0);
        position.AvgEntryPrice.Should().Be(0);
    }

    [Fact]
    public void ApplyFill_PartialClose_MaintainsOriginalAvgPrice()
    {
        // Arrange - existing 100 shares at $150
        var position = TestDataBuilder.CreatePosition(qty: 100, avgPrice: 150m);

        // Act - sell 50 shares
        position.ApplyFill(fillQty: 50, fillPrice: 160m, side: OrderSide.Sell);

        // Assert - avg price unchanged for partial close of long
        position.Qty.Should().Be(50);
        position.AvgEntryPrice.Should().Be(150m);
    }

    [Fact]
    public void ApplyFill_FlippingFromLongToShort_SetsNewAvgPrice()
    {
        // Arrange - existing 100 shares long at $150
        var position = TestDataBuilder.CreatePosition(qty: 100, avgPrice: 150m);

        // Act - sell 150 shares (flip to short 50)
        position.ApplyFill(fillQty: 150, fillPrice: 160m, side: OrderSide.Sell);

        // Assert
        position.Qty.Should().Be(-50);
        position.AvgEntryPrice.Should().Be(160m); // New entry at flip price
        position.Side.Should().Be(PositionSide.Short);
    }

    [Fact]
    public void ApplyFill_OpeningShortPosition_SetsCorrectAvgPrice()
    {
        // Arrange
        var position = TestDataBuilder.CreatePosition(qty: 0, avgPrice: 0);

        // Act - sell short 100 shares
        position.ApplyFill(fillQty: 100, fillPrice: 150m, side: OrderSide.Sell);

        // Assert
        position.Qty.Should().Be(-100);
        position.AvgEntryPrice.Should().Be(150m);
        position.Side.Should().Be(PositionSide.Short);
    }

    [Fact]
    public void ApplyFill_AddingToShortPosition_CalculatesWeightedAvgPrice()
    {
        // Arrange - existing short 100 shares at $150
        var position = TestDataBuilder.CreatePosition(qty: -100, avgPrice: 150m);

        // Act - sell short 100 more at $140
        position.ApplyFill(fillQty: 100, fillPrice: 140m, side: OrderSide.Sell);

        // Assert - weighted average: (100*150 + 100*140) / 200 = 145
        position.Qty.Should().Be(-200);
        position.AvgEntryPrice.Should().Be(145m);
    }

    [Fact]
    public void ApplyFill_CoveringShortPosition_ClosesPosition()
    {
        // Arrange - existing short 100 shares
        var position = TestDataBuilder.CreatePosition(qty: -100, avgPrice: 150m);

        // Act - buy to cover
        position.ApplyFill(fillQty: 100, fillPrice: 140m, side: OrderSide.Buy);

        // Assert
        position.Qty.Should().Be(0);
        position.AvgEntryPrice.Should().Be(0);
    }

    #endregion

    #region P&L Calculation Tests

    [Fact]
    public void UpdatePrices_LongPosition_CalculatesCorrectUnrealizedPnL()
    {
        // Arrange - long 100 shares at $150
        var position = TestDataBuilder.CreatePosition(qty: 100, avgPrice: 150m);

        // Act - price rises to $160
        position.UpdatePrices(currentPrice: 160m);

        // Assert
        position.MarketValue.Should().Be(16000m);
        position.CostBasis.Should().Be(15000m);
        position.UnrealizedPnL.Should().Be(1000m); // $10 * 100 shares
    }

    [Fact]
    public void UpdatePrices_LongPosition_CalculatesNegativePnL()
    {
        // Arrange - long 100 shares at $150
        var position = TestDataBuilder.CreatePosition(qty: 100, avgPrice: 150m);

        // Act - price drops to $140
        position.UpdatePrices(currentPrice: 140m);

        // Assert
        position.UnrealizedPnL.Should().Be(-1000m); // -$10 * 100 shares
    }

    [Fact]
    public void UpdatePrices_ShortPosition_CalculatesCorrectUnrealizedPnL()
    {
        // Arrange - short 100 shares at $150
        var position = TestDataBuilder.CreatePosition(qty: -100, avgPrice: 150m);

        // Act - price drops to $140 (profit for short)
        position.UpdatePrices(currentPrice: 140m);

        // Assert
        position.MarketValue.Should().Be(-14000m); // Negative for short
        position.UnrealizedPnL.Should().Be(1000m); // Profit: shorted at 150, now 140
    }

    [Fact]
    public void UpdatePrices_ShortPosition_CalculatesLoss()
    {
        // Arrange - short 100 shares at $150
        var position = TestDataBuilder.CreatePosition(qty: -100, avgPrice: 150m);

        // Act - price rises to $160 (loss for short)
        position.UpdatePrices(currentPrice: 160m);

        // Assert
        position.UnrealizedPnL.Should().Be(-1000m); // Loss: shorted at 150, now 160
    }

    [Fact]
    public void UnrealizedPnLPercent_CalculatesCorrectPercentage()
    {
        // Arrange - long 100 shares at $100
        var position = TestDataBuilder.CreatePosition(qty: 100, avgPrice: 100m);
        position.UpdatePrices(currentPrice: 110m);

        // Assert
        position.CostBasis.Should().Be(10000m);
        position.UnrealizedPnL.Should().Be(1000m);
        position.UnrealizedPnLPercent.Should().BeApproximately(0.10m, 0.001m); // 10%
    }

    [Fact]
    public void UnrealizedPnLPercent_ZeroCostBasis_ReturnsZero()
    {
        // Arrange
        var position = TestDataBuilder.CreatePosition(qty: 0, avgPrice: 0);

        // Assert
        position.UnrealizedPnLPercent.Should().Be(0);
    }

    #endregion

    #region Market Value Tests

    [Fact]
    public void UpdatePrices_LongPosition_PositiveMarketValue()
    {
        // Arrange
        var position = TestDataBuilder.CreatePosition(qty: 100, avgPrice: 150m);

        // Act
        position.UpdatePrices(currentPrice: 155m);

        // Assert
        position.MarketValue.Should().Be(15500m);
        position.MarketValue.Should().BePositive();
    }

    [Fact]
    public void UpdatePrices_ShortPosition_NegativeMarketValue()
    {
        // Arrange
        var position = TestDataBuilder.CreatePosition(qty: -100, avgPrice: 150m);

        // Act
        position.UpdatePrices(currentPrice: 155m);

        // Assert
        position.MarketValue.Should().Be(-15500m);
        position.MarketValue.Should().BeNegative();
    }

    #endregion

    #region Intraday P&L Tests

    [Fact]
    public void UpdatePrices_CalculatesIntradayPnLFromLastDayPrice()
    {
        // Arrange
        var position = TestDataBuilder.CreatePosition(qty: 100, avgPrice: 150m);

        // Act - yesterday closed at $152, now at $155
        position.UpdatePrices(currentPrice: 155m, lastDayPrice: 152m);

        // Assert
        position.UnrealizedIntradayPnL.Should().Be(300m); // $3 * 100 shares
    }

    [Fact]
    public void ChangeToday_CalculatesPercentageChange()
    {
        // Arrange
        var position = TestDataBuilder.CreatePosition(qty: 100, avgPrice: 150m);

        // Act - yesterday closed at $100, now at $105
        position.UpdatePrices(currentPrice: 105m, lastDayPrice: 100m);

        // Assert
        position.ChangeToday.Should().BeApproximately(0.05m, 0.001m); // 5%
    }

    [Fact]
    public void ChangeToday_ZeroLastDayPrice_ReturnsZero()
    {
        // Arrange
        var position = TestDataBuilder.CreatePosition(qty: 100, avgPrice: 150m);
        position.LastDayPrice = 0;
        position.CurrentPrice = 155m;

        // Assert
        position.ChangeToday.Should().Be(0);
    }

    #endregion

    #region CostBasis Tests

    [Fact]
    public void CostBasis_LongPosition_ReturnsCorrectValue()
    {
        // Arrange
        var position = TestDataBuilder.CreatePosition(qty: 100, avgPrice: 150m);

        // Assert
        position.CostBasis.Should().Be(15000m);
    }

    [Fact]
    public void CostBasis_ShortPosition_ReturnsAbsoluteValue()
    {
        // Arrange
        var position = TestDataBuilder.CreatePosition(qty: -100, avgPrice: 150m);

        // Assert - cost basis uses absolute qty
        position.CostBasis.Should().Be(15000m);
    }

    #endregion
}
