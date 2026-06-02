using FluentAssertions;
using Moq;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Services;
using Xunit;

namespace MyStore.Tests.Services;

public class ConsignmentServiceTests
{
    private readonly Mock<IConsignmentRepository> _repositoryMock;
    private readonly ConsignmentService _service;

    public ConsignmentServiceTests()
    {
        _repositoryMock = new Mock<IConsignmentRepository>();
        _service = new ConsignmentService(_repositoryMock.Object);
    }

    [Fact]
    public async Task GetByIdAsync_ItemNotFound_ReturnsError()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync((ConsignmentItem?)null);

        var result = await _service.GetByIdAsync(1, 1);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task GetByIdAsync_ItemFound_ReturnsSuccess()
    {
        var item = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "pending" };
        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(item);

        var result = await _service.GetByIdAsync(1, 1);

        result.Success.Should().BeTrue();
        result.Data.Should().Be(item);
    }

    [Fact]
    public async Task GetByIdAsync_WrongCompany_ReturnsError()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(1, 99)).ReturnsAsync((ConsignmentItem?)null);

        var result = await _service.GetByIdAsync(1, 99);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task MarkSoldAsync_ActiveItem_CalculatesPayoutCorrectly()
    {
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "pending", SplitPercent = 60m };
        var updated = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "sold", SplitPercent = 60m, SalePrice = 100m };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.MarkSoldAsync(1, 100m, 1)).ReturnsAsync(updated);

        var result = await _service.MarkSoldAsync(1, 100m, 1);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("60.00");
        result.Message.Should().Contain("40.00");
        result.Data.Should().NotBeNull();
        result.Data.PayoutAmount.Should().Be(60m);
        result.Data.StoreAmount.Should().Be(40m);
        result.Data.Item.Should().NotBeNull();
        result.Data.Item.Should().Be(updated);
    }

    [Fact]
    public async Task MarkSoldAsync_NonActiveItem_ReturnsError()
    {
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "sold", SplitPercent = 60m };
        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(existing);

        var result = await _service.MarkSoldAsync(1, 100m, 1);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("sold");
        _repositoryMock.Verify(r => r.MarkSoldAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task MarkSoldAsync_ReturnedItem_ReturnsError()
    {
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "returned", SplitPercent = 60m };
        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(existing);

        var result = await _service.MarkSoldAsync(1, 100m, 1);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("returned");
        _repositoryMock.Verify(r => r.MarkSoldAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task MarkSoldAsync_ItemNotFound_ReturnsError()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync((ConsignmentItem?)null);

        var result = await _service.MarkSoldAsync(1, 100m, 1);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Theory]
    [InlineData(100, 60, 60, 40)]
    [InlineData(200, 70, 140, 60)]
    [InlineData(50, 50, 25, 25)]
    [InlineData(100, 70, 70, 30)]
    [InlineData(100, 100, 100, 0)]
    [InlineData(100, 0, 0, 100)]
    public async Task MarkSoldAsync_PayoutCalculation_IsCorrect(
        decimal salePrice, decimal splitPercent, decimal expectedPayout, decimal expectedStore)
    {
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "pending", SplitPercent = splitPercent };
        var updated = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "sold", SplitPercent = splitPercent, SalePrice = salePrice };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.MarkSoldAsync(1, salePrice, 1)).ReturnsAsync(updated);

        var result = await _service.MarkSoldAsync(1, salePrice, 1);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain(expectedPayout.ToString("F2"));
        result.Message.Should().Contain(expectedStore.ToString("F2"));
        result.Data.Should().NotBeNull();
        result.Data.PayoutAmount.Should().Be(expectedPayout);
        result.Data.StoreAmount.Should().Be(expectedStore);
    }

    [Fact]
    public async Task MarkSoldAsync_FractionalResult_RoundsToTwoDecimalPlaces()
    {
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "pending", SplitPercent = 33.33m };
        var updated = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "sold", SplitPercent = 33.33m, SalePrice = 99.99m };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.MarkSoldAsync(1, 99.99m, 1)).ReturnsAsync(updated);

        var result = await _service.MarkSoldAsync(1, 99.99m, 1);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.PayoutAmount.Should().Be(33.33m);
        result.Data.StoreAmount.Should().Be(66.66m);
    }

    [Fact]
    public async Task ProcessPayoutAsync_FractionalResult_RoundsToTwoDecimalPlaces()
    {
        var item = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "sold", SplitPercent = 33.33m, SalePrice = 99.99m };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(item);
        _repositoryMock.Setup(r => r.GetPayoutsAsync(1, 1)).ReturnsAsync(new List<ConsignmentPayout>());
        _repositoryMock
            .Setup(r => r.CreatePayoutAsync(It.IsAny<ConsignmentPayout>()))
            .ReturnsAsync((ConsignmentPayout p) => p);

        var result = await _service.ProcessPayoutAsync(1, null, 1);

        result.Success.Should().BeTrue();
        _repositoryMock.Verify(r => r.CreatePayoutAsync(It.Is<ConsignmentPayout>(p => p.Amount == 33.33m)), Times.Once);
    }

    [Fact]
    public async Task ProcessPayoutAsync_SoldItemNoPriorPayout_CreatesPayoutWithCorrectAmount()
    {
        var item = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "sold", SplitPercent = 60m, SalePrice = 100m };
        var payout = new ConsignmentPayout { Id = 1, ConsignmentItemId = 1, Amount = 60m };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(item);
        _repositoryMock.Setup(r => r.GetPayoutsAsync(1, 1)).ReturnsAsync(new List<ConsignmentPayout>());
        _repositoryMock.Setup(r => r.CreatePayoutAsync(It.IsAny<ConsignmentPayout>())).ReturnsAsync(payout);

        var result = await _service.ProcessPayoutAsync(1, null, 1);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Payout processed");
        _repositoryMock.Verify(r => r.CreatePayoutAsync(It.Is<ConsignmentPayout>(p => p.Amount == 60m)), Times.Once);
    }

    [Fact]
    public async Task ProcessPayoutAsync_AlreadyPaid_ReturnsError()
    {
        var item = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "sold", SplitPercent = 60m, SalePrice = 100m };
        var existingPayout = new ConsignmentPayout { Id = 5, ConsignmentItemId = 1, Amount = 60m };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(item);
        _repositoryMock.Setup(r => r.GetPayoutsAsync(1, 1)).ReturnsAsync(new List<ConsignmentPayout> { existingPayout });

        var result = await _service.ProcessPayoutAsync(1, null, 1);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("already been processed");
        _repositoryMock.Verify(r => r.CreatePayoutAsync(It.IsAny<ConsignmentPayout>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPayoutAsync_NonSoldItem_ReturnsError()
    {
        var item = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "pending", SplitPercent = 60m };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(item);

        var result = await _service.ProcessPayoutAsync(1, null, 1);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Only sold items");
        _repositoryMock.Verify(r => r.CreatePayoutAsync(It.IsAny<ConsignmentPayout>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPayoutAsync_ItemNotFound_ReturnsError()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync((ConsignmentItem?)null);

        var result = await _service.ProcessPayoutAsync(1, null, 1);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task ProcessPayoutAsync_NoSalePrice_ReturnsError()
    {
        var item = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "sold", SplitPercent = 60m, SalePrice = null };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(item);
        _repositoryMock.Setup(r => r.GetPayoutsAsync(1, 1)).ReturnsAsync(new List<ConsignmentPayout>());

        var result = await _service.ProcessPayoutAsync(1, null, 1);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("no recorded sale price");
    }

    [Fact]
    public async Task ReturnToCustomerAsync_ActiveItem_ReturnsSuccess()
    {
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "pending" };
        var updated = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "returned" };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.UpdateAsync(It.Is<ConsignmentItem>(i => i.Status == "returned"))).ReturnsAsync(updated);

        var result = await _service.ReturnToCustomerAsync(1, 1);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("returned to customer");
    }

    [Fact]
    public async Task ReturnToCustomerAsync_NonActiveItem_ReturnsError()
    {
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "sold" };
        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(existing);

        var result = await _service.ReturnToCustomerAsync(1, 1);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Only pending items");
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<ConsignmentItem>()), Times.Never);
    }

    [Fact]
    public async Task ReturnToCustomerAsync_ItemNotFound_ReturnsError()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync((ConsignmentItem?)null);

        var result = await _service.ReturnToCustomerAsync(1, 1);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task ReturnToCustomerAsync_SetsStatusToReturned()
    {
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "pending" };
        ConsignmentItem? captured = null;

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(existing);
        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<ConsignmentItem>()))
            .Callback<ConsignmentItem>(i => captured = i)
            .ReturnsAsync((ConsignmentItem i) => i);

        await _service.ReturnToCustomerAsync(1, 1);

        captured.Should().NotBeNull();
        captured!.Status.Should().Be("returned");
    }
}
