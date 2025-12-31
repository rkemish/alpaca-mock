using AlpacaMock.Domain.Trading;
using FluentAssertions;

namespace AlpacaMock.Domain.Tests.Trading;

public class DayTradeTrackerTests
{
    private readonly DayTradeTracker _tracker = new();
    private const string AccountId = "test-account";

    #region Day Trade Counting Tests

    [Fact]
    public void GetDayTradeCount_BuyAndSellSameDay_CountsAsOneDayTrade()
    {
        // Arrange
        var today = DateTimeOffset.UtcNow;
        _tracker.RecordTrade(AccountId, "AAPL", OrderSide.Buy, 100, today.AddHours(-2));
        _tracker.RecordTrade(AccountId, "AAPL", OrderSide.Sell, 100, today.AddHours(-1));

        // Act
        var count = _tracker.GetDayTradeCount(AccountId, today);

        // Assert
        count.Should().Be(1);
    }

    [Fact]
    public void GetDayTradeCount_BuyAndSellDifferentDays_DoesNotCount()
    {
        // Arrange
        var today = DateTimeOffset.UtcNow;
        var yesterday = today.AddDays(-1);
        _tracker.RecordTrade(AccountId, "AAPL", OrderSide.Buy, 100, yesterday);
        _tracker.RecordTrade(AccountId, "AAPL", OrderSide.Sell, 100, today);

        // Act
        var count = _tracker.GetDayTradeCount(AccountId, today);

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public void GetDayTradeCount_MultipleDayTrades_CountsEachUniquely()
    {
        // Arrange
        var today = DateTimeOffset.UtcNow;

        // Day trade 1: AAPL
        _tracker.RecordTrade(AccountId, "AAPL", OrderSide.Buy, 100, today.AddHours(-4));
        _tracker.RecordTrade(AccountId, "AAPL", OrderSide.Sell, 100, today.AddHours(-3));

        // Day trade 2: MSFT
        _tracker.RecordTrade(AccountId, "MSFT", OrderSide.Buy, 50, today.AddHours(-2));
        _tracker.RecordTrade(AccountId, "MSFT", OrderSide.Sell, 50, today.AddHours(-1));

        // Act
        var count = _tracker.GetDayTradeCount(AccountId, today);

        // Assert
        count.Should().Be(2);
    }

    [Fact]
    public void GetDayTradeCount_TradesOver5DaysAgo_NotCounted()
    {
        // Arrange
        var today = DateTimeOffset.UtcNow;
        var sixDaysAgo = today.AddDays(-6);

        _tracker.RecordTrade(AccountId, "AAPL", OrderSide.Buy, 100, sixDaysAgo.AddHours(1));
        _tracker.RecordTrade(AccountId, "AAPL", OrderSide.Sell, 100, sixDaysAgo.AddHours(2));

        // Act
        var count = _tracker.GetDayTradeCount(AccountId, today);

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public void GetDayTradeCount_OnlyBuyNoSell_NotADayTrade()
    {
        // Arrange
        var today = DateTimeOffset.UtcNow;
        _tracker.RecordTrade(AccountId, "AAPL", OrderSide.Buy, 100, today.AddHours(-1));
        // No sell

        // Act
        var count = _tracker.GetDayTradeCount(AccountId, today);

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public void GetDayTradeCount_DifferentAccountsSeparated()
    {
        // Arrange
        var today = DateTimeOffset.UtcNow;

        // Account 1 day trade
        _tracker.RecordTrade("account-1", "AAPL", OrderSide.Buy, 100, today.AddHours(-2));
        _tracker.RecordTrade("account-1", "AAPL", OrderSide.Sell, 100, today.AddHours(-1));

        // Account 2 only buy
        _tracker.RecordTrade("account-2", "AAPL", OrderSide.Buy, 100, today.AddHours(-1));

        // Act
        var count1 = _tracker.GetDayTradeCount("account-1", today);
        var count2 = _tracker.GetDayTradeCount("account-2", today);

        // Assert
        count1.Should().Be(1);
        count2.Should().Be(0);
    }

    #endregion

    #region PDT Status Tests

    [Fact]
    public void IsPdt_FourOrMoreDayTrades_ReturnsTrue()
    {
        // Arrange
        var today = DateTimeOffset.UtcNow;

        // Create 4 day trades over the past 5 days
        for (int i = 0; i < 4; i++)
        {
            var date = today.AddDays(-i);
            _tracker.RecordTrade(AccountId, $"SYM{i}", OrderSide.Buy, 100, date.AddHours(-2));
            _tracker.RecordTrade(AccountId, $"SYM{i}", OrderSide.Sell, 100, date.AddHours(-1));
        }

        // Act
        var isPdt = _tracker.IsPdt(AccountId, today);

        // Assert
        isPdt.Should().BeTrue();
    }

    [Fact]
    public void IsPdt_LessThanFourDayTrades_ReturnsFalse()
    {
        // Arrange
        var today = DateTimeOffset.UtcNow;

        // Create 3 day trades
        for (int i = 0; i < 3; i++)
        {
            var date = today.AddDays(-i);
            _tracker.RecordTrade(AccountId, $"SYM{i}", OrderSide.Buy, 100, date.AddHours(-2));
            _tracker.RecordTrade(AccountId, $"SYM{i}", OrderSide.Sell, 100, date.AddHours(-1));
        }

        // Act
        var isPdt = _tracker.IsPdt(AccountId, today);

        // Assert
        isPdt.Should().BeFalse();
    }

    #endregion

    #region WouldBeDayTrade Tests

    [Fact]
    public void WouldBeDayTrade_HasOppositeSideSameDay_ReturnsTrue()
    {
        // Arrange - use fixed timestamps to avoid date boundary issues
        var checkTime = new DateTimeOffset(2024, 6, 15, 15, 0, 0, TimeSpan.Zero);
        var buyTime = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero);
        _tracker.RecordTrade(AccountId, "AAPL", OrderSide.Buy, 100, buyTime);

        // Act - selling would close the same-day position
        var wouldBe = _tracker.WouldBeDayTrade(AccountId, "AAPL", OrderSide.Sell, checkTime);

        // Assert
        wouldBe.Should().BeTrue();
    }

    [Fact]
    public void WouldBeDayTrade_NoOppositeSide_ReturnsFalse()
    {
        // Arrange
        var today = DateTimeOffset.UtcNow;
        _tracker.RecordTrade(AccountId, "AAPL", OrderSide.Buy, 100, today.AddHours(-1));

        // Act - buying more is not a day trade
        var wouldBe = _tracker.WouldBeDayTrade(AccountId, "AAPL", OrderSide.Buy, today);

        // Assert
        wouldBe.Should().BeFalse();
    }

    [Fact]
    public void WouldBeDayTrade_OppositeSideDifferentSymbol_ReturnsFalse()
    {
        // Arrange
        var today = DateTimeOffset.UtcNow;
        _tracker.RecordTrade(AccountId, "AAPL", OrderSide.Buy, 100, today.AddHours(-1));

        // Act - selling a different symbol
        var wouldBe = _tracker.WouldBeDayTrade(AccountId, "MSFT", OrderSide.Sell, today);

        // Assert
        wouldBe.Should().BeFalse();
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void ValidateTrade_AccountOver25k_AlwaysAllowed()
    {
        // Arrange
        var today = DateTimeOffset.UtcNow;

        // Create 4 day trades (at PDT limit)
        for (int i = 0; i < 4; i++)
        {
            var date = today.AddDays(-i);
            _tracker.RecordTrade(AccountId, $"SYM{i}", OrderSide.Buy, 100, date.AddHours(-2));
            _tracker.RecordTrade(AccountId, $"SYM{i}", OrderSide.Sell, 100, date.AddHours(-1));
        }

        // Set up a potential day trade
        _tracker.RecordTrade(AccountId, "NEWSTOCK", OrderSide.Buy, 100, today.AddHours(-1));

        // Act - account has $30k, should be allowed
        var result = _tracker.ValidateTrade(
            AccountId,
            "NEWSTOCK",
            OrderSide.Sell,
            accountEquity: 30_000m,
            today);

        // Assert
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void ValidateTrade_AccountUnder25k_AtLimit_Rejected()
    {
        // Arrange - use fixed base time at 3 PM to avoid date boundary issues
        var baseTime = new DateTimeOffset(2024, 6, 15, 15, 0, 0, TimeSpan.Zero);

        // Create 3 day trades (at limit for non-PDT)
        for (int i = 0; i < 3; i++)
        {
            var dayStart = baseTime.AddDays(-i).Date;
            var tradeTime = new DateTimeOffset(dayStart.Year, dayStart.Month, dayStart.Day, 10, 0, 0, TimeSpan.Zero);
            _tracker.RecordTrade(AccountId, $"SYM{i}", OrderSide.Buy, 100, tradeTime);
            _tracker.RecordTrade(AccountId, $"SYM{i}", OrderSide.Sell, 100, tradeTime.AddHours(1));
        }

        // Set up a potential 4th day trade on today
        var todayTradeTime = new DateTimeOffset(2024, 6, 15, 11, 0, 0, TimeSpan.Zero);
        _tracker.RecordTrade(AccountId, "NEWSTOCK", OrderSide.Buy, 100, todayTradeTime);

        // Act - account has only $20k
        var result = _tracker.ValidateTrade(
            AccountId,
            "NEWSTOCK",
            OrderSide.Sell,
            accountEquity: 20_000m,
            baseTime);

        // Assert
        result.IsAllowed.Should().BeFalse();
        result.Message.Should().Contain("limit exceeded");
    }

    [Fact]
    public void ValidateTrade_AccountUnder25k_ApproachingLimit_Warning()
    {
        // Arrange - use fixed base time at 3 PM to avoid date boundary issues
        var baseTime = new DateTimeOffset(2024, 6, 15, 15, 0, 0, TimeSpan.Zero);

        // Create 2 day trades
        for (int i = 0; i < 2; i++)
        {
            var dayStart = baseTime.AddDays(-i).Date;
            var tradeTime = new DateTimeOffset(dayStart.Year, dayStart.Month, dayStart.Day, 10, 0, 0, TimeSpan.Zero);
            _tracker.RecordTrade(AccountId, $"SYM{i}", OrderSide.Buy, 100, tradeTime);
            _tracker.RecordTrade(AccountId, $"SYM{i}", OrderSide.Sell, 100, tradeTime.AddHours(1));
        }

        // Set up a potential 3rd day trade (last allowed) on today
        var todayTradeTime = new DateTimeOffset(2024, 6, 15, 11, 0, 0, TimeSpan.Zero);
        _tracker.RecordTrade(AccountId, "NEWSTOCK", OrderSide.Buy, 100, todayTradeTime);

        // Act
        var result = _tracker.ValidateTrade(
            AccountId,
            "NEWSTOCK",
            OrderSide.Sell,
            accountEquity: 20_000m,
            baseTime);

        // Assert
        result.IsAllowed.Should().BeTrue();
        result.HasWarning.Should().BeTrue();
        result.Message.Should().Contain("last");
    }

    [Fact]
    public void ValidateTrade_NotADayTrade_AlwaysAllowed()
    {
        // Arrange
        var today = DateTimeOffset.UtcNow;

        // Create 4 day trades (at PDT)
        for (int i = 0; i < 4; i++)
        {
            var date = today.AddDays(-i);
            _tracker.RecordTrade(AccountId, $"SYM{i}", OrderSide.Buy, 100, date.AddHours(-2));
            _tracker.RecordTrade(AccountId, $"SYM{i}", OrderSide.Sell, 100, date.AddHours(-1));
        }

        // Act - new buy is not a day trade
        var result = _tracker.ValidateTrade(
            AccountId,
            "NEWSTOCK",
            OrderSide.Buy,
            accountEquity: 20_000m,
            today);

        // Assert
        result.IsAllowed.Should().BeTrue();
        result.HasWarning.Should().BeFalse();
    }

    #endregion

    #region Cleanup Tests

    [Fact]
    public void PurgeOldRecords_RemovesOldTrades()
    {
        // Arrange
        var today = DateTimeOffset.UtcNow;
        var oldDate = today.AddDays(-10);

        _tracker.RecordTrade(AccountId, "OLD", OrderSide.Buy, 100, oldDate);
        _tracker.RecordTrade(AccountId, "NEW", OrderSide.Buy, 100, today);

        // Act
        _tracker.PurgeOldRecords(today);

        // Assert
        var trades = _tracker.GetTrades(AccountId);
        trades.Should().HaveCount(1);
        trades[0].Symbol.Should().Be("NEW");
    }

    [Fact]
    public void Clear_RemovesAllTrades()
    {
        // Arrange
        var today = DateTimeOffset.UtcNow;
        _tracker.RecordTrade(AccountId, "AAPL", OrderSide.Buy, 100, today);
        _tracker.RecordTrade(AccountId, "MSFT", OrderSide.Buy, 50, today);

        // Act
        _tracker.Clear();

        // Assert
        _tracker.GetTrades(AccountId).Should().BeEmpty();
    }

    #endregion
}
