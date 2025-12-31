using AlpacaMock.Domain.Accounts;
using AlpacaMock.Domain.Tests.Fixtures;
using FluentAssertions;

namespace AlpacaMock.Domain.Tests.Accounts;

public class AccountTests
{
    #region RecalculateValues Tests

    [Fact]
    public void RecalculateValues_WithLongPositions_UpdatesEquity()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount(cash: 50_000m);

        // Act - add $25k in long positions
        account.RecalculateValues(
            totalLongValue: 25_000m,
            totalShortValue: 0m,
            totalUnrealizedPnL: 5_000m);

        // Assert
        account.LongMarketValue.Should().Be(25_000m);
        account.Equity.Should().Be(75_000m); // 50k cash + 25k positions
        account.PortfolioValue.Should().Be(75_000m);
    }

    [Fact]
    public void RecalculateValues_WithShortPositions_SubtractsFromEquity()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount(cash: 100_000m);

        // Act - short positions valued at -$20k
        account.RecalculateValues(
            totalLongValue: 0m,
            totalShortValue: -20_000m,
            totalUnrealizedPnL: 0m);

        // Assert
        account.ShortMarketValue.Should().Be(-20_000m);
        account.Equity.Should().Be(80_000m); // 100k - 20k short
    }

    [Fact]
    public void RecalculateValues_MixedPositions_CalculatesCorrectEquity()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount(cash: 50_000m);

        // Act - $30k long, $10k short
        account.RecalculateValues(
            totalLongValue: 30_000m,
            totalShortValue: -10_000m,
            totalUnrealizedPnL: 0m);

        // Assert
        account.Equity.Should().Be(70_000m); // 50k + 30k - 10k
    }

    #endregion

    #region Day Trading Buying Power Tests

    [Fact]
    public void CalculateDayTradingBuyingPower_PdtAccount_Returns4xMarginExcess()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount(cash: 50_000m, pdt: true);
        account.Equity = 50_000m;
        account.MaintenanceMargin = 10_000m;

        // Act
        var dtbp = account.CalculateDayTradingBuyingPower();

        // Assert - 4x (equity - maintenance margin) = 4 * 40k = 160k
        dtbp.Should().Be(160_000m);
    }

    [Fact]
    public void CalculateDayTradingBuyingPower_NonPdtAccount_ReturnsRegularBuyingPower()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount(cash: 50_000m, pdt: false);
        account.BuyingPower = 50_000m;

        // Act
        var dtbp = account.CalculateDayTradingBuyingPower();

        // Assert - non-PDT gets regular buying power
        dtbp.Should().Be(50_000m);
    }

    [Fact]
    public void CalculateDayTradingBuyingPower_NegativeExcess_ReturnsZero()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount(cash: 10_000m, pdt: true);
        account.Equity = 10_000m;
        account.MaintenanceMargin = 15_000m; // Exceeds equity

        // Act
        var dtbp = account.CalculateDayTradingBuyingPower();

        // Assert - should not go negative
        dtbp.Should().Be(0m);
    }

    #endregion

    #region Buying Power Validation Tests

    [Fact]
    public void ValidateBuyingPower_SufficientFunds_ReturnsValid()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount(cash: 100_000m);

        // Act
        var result = account.ValidateBuyingPower(orderCost: 50_000m);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void ValidateBuyingPower_InsufficientFunds_ReturnsInvalid()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount(cash: 10_000m);

        // Act
        var result = account.ValidateBuyingPower(orderCost: 50_000m);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("Insufficient");
    }

    [Fact]
    public void ValidateBuyingPower_DayTrade_UsesDayTradingBuyingPower()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount(cash: 50_000m, pdt: true);
        account.Equity = 50_000m;
        account.MaintenanceMargin = 10_000m;
        // DTBP = 4 * 40k = 160k

        // Act - order larger than cash but within DTBP
        var result = account.ValidateBuyingPower(orderCost: 100_000m, isDayTrade: true);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Short Selling Requirement Tests

    [Fact]
    public void CalculateShortSellingRequirement_UsesMaxOfLimitAndAskPlus3Percent()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount();
        decimal limitPrice = 100m;
        decimal currentAsk = 105m;

        // Act
        var requirement = account.CalculateShortSellingRequirement(
            qty: 100,
            limitPrice: limitPrice,
            currentAsk: currentAsk);

        // Assert - 3% above ask = 108.15, which is > limit of 100
        // So requirement = 100 * 108.15 = 10815
        requirement.Should().Be(10815m);
    }

    [Fact]
    public void CalculateShortSellingRequirement_LimitHigherThanAsk_UsesLimit()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount();
        decimal limitPrice = 120m;
        decimal currentAsk = 100m;

        // Act
        var requirement = account.CalculateShortSellingRequirement(
            qty: 100,
            limitPrice: limitPrice,
            currentAsk: currentAsk);

        // Assert - limit 120 > 103 (ask + 3%), so uses 120
        requirement.Should().Be(12000m);
    }

    [Fact]
    public void CalculateShortSellingRequirement_NullLimit_UsesAskPlus3Percent()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount();
        decimal currentAsk = 100m;

        // Act
        var requirement = account.CalculateShortSellingRequirement(
            qty: 100,
            limitPrice: null,
            currentAsk: currentAsk);

        // Assert - 100 * 103 = 10300
        requirement.Should().Be(10300m);
    }

    #endregion

    #region PDT Minimum Tests

    [Fact]
    public void MeetsPdtMinimum_EquityAbove25k_ReturnsTrue()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount(cash: 30_000m);
        account.Equity = 30_000m;

        // Act & Assert
        account.MeetsPdtMinimum().Should().BeTrue();
    }

    [Fact]
    public void MeetsPdtMinimum_EquityBelow25k_ReturnsFalse()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount(cash: 20_000m);
        account.Equity = 20_000m;

        // Act & Assert
        account.MeetsPdtMinimum().Should().BeFalse();
    }

    [Fact]
    public void MeetsPdtMinimum_EquityExactly25k_ReturnsTrue()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount(cash: 25_000m);
        account.Equity = 25_000m;

        // Act & Assert
        account.MeetsPdtMinimum().Should().BeTrue();
    }

    #endregion

    #region Cash Management Tests

    [Fact]
    public void DeductCash_SufficientFunds_DeductsAndReturnsTrue()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount(cash: 100_000m);

        // Act
        var result = account.DeductCash(50_000m);

        // Assert
        result.Should().BeTrue();
        account.Cash.Should().Be(50_000m);
        account.BuyingPower.Should().Be(50_000m);
    }

    [Fact]
    public void DeductCash_InsufficientFunds_ReturnsFalseAndNoChange()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount(cash: 10_000m);

        // Act
        var result = account.DeductCash(50_000m);

        // Assert
        result.Should().BeFalse();
        account.Cash.Should().Be(10_000m); // Unchanged
    }

    [Fact]
    public void AddCash_AddsFundsAndUpdatesBuyingPower()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount(cash: 50_000m);

        // Act
        account.AddCash(25_000m);

        // Assert
        account.Cash.Should().Be(75_000m);
        account.BuyingPower.Should().Be(75_000m);
    }

    #endregion

    #region Cash Withdrawable Tests

    [Fact]
    public void RecalculateValues_UpdatesCashWithdrawable_ConsideringMargin()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount(cash: 100_000m);
        account.InitialMargin = 20_000m;

        // Act
        account.RecalculateValues(0, 0, 0);

        // Assert
        account.CashWithdrawable.Should().Be(80_000m); // 100k - 20k margin
    }

    [Fact]
    public void RecalculateValues_CashWithdrawable_NeverNegative()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount(cash: 10_000m);
        account.InitialMargin = 20_000m; // More than cash

        // Act
        account.RecalculateValues(0, 0, 0);

        // Assert
        account.CashWithdrawable.Should().Be(0m);
    }

    #endregion

    #region Account Number Generation Tests

    [Fact]
    public void AccountNumber_GeneratedAutomatically()
    {
        // Arrange
        var account = TestDataBuilder.CreateAccount();

        // Assert
        account.AccountNumber.Should().StartWith("A");
        account.AccountNumber.Should().HaveLength(9); // A + 8 digits
    }

    [Fact]
    public void AccountNumber_UniquePerAccount()
    {
        // Arrange
        var account1 = new Account { Id = "1", SessionId = "s" };
        var account2 = new Account { Id = "2", SessionId = "s" };

        // Assert - should be different (probabilistically)
        account1.AccountNumber.Should().NotBe(account2.AccountNumber);
    }

    #endregion
}
