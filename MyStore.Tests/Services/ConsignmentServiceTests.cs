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
    private readonly Mock<ISalesRepository> _salesRepositoryMock;
    private readonly Mock<IInventoryRepository> _inventoryRepositoryMock;
    private readonly ConsignmentService _service;

    public ConsignmentServiceTests()
    {
        _repositoryMock = new Mock<IConsignmentRepository>();
        _salesRepositoryMock = new Mock<ISalesRepository>();
        _inventoryRepositoryMock = new Mock<IInventoryRepository>();
        _salesRepositoryMock
            .Setup(s => s.CreateAsync(It.IsAny<Sale>()))
            .ReturnsAsync((Sale s) => { s.Id = 999; return s; });
        _inventoryRepositoryMock
            .Setup(i => i.UpdateQuantityAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(true);
        _service = new ConsignmentService(
            _repositoryMock.Object,
            _salesRepositoryMock.Object,
            _inventoryRepositoryMock.Object);
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
        var item = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "active" };
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
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "active", SplitPercent = 60m };
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
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "active", SplitPercent = splitPercent };
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
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "active", SplitPercent = 33.33m };
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
        var item = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "active", SplitPercent = 60m };

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
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "active" };
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
        result.Message.Should().Contain("Only active items");
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
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "active" };
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

    [Fact]
    public async Task MarkSoldAsync_CreatesSalesRecordWithCorrectTotals()
    {
        var existing = new ConsignmentItem { Id = 7, CompanyId = 1, CustomerId = 42, Status = "active", SplitPercent = 60m, Description = "Game" };
        var updated = new ConsignmentItem { Id = 7, CompanyId = 1, CustomerId = 42, Status = "sold", SplitPercent = 60m, SalePrice = 100m, Description = "Game" };

        _repositoryMock.Setup(r => r.GetByIdAsync(7, 1)).ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.MarkSoldAsync(7, 100m, 1)).ReturnsAsync(updated);

        Sale? capturedSale = null;
        _salesRepositoryMock
            .Setup(s => s.CreateAsync(It.IsAny<Sale>()))
            .Callback<Sale>(s => capturedSale = s)
            .ReturnsAsync((Sale s) => { s.Id = 999; return s; });

        var result = await _service.MarkSoldAsync(7, 100m, 1);

        result.Success.Should().BeTrue();
        capturedSale.Should().NotBeNull();
        capturedSale!.CompanyId.Should().Be(1);
        capturedSale.CustomerId.Should().Be(42);
        capturedSale.Subtotal.Should().Be(100m);
        capturedSale.Total.Should().Be(100m);
        capturedSale.PaymentMethod.Should().Be("consignment");
        _salesRepositoryMock.Verify(s => s.CreateAsync(It.IsAny<Sale>()), Times.Once);
    }

    [Fact]
    public async Task MarkSoldAsync_PayoutAmountIsCorrect()
    {
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, CustomerId = 5, Status = "active", SplitPercent = 70m };
        var updated = new ConsignmentItem { Id = 1, CompanyId = 1, CustomerId = 5, Status = "sold", SplitPercent = 70m, SalePrice = 200m };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.MarkSoldAsync(1, 200m, 1)).ReturnsAsync(updated);

        var result = await _service.MarkSoldAsync(1, 200m, 1);

        result.Success.Should().BeTrue();
        result.Data!.PayoutAmount.Should().Be(140m);
        result.Data.StoreAmount.Should().Be(60m);
    }

    [Fact]
    public async Task MarkSoldAsync_LinkedInventoryItem_DecrementsQuantity()
    {
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, CustomerId = 5, Status = "active", SplitPercent = 50m, InventoryItemId = 88 };
        var updated = new ConsignmentItem { Id = 1, CompanyId = 1, CustomerId = 5, Status = "sold", SplitPercent = 50m, SalePrice = 50m, InventoryItemId = 88 };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.MarkSoldAsync(1, 50m, 1)).ReturnsAsync(updated);

        var result = await _service.MarkSoldAsync(1, 50m, 1);

        result.Success.Should().BeTrue();
        _inventoryRepositoryMock.Verify(i => i.UpdateQuantityAsync(88, -1, 1), Times.Once);
    }

    [Fact]
    public async Task MarkSoldAsync_NoInventoryLink_DoesNotCallInventoryRepo()
    {
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, CustomerId = 5, Status = "active", SplitPercent = 50m, InventoryItemId = null };
        var updated = new ConsignmentItem { Id = 1, CompanyId = 1, CustomerId = 5, Status = "sold", SplitPercent = 50m, SalePrice = 50m, InventoryItemId = null };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.MarkSoldAsync(1, 50m, 1)).ReturnsAsync(updated);

        var result = await _service.MarkSoldAsync(1, 50m, 1);

        result.Success.Should().BeTrue();
        _inventoryRepositoryMock.Verify(
            i => i.UpdateQuantityAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task MarkSoldAsync_NoInventoryLink_StillCreatesSalesRecord()
    {
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, CustomerId = 5, Status = "active", SplitPercent = 50m, InventoryItemId = null };
        var updated = new ConsignmentItem { Id = 1, CompanyId = 1, CustomerId = 5, Status = "sold", SplitPercent = 50m, SalePrice = 50m, InventoryItemId = null };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.MarkSoldAsync(1, 50m, 1)).ReturnsAsync(updated);

        var result = await _service.MarkSoldAsync(1, 50m, 1);

        result.Success.Should().BeTrue();
        _salesRepositoryMock.Verify(s => s.CreateAsync(It.Is<Sale>(sale => sale.Items.Count == 0)), Times.Once);
    }

    [Fact]
    public async Task MarkSoldAsync_AlreadySold_DoesNotCreateDuplicateSale()
    {
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, Status = "sold", SplitPercent = 60m };
        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(existing);

        var result = await _service.MarkSoldAsync(1, 100m, 1);

        result.Success.Should().BeFalse();
        _salesRepositoryMock.Verify(s => s.CreateAsync(It.IsAny<Sale>()), Times.Never);
    }

    [Fact]
    public async Task MarkSoldAsync_TaxEnabled_AppliesTaxToSale()
    {
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, CustomerId = 5, Status = "active", SplitPercent = 60m };
        var updated = new ConsignmentItem { Id = 1, CompanyId = 1, CustomerId = 5, Status = "sold", SplitPercent = 60m, SalePrice = 100m };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.MarkSoldAsync(1, 100m, 1)).ReturnsAsync(updated);

        var companyRepoMock = new Mock<ICompanyRepository>();
        companyRepoMock
            .Setup(c => c.GetTaxSettingsAsync(1))
            .ReturnsAsync(new TaxSettingsResponse { TaxEnabled = true, TaxRate = 0.10m, TaxLabel = "Sales Tax" });

        Sale? capturedSale = null;
        var salesRepoMock = new Mock<ISalesRepository>();
        salesRepoMock
            .Setup(s => s.CreateAsync(It.IsAny<Sale>()))
            .Callback<Sale>(s => capturedSale = s)
            .ReturnsAsync((Sale s) => { s.Id = 555; return s; });

        var service = new ConsignmentService(
            _repositoryMock.Object,
            salesRepoMock.Object,
            _inventoryRepositoryMock.Object,
            companyRepoMock.Object);

        var result = await service.MarkSoldAsync(1, 100m, 1);

        result.Success.Should().BeTrue();
        capturedSale.Should().NotBeNull();
        capturedSale!.Subtotal.Should().Be(100m);
        capturedSale.Tax.Should().Be(10m);
        capturedSale.TaxAmount.Should().Be(10m);
        capturedSale.TaxRate.Should().Be(0.10m);
        capturedSale.TaxLabel.Should().Be("Sales Tax");
        capturedSale.Total.Should().Be(110m);
    }

    [Fact]
    public async Task MarkSoldAsync_TaxDisabled_NoTaxOnSale()
    {
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, CustomerId = 5, Status = "active", SplitPercent = 60m };
        var updated = new ConsignmentItem { Id = 1, CompanyId = 1, CustomerId = 5, Status = "sold", SplitPercent = 60m, SalePrice = 100m };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.MarkSoldAsync(1, 100m, 1)).ReturnsAsync(updated);

        var companyRepoMock = new Mock<ICompanyRepository>();
        companyRepoMock
            .Setup(c => c.GetTaxSettingsAsync(1))
            .ReturnsAsync(new TaxSettingsResponse { TaxEnabled = false, TaxRate = 0.10m });

        Sale? capturedSale = null;
        var salesRepoMock = new Mock<ISalesRepository>();
        salesRepoMock
            .Setup(s => s.CreateAsync(It.IsAny<Sale>()))
            .Callback<Sale>(s => capturedSale = s)
            .ReturnsAsync((Sale s) => { s.Id = 555; return s; });

        var service = new ConsignmentService(
            _repositoryMock.Object,
            salesRepoMock.Object,
            _inventoryRepositoryMock.Object,
            companyRepoMock.Object);

        var result = await service.MarkSoldAsync(1, 100m, 1);

        result.Success.Should().BeTrue();
        capturedSale.Should().NotBeNull();
        capturedSale!.Tax.Should().Be(0m);
        capturedSale.Total.Should().Be(100m);
    }

    [Fact]
    public async Task MarkSoldAsync_CustomerLinked_CallsLoyaltyEarnFromSale()
    {
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, CustomerId = 42, Status = "active", SplitPercent = 60m };
        var updated = new ConsignmentItem { Id = 1, CompanyId = 1, CustomerId = 42, Status = "sold", SplitPercent = 60m, SalePrice = 100m };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.MarkSoldAsync(1, 100m, 1)).ReturnsAsync(updated);

        var loyaltyMock = new Mock<ILoyaltyService>();
        loyaltyMock
            .Setup(l => l.EarnFromSaleAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);

        var salesRepoMock = new Mock<ISalesRepository>();
        salesRepoMock
            .Setup(s => s.CreateAsync(It.IsAny<Sale>()))
            .ReturnsAsync((Sale s) => { s.Id = 777; return s; });

        var service = new ConsignmentService(
            _repositoryMock.Object,
            salesRepoMock.Object,
            _inventoryRepositoryMock.Object,
            companyRepository: null,
            loyaltyService: loyaltyMock.Object);

        var result = await service.MarkSoldAsync(1, 100m, 1);

        result.Success.Should().BeTrue();
        loyaltyMock.Verify(l => l.EarnFromSaleAsync(42, 1, 100m, 777), Times.Once);
    }

    [Fact]
    public async Task MarkSoldAsync_NoCustomer_DoesNotCallLoyalty()
    {
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, CustomerId = 0, Status = "active", SplitPercent = 60m };
        var updated = new ConsignmentItem { Id = 1, CompanyId = 1, CustomerId = 0, Status = "sold", SplitPercent = 60m, SalePrice = 100m };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.MarkSoldAsync(1, 100m, 1)).ReturnsAsync(updated);

        var loyaltyMock = new Mock<ILoyaltyService>();
        var service = new ConsignmentService(
            _repositoryMock.Object,
            _salesRepositoryMock.Object,
            _inventoryRepositoryMock.Object,
            companyRepository: null,
            loyaltyService: loyaltyMock.Object);

        var result = await service.MarkSoldAsync(1, 100m, 1);

        result.Success.Should().BeTrue();
        loyaltyMock.Verify(
            l => l.EarnFromSaleAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<int?>()),
            Times.Never);
    }

    [Fact]
    public async Task MarkSoldAsync_WithUserEmail_LooksUpEmployeeAndRecordsOnSale()
    {
        var existing = new ConsignmentItem { Id = 1, CompanyId = 1, CustomerId = 5, Status = "active", SplitPercent = 50m };
        var updated = new ConsignmentItem { Id = 1, CompanyId = 1, CustomerId = 5, Status = "sold", SplitPercent = 50m, SalePrice = 80m };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, 1)).ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.MarkSoldAsync(1, 80m, 1)).ReturnsAsync(updated);

        var userRepoMock = new Mock<IUserRepository>();
        userRepoMock
            .Setup(u => u.GetByEmailAsync("clerk@store.com", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = 33, Email = "clerk@store.com", CompanyId = 1 });

        Sale? capturedSale = null;
        var salesRepoMock = new Mock<ISalesRepository>();
        salesRepoMock
            .Setup(s => s.CreateAsync(It.IsAny<Sale>()))
            .Callback<Sale>(s => capturedSale = s)
            .ReturnsAsync((Sale s) => { s.Id = 888; return s; });

        var service = new ConsignmentService(
            _repositoryMock.Object,
            salesRepoMock.Object,
            _inventoryRepositoryMock.Object,
            companyRepository: null,
            loyaltyService: null,
            userRepository: userRepoMock.Object);

        var result = await service.MarkSoldAsync(1, 80m, 1, "clerk@store.com");

        result.Success.Should().BeTrue();
        capturedSale.Should().NotBeNull();
        capturedSale!.UserId.Should().Be(33);
    }

    [Fact]
    public async Task MarkSoldAsync_QaScenario_ActiveItemTriggersSaleAndInventoryDecrement()
    {
        var existing = new ConsignmentItem
        {
            Id = 9,
            CompanyId = 13,
            CustomerId = 53,
            Status = "active",
            SplitPercent = 60m,
            Description = "PR188 QA",
            InventoryItemId = 70,
        };
        var updated = new ConsignmentItem
        {
            Id = 9,
            CompanyId = 13,
            CustomerId = 53,
            Status = "sold",
            SplitPercent = 60m,
            SalePrice = 50m,
            Description = "PR188 QA",
            InventoryItemId = 70,
        };

        _repositoryMock.Setup(r => r.GetByIdAsync(9, 13)).ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.MarkSoldAsync(9, 50m, 13)).ReturnsAsync(updated);

        var result = await _service.MarkSoldAsync(9, 50m, 13);

        result.Success.Should().BeTrue();
        result.Message.Should().NotContain("Only active items");
        _salesRepositoryMock.Verify(
            s => s.CreateAsync(It.Is<Sale>(sale =>
                sale.CompanyId == 13
                && sale.CustomerId == 53
                && sale.PaymentMethod == "consignment"
                && sale.Total == 50m
                && sale.Items.Count == 1
                && sale.Items[0].InventoryItemId == 70
                && sale.Items[0].Quantity == 1)),
            Times.Once);
        _inventoryRepositoryMock.Verify(i => i.UpdateQuantityAsync(70, -1, 13), Times.Once);
    }
}
