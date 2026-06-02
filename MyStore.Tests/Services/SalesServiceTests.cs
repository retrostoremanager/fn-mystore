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
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
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
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
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
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
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
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
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
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
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
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
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

    [Theory]
    [InlineData(64, "REC-64")]
    [InlineData(1, "REC-1")]
    [InlineData(1042, "REC-1042")]
    [InlineData(123456, "REC-123456")]
    [InlineData(1234567, "REC-1234567")]
    public async Task GetReceiptAsync_ReturnsReceiptNumberWithRecPrefix(int saleId, string expectedReceiptNumber)
    {
        var sale = CreateSaleWithStoredTotals(id: saleId);

        _salesRepositoryMock.Setup(r => r.GetByIdAsync(saleId, CompanyId)).ReturnsAsync(sale);
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<int>(), CompanyId)).ReturnsAsync((InventoryItem?)null);
        _companyRepositoryMock.Setup(r => r.GetProfileAsync(CompanyId)).ReturnsAsync(new CompanyProfile { CompanyName = "Test Store" });

        var result = await _service.GetReceiptAsync(saleId, CompanyId);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.ReceiptNumber.Should().Be(expectedReceiptNumber);
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
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
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
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
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
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });

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
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
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
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
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
        _customerRepositoryMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new Customer { Id = 10, CompanyId = CompanyId });
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
        _customerRepositoryMock.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }
}
