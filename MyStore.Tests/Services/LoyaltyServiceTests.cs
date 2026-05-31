using FluentAssertions;
using Moq;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Services;
using Xunit;

namespace MyStore.Tests.Services;

public class LoyaltyServiceTests
{
    private readonly Mock<ILoyaltyRepository> _repositoryMock;
    private readonly Mock<ICustomerRepository> _customerRepositoryMock;
    private readonly LoyaltyService _service;

    public LoyaltyServiceTests()
    {
        _repositoryMock = new Mock<ILoyaltyRepository>();
        _customerRepositoryMock = new Mock<ICustomerRepository>();
        _service = new LoyaltyService(_repositoryMock.Object, _customerRepositoryMock.Object);
    }

    [Fact]
    public async Task GetSettingsAsync_SettingsExist_ReturnsSuccess()
    {
        var settings = new LoyaltySettings { Id = 1, CompanyId = 1, IsEnabled = true };
        _repositoryMock.Setup(r => r.GetSettingsAsync(1)).ReturnsAsync(settings);

        var result = await _service.GetSettingsAsync(1);

        result.Success.Should().BeTrue();
        result.Data.Should().Be(settings);
    }

    [Fact]
    public async Task GetSettingsAsync_NoSettings_ReturnsDefaultSettings()
    {
        _repositoryMock.Setup(r => r.GetSettingsAsync(1)).ReturnsAsync((LoyaltySettings?)null);

        var result = await _service.GetSettingsAsync(1);

        result.Success.Should().BeTrue();
        result.Data!.IsEnabled.Should().BeFalse();
        result.Data.PointsPerDollarSpent.Should().Be(1m);
        result.Data.RedemptionRate.Should().Be(100m);
    }

    [Fact]
    public async Task UpdateSettingsAsync_ValidSettings_ReturnsUpdated()
    {
        var settings = new LoyaltySettings { PointsPerDollarSpent = 2m, RedemptionRate = 50m, IsEnabled = true };
        var saved = new LoyaltySettings { Id = 1, CompanyId = 1, PointsPerDollarSpent = 2m, RedemptionRate = 50m, IsEnabled = true };
        _repositoryMock.Setup(r => r.UpsertSettingsAsync(It.IsAny<LoyaltySettings>())).ReturnsAsync(saved);

        var result = await _service.UpdateSettingsAsync(settings, 1);

        result.Success.Should().BeTrue();
        result.Data.Should().Be(saved);
    }

    [Fact]
    public async Task UpdateSettingsAsync_DisabledWithZeroRedemptionRate_DefaultsRateToOne()
    {
        var settings = new LoyaltySettings { IsEnabled = false, PointsPerDollarSpent = 0m, PointsPerDollarTradeIn = 0m, RedemptionRate = 0m };
        LoyaltySettings? captured = null;
        _repositoryMock
            .Setup(r => r.UpsertSettingsAsync(It.IsAny<LoyaltySettings>()))
            .Callback<LoyaltySettings>(s => captured = s)
            .ReturnsAsync(settings);

        var result = await _service.UpdateSettingsAsync(settings, 1);

        result.Success.Should().BeTrue();
        captured!.RedemptionRate.Should().Be(1m);
        captured.PointsPerDollarSpent.Should().Be(0m);
        captured.PointsPerDollarTradeIn.Should().Be(0m);
    }

    [Fact]
    public async Task GetBalanceAsync_ReturnsBalance()
    {
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(new Customer { Id = 42, CompanyId = 1 });
        _repositoryMock.Setup(r => r.GetBalanceAsync(1, 42)).ReturnsAsync(150);
        _repositoryMock.Setup(r => r.GetTransactionsAsync(1, 42)).ReturnsAsync(new List<LoyaltyTransaction>());

        var result = await _service.GetBalanceAsync(1, 42);

        result.Success.Should().BeTrue();
        result.Data!.Balance.Should().Be(150);
        result.Data.CustomerId.Should().Be(42);
    }

    [Fact]
    public async Task GetBalanceAsync_CustomerNotFound_ReturnsError()
    {
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(99999)).ReturnsAsync((Customer?)null);

