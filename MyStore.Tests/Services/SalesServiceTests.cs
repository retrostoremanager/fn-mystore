using FluentAssertions;
using Moq;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Services;
using Xunit;

namespace MyStore.Tests.Services;

public class SalesServiceTests
{
    private readonly Mock<ISalesRepository> _salesRepositoryMock;
    private readonly Mock<ICustomerRepository> _customerRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IInventoryRepository> _inventoryRepositoryMock;
    private readonly Mock<ICompanyRepository> _companyRepositoryMock;
    private readonly SalesService _service;

    private const int CompanyId = 1;

    public SalesServiceTests()
    {
        _salesRepositoryMock = new Mock<ISalesRepository>();
        _customerRepositoryMock = new Mock<ICustomerRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _inventoryRepositoryMock = new Mock<IInventoryRepository>();
        _companyRepositoryMock = new Mock<ICompanyRepository>();

        _companyRepositoryMock
            .Setup(r => r.GetTaxSettingsAsync(It.IsAny<int>()))
            .ReturnsAsync(new TaxSettingsResponse { TaxEnabled = false, TaxRate = 0m, TaxLabel = "Sales Tax" });

        _service = new SalesService(
            _salesRepositoryMock.Object,
            _customerRepositoryMock.Object,
            _userRepositoryMock.Object,
            _inventoryRepositoryMock.Object,
            _companyRepositoryMock.Object);
    }

    private static Sale CreateSaleWithStoredTotals(int id = 1, decimal subtotal = 80m, decimal tax = 8m, decimal total = 88m)
    {
        return new Sale
        {
            Id = id,
            CompanyId = CompanyId,
            CustomerId = 10,
            Subtotal = subtotal,
            Tax = tax,
            Total = total,
            PaymentMethod = "Cash",
            SaleDate = DateTime.UtcNow,
            Items = new List<SaleItem>
            {
                new SaleItem { Id = 1, SaleId = id, InventoryItemId = 100, Quantity = 2, UnitPrice = 25m, TotalPrice = 50m },
                new SaleItem { Id = 2, SaleId = id, InventoryItemId = 101, Quantity = 1, UnitPrice = 30m, TotalPrice = 30m }
            }
        };
    }