        var result = await _service.GetBalanceAsync(1, 99999);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Customer not found");
        _repositoryMock.Verify(r => r.GetBalanceAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetBalanceAsync_CustomerBelongsToDifferentCompany_ReturnsError()
    {
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(new Customer { Id = 42, CompanyId = 99 });

        var result = await _service.GetBalanceAsync(1, 42);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Customer not found");
        _repositoryMock.Verify(r => r.GetBalanceAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task EarnFromSaleAsync_LoyaltyEnabled_InsertsTransaction()
    {
        var settings = new LoyaltySettings { CompanyId = 1, IsEnabled = true, PointsPerDollarSpent = 1m };
        _repositoryMock.Setup(r => r.GetSettingsAsync(1)).ReturnsAsync(settings);
        _repositoryMock.Setup(r => r.AddTransactionAsync(It.IsAny<LoyaltyTransaction>()))
            .ReturnsAsync(new LoyaltyTransaction());

        await _service.EarnFromSaleAsync(1, 5, 50.75m);

        _repositoryMock.Verify(r => r.AddTransactionAsync(It.Is<LoyaltyTransaction>(t =>
            t.CompanyId == 1 &&
            t.CustomerId == 5 &&
            t.Points == 50 &&
            t.TransactionType == "earn_sale")), Times.Once);
    }

    [Fact]
    public async Task EarnFromSaleAsync_LoyaltyDisabled_NoTransaction()
    {
        var settings = new LoyaltySettings { CompanyId = 1, IsEnabled = false };
        _repositoryMock.Setup(r => r.GetSettingsAsync(1)).ReturnsAsync(settings);

        await _service.EarnFromSaleAsync(1, 5, 100m);

        _repositoryMock.Verify(r => r.AddTransactionAsync(It.IsAny<LoyaltyTransaction>()), Times.Never);
    }

    [Fact]
    public async Task EarnFromSaleAsync_NoSettings_NoTransaction()
    {
        _repositoryMock.Setup(r => r.GetSettingsAsync(1)).ReturnsAsync((LoyaltySettings?)null);

        await _service.EarnFromSaleAsync(1, 5, 100m);

        _repositoryMock.Verify(r => r.AddTransactionAsync(It.IsAny<LoyaltyTransaction>()), Times.Never);
    }

    [Fact]
    public async Task EarnFromSaleAsync_PointsFlooredCorrectly()
    {
        var settings = new LoyaltySettings { CompanyId = 1, IsEnabled = true, PointsPerDollarSpent = 1.5m };
        _repositoryMock.Setup(r => r.GetSettingsAsync(1)).ReturnsAsync(settings);
        _repositoryMock.Setup(r => r.AddTransactionAsync(It.IsAny<LoyaltyTransaction>()))
            .ReturnsAsync(new LoyaltyTransaction());

        await _service.EarnFromSaleAsync(1, 5, 10m);

        _repositoryMock.Verify(r => r.AddTransactionAsync(It.Is<LoyaltyTransaction>(t =>
            t.Points == 15)), Times.Once);
    }

    [Fact]
    public async Task EarnFromTradeInAsync_LoyaltyEnabled_InsertsTransaction()
    {
        var settings = new LoyaltySettings { CompanyId = 1, IsEnabled = true, PointsPerDollarTradeIn = 2m };
        _repositoryMock.Setup(r => r.GetSettingsAsync(1)).ReturnsAsync(settings);
        _repositoryMock.Setup(r => r.AddTransactionAsync(It.IsAny<LoyaltyTransaction>()))
            .ReturnsAsync(new LoyaltyTransaction());

        await _service.EarnFromTradeInAsync(5, 1, 20m);

        _repositoryMock.Verify(r => r.AddTransactionAsync(It.Is<LoyaltyTransaction>(t =>
            t.CompanyId == 1 &&
            t.CustomerId == 5 &&
            t.Points == 40 &&
            t.TransactionType == "earn_tradein")), Times.Once);
    }

    [Fact]
    public async Task EarnFromTradeInAsync_LoyaltyDisabled_NoTransaction()
    {
        var settings = new LoyaltySettings { CompanyId = 1, IsEnabled = false };
        _repositoryMock.Setup(r => r.GetSettingsAsync(1)).ReturnsAsync(settings);

        await _service.EarnFromTradeInAsync(5, 1, 100m);

        _repositoryMock.Verify(r => r.AddTransactionAsync(It.IsAny<LoyaltyTransaction>()), Times.Never);
    }

    [Fact]
    public async Task RedeemAsync_CustomerNotFound_ReturnsError()
    {
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(99999)).ReturnsAsync((Customer?)null);

        var result = await _service.RedeemAsync(1, 99999, 10);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Customer not found");
        _repositoryMock.Verify(r => r.GetSettingsAsync(It.IsAny<int>()), Times.Never);
        _repositoryMock.Verify(r => r.GetBalanceAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _repositoryMock.Verify(r => r.AddTransactionAsync(It.IsAny<LoyaltyTransaction>()), Times.Never);
    }

    [Fact]
    public async Task RedeemAsync_CustomerBelongsToDifferentCompany_ReturnsError()
    {
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new Customer { Id = 5, CompanyId = 99 });

        var result = await _service.RedeemAsync(1, 5, 10);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Customer not found");
        _repositoryMock.Verify(r => r.GetSettingsAsync(It.IsAny<int>()), Times.Never);
        _repositoryMock.Verify(r => r.GetBalanceAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _repositoryMock.Verify(r => r.AddTransactionAsync(It.IsAny<LoyaltyTransaction>()), Times.Never);
    }

    [Fact]
    public async Task RedeemAsync_SufficientBalance_ReturnsCredit()
    {
        var settings = new LoyaltySettings { CompanyId = 1, IsEnabled = true, RedemptionRate = 100m };
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new Customer { Id = 5, CompanyId = 1 });
        _repositoryMock.Setup(r => r.GetSettingsAsync(1)).ReturnsAsync(settings);
        _repositoryMock.Setup(r => r.GetBalanceAsync(1, 5)).ReturnsAsync(500);
        _repositoryMock.Setup(r => r.AddTransactionAsync(It.IsAny<LoyaltyTransaction>()))
            .ReturnsAsync(new LoyaltyTransaction());

        var result = await _service.RedeemAsync(1, 5, 200);

        result.Success.Should().BeTrue();
        result.Data!.PointsRedeemed.Should().Be(200);
        result.Data.CreditAmount.Should().Be(2m);
        result.Data.NewBalance.Should().Be(300);
    }

    [Fact]
    public async Task RedeemAsync_InsufficientBalance_ReturnsError()
    {
        var settings = new LoyaltySettings { CompanyId = 1, IsEnabled = true, RedemptionRate = 100m };
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new Customer { Id = 5, CompanyId = 1 });
        _repositoryMock.Setup(r => r.GetSettingsAsync(1)).ReturnsAsync(settings);
        _repositoryMock.Setup(r => r.GetBalanceAsync(1, 5)).ReturnsAsync(100);

        var result = await _service.RedeemAsync(1, 5, 200);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Insufficient");
        _repositoryMock.Verify(r => r.AddTransactionAsync(It.IsAny<LoyaltyTransaction>()), Times.Never);
    }

    [Fact]
    public async Task RedeemAsync_LoyaltyDisabled_ReturnsError()
    {
        var settings = new LoyaltySettings { CompanyId = 1, IsEnabled = false };
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new Customer { Id = 5, CompanyId = 1 });
        _repositoryMock.Setup(r => r.GetSettingsAsync(1)).ReturnsAsync(settings);

        var result = await _service.RedeemAsync(1, 5, 100);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(r => r.AddTransactionAsync(It.IsAny<LoyaltyTransaction>()), Times.Never);
    }

    [Fact]
    public async Task RedeemAsync_ZeroPoints_ReturnsError()
    {
        var result = await _service.RedeemAsync(1, 5, 0);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(r => r.GetSettingsAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task RedeemAsync_InsertsNegativeTransaction()
    {
        var settings = new LoyaltySettings { CompanyId = 1, IsEnabled = true, RedemptionRate = 100m };
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new Customer { Id = 5, CompanyId = 1 });
        _repositoryMock.Setup(r => r.GetSettingsAsync(1)).ReturnsAsync(settings);
        _repositoryMock.Setup(r => r.GetBalanceAsync(1, 5)).ReturnsAsync(300);
        _repositoryMock.Setup(r => r.AddTransactionAsync(It.IsAny<LoyaltyTransaction>()))
            .ReturnsAsync(new LoyaltyTransaction());

        await _service.RedeemAsync(1, 5, 100);

        _repositoryMock.Verify(r => r.AddTransactionAsync(It.Is<LoyaltyTransaction>(t =>
            t.Points == -100 &&
            t.TransactionType == "redeem")), Times.Once);
    }

    [Fact]
    public async Task EarnFromSaleAsync_DollarNinetyNineSaleAtOnePointPerDollar_YieldsOnePoint()
    {
        var settings = new LoyaltySettings { CompanyId = 1, IsEnabled = true, PointsPerDollarSpent = 1m };
        _repositoryMock.Setup(r => r.GetSettingsAsync(1)).ReturnsAsync(settings);
        _repositoryMock.Setup(r => r.AddTransactionAsync(It.IsAny<LoyaltyTransaction>()))
            .ReturnsAsync(new LoyaltyTransaction());

        await _service.EarnFromSaleAsync(1, 5, 1.99m);

        _repositoryMock.Verify(r => r.AddTransactionAsync(It.Is<LoyaltyTransaction>(t =>
            t.Points == 1)), Times.Once);
    }

    [Fact]
    public async Task EarnFromTradeInAsync_FloorRounding_YieldsCorrectPoints()
    {
        var settings = new LoyaltySettings { CompanyId = 1, IsEnabled = true, PointsPerDollarTradeIn = 1m };
        _repositoryMock.Setup(r => r.GetSettingsAsync(1)).ReturnsAsync(settings);
        _repositoryMock.Setup(r => r.AddTransactionAsync(It.IsAny<LoyaltyTransaction>()))
            .ReturnsAsync(new LoyaltyTransaction());

        await _service.EarnFromTradeInAsync(5, 1, 1.99m);

        _repositoryMock.Verify(r => r.AddTransactionAsync(It.Is<LoyaltyTransaction>(t =>
            t.Points == 1)), Times.Once);
    }

    [Fact]
    public async Task RedeemAsync_ZeroBalance_ReturnsErrorNotException()
    {
        var settings = new LoyaltySettings { CompanyId = 1, IsEnabled = true, RedemptionRate = 100m };
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new Customer { Id = 5, CompanyId = 1 });
        _repositoryMock.Setup(r => r.GetSettingsAsync(1)).ReturnsAsync(settings);
        _repositoryMock.Setup(r => r.GetBalanceAsync(1, 5)).ReturnsAsync(0);

        var result = await _service.RedeemAsync(1, 5, 1);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Insufficient");
        _repositoryMock.Verify(r => r.AddTransactionAsync(It.IsAny<LoyaltyTransaction>()), Times.Never);
    }