    [Fact]
    public async Task GetAllSalesAsync_ReturnsStoredSubtotalTaxTotal_NotComputedFromItems()
    {
        var storedSubtotal = 80m;
        var storedTax = 8m;
        var storedTotal = 88m;
        var sale = CreateSaleWithStoredTotals(subtotal: storedSubtotal, tax: storedTax, total: storedTotal);

        _salesRepositoryMock.Setup(r => r.GetAllAsync(CompanyId)).ReturnsAsync(new List<Sale> { sale });
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<int>(), CompanyId)).ReturnsAsync((InventoryItem?)null);

        var result = await _service.GetAllSalesAsync(CompanyId);

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        var returnedSale = result.Data![0];
        returnedSale.Subtotal.Should().Be(storedSubtotal);
        returnedSale.Tax.Should().Be(storedTax);
        returnedSale.Total.Should().Be(storedTotal);
    }

    [Fact]
    public async Task GetAllSalesAsync_SaleItemTotalPriceReflectsStoredValue()
    {
        var sale = CreateSaleWithStoredTotals();

        _salesRepositoryMock.Setup(r => r.GetAllAsync(CompanyId)).ReturnsAsync(new List<Sale> { sale });
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<int>(), CompanyId)).ReturnsAsync((InventoryItem?)null);

        var result = await _service.GetAllSalesAsync(CompanyId);

        result.Success.Should().BeTrue();
        var items = result.Data![0].Items;
        items.Should().HaveCount(2);
        items[0].TotalPrice.Should().Be(50m);
        items[1].TotalPrice.Should().Be(30m);
    }

    [Fact]
    public async Task GetSaleByIdAsync_ReturnsStoredSubtotalTaxTotal_NotComputedFromItems()
    {
        var storedSubtotal = 80m;
        var storedTax = 8m;
        var storedTotal = 88m;
        var sale = CreateSaleWithStoredTotals(subtotal: storedSubtotal, tax: storedTax, total: storedTotal);

        _salesRepositoryMock.Setup(r => r.GetByIdAsync(1, CompanyId)).ReturnsAsync(sale);
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<int>(), CompanyId)).ReturnsAsync((InventoryItem?)null);

        var result = await _service.GetSaleByIdAsync(1, CompanyId);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Subtotal.Should().Be(storedSubtotal);
        result.Data.Tax.Should().Be(storedTax);
        result.Data.Total.Should().Be(storedTotal);
    }

    [Fact]
    public async Task GetSaleByIdAsync_IncludesLineItemsWithTotalPrice()
    {
        var sale = CreateSaleWithStoredTotals();

        _salesRepositoryMock.Setup(r => r.GetByIdAsync(1, CompanyId)).ReturnsAsync(sale);
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<int>(), CompanyId)).ReturnsAsync((InventoryItem?)null);

        var result = await _service.GetSaleByIdAsync(1, CompanyId);

        result.Success.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(2);
        result.Data.Items.Should().AllSatisfy(item => item.TotalPrice.Should().BeGreaterThan(0));
    }

    [Fact]
    public async Task GetSaleByIdAsync_NotFound_ReturnsErrorResponse()
    {
        _salesRepositoryMock.Setup(r => r.GetByIdAsync(999, CompanyId)).ReturnsAsync((Sale?)null);

        var result = await _service.GetSaleByIdAsync(999, CompanyId);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task GetSalesByCustomerIdAsync_Returns200WithCustomerSalesAndStoredTotals()
    {
        var sale = CreateSaleWithStoredTotals(subtotal: 100m, tax: 10m, total: 110m);

        _salesRepositoryMock.Setup(r => r.GetByCustomerIdAsync(10, CompanyId)).ReturnsAsync(new List<Sale> { sale });
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<int>(), CompanyId)).ReturnsAsync((InventoryItem?)null);

        var result = await _service.GetSalesByCustomerIdAsync(10, CompanyId);

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data![0].Total.Should().Be(110m);
    }

    [Fact]
    public async Task GetSalesByDateRangeAsync_ReturnsFilteredSalesWithStoredTotals()
    {
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 1, 31);
        var sale = CreateSaleWithStoredTotals(subtotal: 50m, tax: 5m, total: 55m);
        sale.SaleDate = new DateTime(2024, 1, 15);

        _salesRepositoryMock.Setup(r => r.GetByDateRangeAsync(startDate, endDate, CompanyId)).ReturnsAsync(new List<Sale> { sale });
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<int>(), CompanyId)).ReturnsAsync((InventoryItem?)null);

        var result = await _service.GetSalesByDateRangeAsync(startDate, endDate, CompanyId);

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data![0].Subtotal.Should().Be(50m);
        result.Data[0].Tax.Should().Be(5m);
        result.Data[0].Total.Should().Be(55m);
    }

    [Fact]
    public async Task GetAllSalesAsync_EmptyList_ReturnsSuccessWithEmptyData()
    {
        _salesRepositoryMock.Setup(r => r.GetAllAsync(CompanyId)).ReturnsAsync(new List<Sale>());

        var result = await _service.GetAllSalesAsync(CompanyId);

        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSaleByIdAsync_TaxEnabled_PopulatesTaxRateAndTaxLabelFromCompanySettings()
    {
        var sale = CreateSaleWithStoredTotals();
        sale.TaxAmount = 7m;

        _companyRepositoryMock
            .Setup(r => r.GetTaxSettingsAsync(CompanyId))
            .ReturnsAsync(new TaxSettingsResponse { TaxEnabled = true, TaxRate = 0.0875m, TaxLabel = "GST" });
        _salesRepositoryMock.Setup(r => r.GetByIdAsync(1, CompanyId)).ReturnsAsync(sale);
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<int>(), CompanyId)).ReturnsAsync((InventoryItem?)null);

        var result = await _service.GetSaleByIdAsync(1, CompanyId);

        result.Success.Should().BeTrue();
        result.Data!.TaxRate.Should().Be(0.0875m);
        result.Data.TaxLabel.Should().Be("GST");
        result.Data.TaxAmount.Should().Be(7m);
    }

    [Fact]
    public async Task GetAllSalesAsync_TaxDisabled_ReturnsZeroTaxRateAndNullLabel()
    {
        var sale = CreateSaleWithStoredTotals();

        _companyRepositoryMock
            .Setup(r => r.GetTaxSettingsAsync(CompanyId))
            .ReturnsAsync(new TaxSettingsResponse { TaxEnabled = false, TaxRate = 0m, TaxLabel = "Sales Tax" });
        _salesRepositoryMock.Setup(r => r.GetAllAsync(CompanyId)).ReturnsAsync(new List<Sale> { sale });
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<int>(), CompanyId)).ReturnsAsync((InventoryItem?)null);

        var result = await _service.GetAllSalesAsync(CompanyId);

        result.Success.Should().BeTrue();
        result.Data![0].TaxRate.Should().Be(0m);
        result.Data[0].TaxLabel.Should().BeNull();
    }

    [Fact]
    public async Task GetSaleByIdAsync_LegacySaleWithoutTaxAmount_DoesNotThrow()
    {
        var sale = new Sale
        {
            Id = 1,
            CompanyId = CompanyId,
            CustomerId = 10,
            Subtotal = 0m,
            Tax = 0m,
            TaxAmount = 0m,
            Total = 50m,
            PaymentMethod = "Cash",
            SaleDate = DateTime.UtcNow,
            Items = new List<SaleItem>()
        };

        _salesRepositoryMock.Setup(r => r.GetByIdAsync(1, CompanyId)).ReturnsAsync(sale);
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });

        var result = await _service.GetSaleByIdAsync(1, CompanyId);

        result.Success.Should().BeTrue();
        result.Data!.TaxAmount.Should().Be(0m);
        result.Data.TaxRate.Should().Be(0m);
        result.Data.TaxLabel.Should().BeNull();
        result.Data.Total.Should().Be(50m);
    }

    [Fact]
    public async Task CreateSaleAsync_TaxEnabled_ComputesSubtotalTaxAmountAndTotal()
    {
        var request = new CreateSaleRequest
        {
            CustomerId = 10,
            PaymentMethod = "Cash",
            Items = new List<CreateSaleItemRequest>
            {
                new CreateSaleItemRequest { InventoryItemId = 100, Quantity = 2, UnitPrice = 50m }
            }
        };

        _companyRepositoryMock
            .Setup(r => r.GetTaxSettingsAsync(CompanyId))
            .ReturnsAsync(new TaxSettingsResponse { TaxEnabled = true, TaxRate = 0.0875m, TaxLabel = "Sales Tax" });
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(100, CompanyId))
            .ReturnsAsync(new InventoryItem { Id = 100, CompanyId = CompanyId, Name = "Game", Quantity = 5 });
        _salesRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<Sale>())).ReturnsAsync((Sale s) =>
        {
            s.Id = 1;
            return s;
        });

        var result = await _service.CreateSaleAsync(request, CompanyId);

        result.Success.Should().BeTrue();
        result.Data!.Subtotal.Should().Be(100m);
        result.Data.TaxAmount.Should().Be(8.75m);
        result.Data.TaxRate.Should().Be(0.0875m);
        result.Data.TaxLabel.Should().Be("Sales Tax");
        result.Data.Total.Should().Be(108.75m);
    }

    [Fact]
    public async Task CreateSaleAsync_TaxDisabled_TaxAmountZeroAndSubtotalEqualsTotal()
    {
        var request = new CreateSaleRequest
        {
            CustomerId = 10,
            PaymentMethod = "Cash",
            Items = new List<CreateSaleItemRequest>
            {
                new CreateSaleItemRequest { InventoryItemId = 100, Quantity = 1, UnitPrice = 25m }
            }
        };

        _companyRepositoryMock
            .Setup(r => r.GetTaxSettingsAsync(CompanyId))
            .ReturnsAsync(new TaxSettingsResponse { TaxEnabled = false, TaxRate = 0m, TaxLabel = "Sales Tax" });
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(100, CompanyId))
            .ReturnsAsync(new InventoryItem { Id = 100, CompanyId = CompanyId, Name = "Game", Quantity = 5 });
        _salesRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<Sale>())).ReturnsAsync((Sale s) =>
        {
            s.Id = 1;
            return s;
        });

        var result = await _service.CreateSaleAsync(request, CompanyId);

        result.Success.Should().BeTrue();
        result.Data!.Subtotal.Should().Be(25m);
        result.Data.TaxAmount.Should().Be(0m);
        result.Data.TaxRate.Should().Be(0m);
        result.Data.TaxLabel.Should().BeNull();
        result.Data.Total.Should().Be(25m);
    }

    [Fact]
    public async Task CreateSaleAsync_TaxEnabled_RoundingEdgeCase_RoundsAwayFromZero()
    {
        var request = new CreateSaleRequest
        {
            CustomerId = 10,
            PaymentMethod = "Cash",
            Items = new List<CreateSaleItemRequest>
            {
                new CreateSaleItemRequest { InventoryItemId = 100, Quantity = 1, UnitPrice = 1.005m }
            }
        };

        _companyRepositoryMock
            .Setup(r => r.GetTaxSettingsAsync(CompanyId))
            .ReturnsAsync(new TaxSettingsResponse { TaxEnabled = true, TaxRate = 0.08m, TaxLabel = "Sales Tax" });
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(100, CompanyId))
            .ReturnsAsync(new InventoryItem { Id = 100, CompanyId = CompanyId, Name = "Game", Quantity = 5 });
        _salesRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<Sale>())).ReturnsAsync((Sale s) =>
        {
            s.Id = 1;
            return s;
        });

        var result = await _service.CreateSaleAsync(request, CompanyId);

        result.Success.Should().BeTrue();
        result.Data!.TaxAmount.Should().Be(0.08m);
        result.Data.TaxAmount.Should().NotBe(0.07m);
    }

    [Fact]
    public async Task CreateSaleAsync_NullCustomerId_ReturnsErrorWithoutCallingRepository()
    {
        var request = new CreateSaleRequest
        {
            CustomerId = 0,
            PaymentMethod = "Cash",
            Items = new List<CreateSaleItemRequest>
            {
                new CreateSaleItemRequest { InventoryItemId = 100, Quantity = 1, UnitPrice = 24.99m }
            }
        };

        var result = await _service.CreateSaleAsync(request, CompanyId);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("customerId is required");
        _salesRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<Sale>()), Times.Never);
        _customerRepositoryMock.Verify(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CreateSaleAsync_LoyaltyServiceProvided_EarnFromSaleAsyncCalledWithSaleTotal()
    {
        var request = new CreateSaleRequest
        {
            CustomerId = 10,
            PaymentMethod = "Cash",
            Items = new List<CreateSaleItemRequest>
            {
                new CreateSaleItemRequest { InventoryItemId = 100, Quantity = 2, UnitPrice = 50m }
            }
        };

        var loyaltyServiceMock = new Mock<ILoyaltyService>();
        loyaltyServiceMock
            .Setup(l => l.EarnFromSaleAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);

        var serviceWithLoyalty = new SalesService(
            _salesRepositoryMock.Object,
            _customerRepositoryMock.Object,
            _userRepositoryMock.Object,
            _inventoryRepositoryMock.Object,
            _companyRepositoryMock.Object,
            loyaltyServiceMock.Object);

        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(100, CompanyId))
            .ReturnsAsync(new InventoryItem { Id = 100, CompanyId = CompanyId, Name = "Game", Quantity = 5 });
        _salesRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<Sale>())).ReturnsAsync((Sale s) =>
        {
            s.Id = 42;
            return s;
        });

        var result = await serviceWithLoyalty.CreateSaleAsync(request, CompanyId);

        result.Success.Should().BeTrue();
        loyaltyServiceMock.Verify(
            l => l.EarnFromSaleAsync(10, CompanyId, 100m, 42),
            Times.Once);
    }

    [Fact]
    public async Task CreateSaleAsync_NoActivePromotions_StoresFullPriceWithoutDiscount()
    {
        var request = new CreateSaleRequest
        {
            CustomerId = 10,
            PaymentMethod = "Cash",
            Items = new List<CreateSaleItemRequest>
            {
                new CreateSaleItemRequest { InventoryItemId = 100, Quantity = 2, UnitPrice = 25m }
            }
        };

        var promotionServiceMock = new Mock<IPromotionService>();
        promotionServiceMock
            .Setup(p => p.ApplyPromotionsAsync(It.IsAny<IEnumerable<CartItem>>(), CompanyId))
            .ReturnsAsync(new List<LineDiscount>());

        var service = new SalesService(
            _salesRepositoryMock.Object,
            _customerRepositoryMock.Object,
            _userRepositoryMock.Object,
            _inventoryRepositoryMock.Object,
            _companyRepositoryMock.Object,
            loyaltyService: null,
            promotionService: promotionServiceMock.Object);

        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(100, CompanyId))
            .ReturnsAsync(new InventoryItem { Id = 100, CompanyId = CompanyId, Name = "Game", Quantity = 5 });
        _salesRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<Sale>())).ReturnsAsync((Sale s) => { s.Id = 1; return s; });

        var result = await service.CreateSaleAsync(request, CompanyId);

        result.Success.Should().BeTrue();
        result.Data!.Subtotal.Should().Be(50m);
        result.Data.Total.Should().Be(50m);
        result.Data.DiscountTotal.Should().Be(0m);
        result.Data.AppliedPromotions.Should().BeEmpty();
        result.Data.Items[0].TotalPrice.Should().Be(50m);
        result.Data.Items[0].DiscountAmount.Should().Be(0m);
        result.Data.Items[0].PromotionId.Should().BeNull();
        promotionServiceMock.Verify(p => p.ApplyPromotionsAsync(It.IsAny<IEnumerable<CartItem>>(), CompanyId), Times.Once);
    }

    [Fact]
    public async Task CreateSaleAsync_NullPromotionService_ProceedsWithoutDiscount()
    {
        var request = new CreateSaleRequest
        {
            CustomerId = 10,
            PaymentMethod = "Cash",
            Items = new List<CreateSaleItemRequest>
            {
                new CreateSaleItemRequest { InventoryItemId = 100, Quantity = 1, UnitPrice = 40m }
            }
        };

        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(100, CompanyId))
            .ReturnsAsync(new InventoryItem { Id = 100, CompanyId = CompanyId, Name = "Game", Quantity = 5 });
        _salesRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<Sale>())).ReturnsAsync((Sale s) => { s.Id = 1; return s; });

        var result = await _service.CreateSaleAsync(request, CompanyId);

        result.Success.Should().BeTrue();
        result.Data!.Subtotal.Should().Be(40m);
        result.Data.DiscountTotal.Should().Be(0m);
        result.Data.AppliedPromotions.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateSaleAsync_PercentagePromotion_AppliesDiscountAndPersistsPromotionMetadata()
    {
        var request = new CreateSaleRequest
        {
            CustomerId = 10,
            PaymentMethod = "Cash",
            Items = new List<CreateSaleItemRequest>
            {
                new CreateSaleItemRequest { InventoryItemId = 100, Quantity = 2, UnitPrice = 50m }
            }
        };

        var promotionServiceMock = new Mock<IPromotionService>();
        promotionServiceMock
            .Setup(p => p.ApplyPromotionsAsync(It.IsAny<IEnumerable<CartItem>>(), CompanyId))
            .ReturnsAsync(new List<LineDiscount>
            {
                new LineDiscount { ItemId = 100, DiscountAmount = 10m, PromotionId = 7, PromotionName = "10% off" }
            });

        var service = new SalesService(
            _salesRepositoryMock.Object,
            _customerRepositoryMock.Object,
            _userRepositoryMock.Object,
            _inventoryRepositoryMock.Object,
            _companyRepositoryMock.Object,
            loyaltyService: null,
            promotionService: promotionServiceMock.Object);

        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(100, CompanyId))
            .ReturnsAsync(new InventoryItem { Id = 100, CompanyId = CompanyId, Name = "Game", Quantity = 5 });
        _salesRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<Sale>())).ReturnsAsync((Sale s) => { s.Id = 1; return s; });

        var result = await service.CreateSaleAsync(request, CompanyId);

        result.Success.Should().BeTrue();
        result.Data!.Subtotal.Should().Be(90m);
        result.Data.Total.Should().Be(90m);
        result.Data.DiscountTotal.Should().Be(10m);
        result.Data.Items[0].TotalPrice.Should().Be(90m);
        result.Data.Items[0].DiscountAmount.Should().Be(10m);
        result.Data.Items[0].PromotionId.Should().Be(7);
        result.Data.Items[0].PromotionName.Should().Be("10% off");
        result.Data.AppliedPromotions.Should().ContainSingle();
        result.Data.AppliedPromotions[0].PromotionId.Should().Be(7);
        result.Data.AppliedPromotions[0].PromotionName.Should().Be("10% off");
        result.Data.AppliedPromotions[0].DiscountAmount.Should().Be(10m);
    }

    [Fact]
    public async Task CreateSaleAsync_BxgyPromotion_PersistsLineDiscountAndPromotion()
    {
        var request = new CreateSaleRequest
        {
            CustomerId = 10,
            PaymentMethod = "Cash",
            Items = new List<CreateSaleItemRequest>
            {
                new CreateSaleItemRequest { InventoryItemId = 100, Quantity = 3, UnitPrice = 20m }
            }
        };

        var promotionServiceMock = new Mock<IPromotionService>();
        promotionServiceMock
            .Setup(p => p.ApplyPromotionsAsync(It.IsAny<IEnumerable<CartItem>>(), CompanyId))
            .ReturnsAsync(new List<LineDiscount>
            {
                new LineDiscount { ItemId = 100, DiscountAmount = 20m, PromotionId = 11, PromotionName = "B2G1" }
            });

        var service = new SalesService(
            _salesRepositoryMock.Object,
            _customerRepositoryMock.Object,
            _userRepositoryMock.Object,
            _inventoryRepositoryMock.Object,
            _companyRepositoryMock.Object,
            loyaltyService: null,
            promotionService: promotionServiceMock.Object);

        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(100, CompanyId))
            .ReturnsAsync(new InventoryItem { Id = 100, CompanyId = CompanyId, Name = "Game", Quantity = 5 });
        _salesRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<Sale>())).ReturnsAsync((Sale s) => { s.Id = 1; return s; });

        Sale? captured = null;
        _salesRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<Sale>())).ReturnsAsync((Sale s) =>
        {
            captured = s;
            s.Id = 1;
            return s;
        });

        var result = await service.CreateSaleAsync(request, CompanyId);

        result.Success.Should().BeTrue();
        result.Data!.Subtotal.Should().Be(40m);
        result.Data.DiscountTotal.Should().Be(20m);
        result.Data.Items[0].DiscountAmount.Should().Be(20m);
        result.Data.Items[0].TotalPrice.Should().Be(40m);
        result.Data.Items[0].PromotionId.Should().Be(11);
        result.Data.Items[0].PromotionName.Should().Be("B2G1");
        result.Data.AppliedPromotions.Should().ContainSingle();
        result.Data.AppliedPromotions[0].PromotionId.Should().Be(11);
        captured.Should().NotBeNull();
        captured!.Items[0].DiscountAmount.Should().Be(20m);
        captured.Items[0].PromotionId.Should().Be(11);
    }

    [Fact]
    public async Task CreateSaleAsync_StackingPromotionsExceedLineTotal_AppliedPromotionsSumEqualsDiscountTotal()
    {
        var request = new CreateSaleRequest
        {
            CustomerId = 10,
            PaymentMethod = "Cash",
            Items = new List<CreateSaleItemRequest>
            {
                new CreateSaleItemRequest { InventoryItemId = 70, Quantity = 2, UnitPrice = 50m }
            }
        };

        var promotionServiceMock = new Mock<IPromotionService>();
        promotionServiceMock
            .Setup(p => p.ApplyPromotionsAsync(It.IsAny<IEnumerable<CartItem>>(), CompanyId))
            .ReturnsAsync(new List<LineDiscount>
            {
                new LineDiscount { ItemId = 70, DiscountAmount = 62m, PromotionId = 21, PromotionName = "Store-wide 62% off" },
                new LineDiscount { ItemId = 70, DiscountAmount = 100m, PromotionId = 29, PromotionName = "QA Bad BXGY" }
            });

        var service = new SalesService(
            _salesRepositoryMock.Object,
            _customerRepositoryMock.Object,
            _userRepositoryMock.Object,
            _inventoryRepositoryMock.Object,
            _companyRepositoryMock.Object,
            loyaltyService: null,
            promotionService: promotionServiceMock.Object);

        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(70, CompanyId))
            .ReturnsAsync(new InventoryItem { Id = 70, CompanyId = CompanyId, Name = "Game", Quantity = 5 });
        _salesRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<Sale>())).ReturnsAsync((Sale s) => { s.Id = 80; return s; });

        var result = await service.CreateSaleAsync(request, CompanyId);

        result.Success.Should().BeTrue();
        result.Data!.Subtotal.Should().Be(0m);
        result.Data.DiscountTotal.Should().Be(100m);
        result.Data.Items[0].DiscountAmount.Should().Be(100m);
        result.Data.Items[0].TotalPrice.Should().Be(0m);
        result.Data.AppliedPromotions.Should().HaveCount(2);
        result.Data.AppliedPromotions.Sum(p => p.DiscountAmount).Should().Be(result.Data.DiscountTotal);
        result.Data.AppliedPromotions.Sum(p => p.DiscountAmount).Should().Be(result.Data.Items.Sum(i => i.DiscountAmount));
    }

    [Fact]
    public async Task CreateSaleAsync_LoyaltyServiceThrows_SaleCompletionIsNotRolledBack()
    {
        var request = new CreateSaleRequest
        {
            CustomerId = 10,
            PaymentMethod = "Cash",
            Items = new List<CreateSaleItemRequest>
            {
                new CreateSaleItemRequest { InventoryItemId = 100, Quantity = 1, UnitPrice = 30m }
            }
        };

        var loyaltyServiceMock = new Mock<ILoyaltyService>();
        loyaltyServiceMock
            .Setup(l => l.EarnFromSaleAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<int?>()))
            .ThrowsAsync(new Exception("loyalty backend offline"));

        var serviceWithLoyalty = new SalesService(
            _salesRepositoryMock.Object,
            _customerRepositoryMock.Object,
            _userRepositoryMock.Object,
            _inventoryRepositoryMock.Object,
            _companyRepositoryMock.Object,
            loyaltyServiceMock.Object);

        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(100, CompanyId))
            .ReturnsAsync(new InventoryItem { Id = 100, CompanyId = CompanyId, Name = "Game", Quantity = 5 });
        _salesRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<Sale>())).ReturnsAsync((Sale s) =>
        {
            s.Id = 77;
            return s;
        });

        var result = await serviceWithLoyalty.CreateSaleAsync(request, CompanyId);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(77);
        loyaltyServiceMock.Verify(
            l => l.EarnFromSaleAsync(10, CompanyId, 30m, 77),
            Times.Once);
    }
}