    [Fact]
    public async Task GetBalanceAsync_ReturnsTransactionsList()
    {
        var transactions = new List<LoyaltyTransaction>
        {
            new LoyaltyTransaction { Id = 1, Points = 100, TransactionType = "earn_sale" },
            new LoyaltyTransaction { Id = 2, Points = 50, TransactionType = "earn_tradein" },
            new LoyaltyTransaction { Id = 3, Points = -30, TransactionType = "redeem" },
        };
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(new Customer { Id = 42, CompanyId = 1 });
        _repositoryMock.Setup(r => r.GetBalanceAsync(1, 42)).ReturnsAsync(120);
        _repositoryMock.Setup(r => r.GetTransactionsAsync(1, 42)).ReturnsAsync(transactions);

        var result = await _service.GetBalanceAsync(1, 42);

        result.Success.Should().BeTrue();
        result.Data!.Balance.Should().Be(120);
        result.Data.Transactions.Should().HaveCount(3);
        result.Data.Transactions.Sum(t => t.Points).Should().Be(120);
    }

    [Fact]
    public async Task RedeemAsync_ExactBalance_ReturnsSuccessAndZeroNewBalance()
    {
        var settings = new LoyaltySettings { CompanyId = 1, IsEnabled = true, RedemptionRate = 100m };
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new Customer { Id = 5, CompanyId = 1 });
        _repositoryMock.Setup(r => r.GetSettingsAsync(1)).ReturnsAsync(settings);
        _repositoryMock.Setup(r => r.GetBalanceAsync(1, 5)).ReturnsAsync(100);
        _repositoryMock.Setup(r => r.AddTransactionAsync(It.IsAny<LoyaltyTransaction>()))
            .ReturnsAsync(new LoyaltyTransaction());

        var result = await _service.RedeemAsync(1, 5, 100);

        result.Success.Should().BeTrue();
        result.Data!.NewBalance.Should().Be(0);
        result.Data.CreditAmount.Should().Be(1m);
    }
}
