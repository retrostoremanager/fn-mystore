using FluentAssertions;
using Moq;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Services;
using Xunit;

namespace MyStore.Tests.Services;

public class ReceiptServiceTests
{
    private readonly Mock<ISalesRepository> _salesRepositoryMock = new();
    private readonly Mock<ICompanyRepository> _companyRepositoryMock = new();
    private readonly Mock<IInventoryRepository> _inventoryRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IEmailService> _emailServiceMock = new();
    private readonly ReceiptService _service;

    private const int CompanyId = 1;

    public ReceiptServiceTests()
    {
        _service = new ReceiptService(
            _salesRepositoryMock.Object,
            _companyRepositoryMock.Object,
            _inventoryRepositoryMock.Object,
            _userRepositoryMock.Object,
            _emailServiceMock.Object);
    }

    private static Sale BuildSale(int id = 42, decimal subtotal = 100m, decimal taxRate = 0m, decimal taxAmount = 0m, string? taxLabel = null)
    {
        var total = subtotal + taxAmount;
        return new Sale
        {
            Id = id,
            CompanyId = CompanyId,
            CustomerId = 5,
            UserId = null,
            Subtotal = subtotal,
            TaxRate = taxRate,
            TaxAmount = taxAmount,
            TaxLabel = taxLabel,
            Tax = taxAmount,
            Total = total,
            PaymentMethod = "Cash",
            SaleDate = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            Items = new List<SaleItem>
            {
                new SaleItem { Id = 1, SaleId = id, InventoryItemId = 10, Quantity = 2, UnitPrice = 50m, TotalPrice = 100m }
            }
        };
    }

    private static CompanyProfile BuildProfile() => new CompanyProfile
    {
        Id = CompanyId,
        CompanyName = "Retro Games",
        CompanyAddress = "123 Main St",
        CompanyCity = "Springfield",
        CompanyState = "IL",
        CompanyZipCode = "62701",
        CompanyPhone = "555-1234"
    };

    private void SetupDefaultMocks(Sale sale)
    {
        _salesRepositoryMock.Setup(r => r.GetByIdAsync(sale.Id, CompanyId)).ReturnsAsync(sale);
        _companyRepositoryMock.Setup(r => r.GetProfileAsync(CompanyId)).ReturnsAsync(BuildProfile());
        _inventoryRepositoryMock.Setup(r => r.GetByIdAsync(10, CompanyId)).ReturnsAsync(
            new InventoryItem { Id = 10, CompanyId = CompanyId, Name = "Mega Drive Console" });
    }

    [Fact]
    public async Task GetReceiptAsync_ZeroTax_ReturnsReceiptWithZeroTaxFields()
    {
        var sale = BuildSale(subtotal: 100m, taxRate: 0m, taxAmount: 0m, taxLabel: null);
        SetupDefaultMocks(sale);

        var result = await _service.GetReceiptAsync(sale.Id, CompanyId);

        result.Success.Should().BeTrue();
        var receipt = result.Data!;
        receipt.ReceiptNumber.Should().Be("REC-42");
        receipt.TaxRate.Should().Be(0m);
        receipt.TaxAmount.Should().Be(0m);
        receipt.Subtotal.Should().Be(100m);
        receipt.Total.Should().Be(100m);
        receipt.TaxLabel.Should().BeNull();
        receipt.Items.Should().HaveCount(1);
        receipt.Items[0].Name.Should().Be("Mega Drive Console");
        receipt.Items[0].Qty.Should().Be(2);
        receipt.Items[0].UnitPrice.Should().Be(50m);
        receipt.Items[0].LineTotal.Should().Be(100m);
    }

    [Fact]
    public async Task GetReceiptAsync_StandardTaxRate_ReturnsCorrectTaxAmount()
    {
        var subtotal = 100m;
        var taxRate = 0.085m;
        var taxAmount = Math.Round(subtotal * taxRate, 2, MidpointRounding.AwayFromZero);
        var sale = BuildSale(subtotal: subtotal, taxRate: taxRate, taxAmount: taxAmount, taxLabel: "Sales Tax");
        SetupDefaultMocks(sale);

        var result = await _service.GetReceiptAsync(sale.Id, CompanyId);

        result.Success.Should().BeTrue();
        var receipt = result.Data!;
        receipt.TaxRate.Should().Be(0.085m);
        receipt.TaxAmount.Should().Be(8.50m);
        receipt.Total.Should().Be(108.50m);
        receipt.TaxLabel.Should().Be("Sales Tax");
    }

    [Fact]
    public async Task GetReceiptAsync_RoundingEdgeCase_ReturnsRoundedTaxAmount()
    {
        var subtotal = 99.99m;
        var taxRate = 0.07m;
        var taxAmount = Math.Round(subtotal * taxRate, 2, MidpointRounding.AwayFromZero);
        var sale = BuildSale(subtotal: subtotal, taxRate: taxRate, taxAmount: taxAmount, taxLabel: "GST");
        SetupDefaultMocks(sale);

        var result = await _service.GetReceiptAsync(sale.Id, CompanyId);

        result.Success.Should().BeTrue();
        var receipt = result.Data!;
        receipt.TaxRate.Should().Be(0.07m);
        receipt.TaxAmount.Should().Be(7.00m);
        receipt.Subtotal.Should().Be(99.99m);
        receipt.Total.Should().Be(subtotal + taxAmount);
    }

    [Fact]
    public async Task GetReceiptAsync_SaleNotFound_ReturnsErrorResponse()
    {
        _salesRepositoryMock.Setup(r => r.GetByIdAsync(999, CompanyId)).ReturnsAsync((Sale?)null);

        var result = await _service.GetReceiptAsync(999, CompanyId);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task GetReceiptAsync_WithEmployee_IncludesEmployeeName()
    {
        var sale = BuildSale();
        sale.UserId = 7;
        SetupDefaultMocks(sale);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(7, CompanyId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new User { Id = 7, CompanyId = CompanyId, FirstName = "Jane", LastName = "Doe" });

        var result = await _service.GetReceiptAsync(sale.Id, CompanyId);

        result.Success.Should().BeTrue();
        result.Data!.EmployeeName.Should().Be("Jane Doe");
    }

    [Fact]
    public async Task GetReceiptAsync_StoreInfoPopulated()
    {
        var sale = BuildSale();
        SetupDefaultMocks(sale);

        var result = await _service.GetReceiptAsync(sale.Id, CompanyId);

        result.Success.Should().BeTrue();
        var receipt = result.Data!;
        receipt.StoreName.Should().Be("Retro Games");
        receipt.StorePhone.Should().Be("555-1234");
        receipt.StoreAddress.Should().Contain("123 Main St");
    }

    [Fact]
    public async Task GetReceiptAsync_ReceiptNumberHasRecPrefix()
    {
        var sale = BuildSale(id: 5);
        SetupDefaultMocks(sale);
        _salesRepositoryMock.Setup(r => r.GetByIdAsync(5, CompanyId)).ReturnsAsync(sale);

        var result = await _service.GetReceiptAsync(5, CompanyId);

        result.Success.Should().BeTrue();
        result.Data!.ReceiptNumber.Should().Be("REC-5");
    }

    [Fact]
    public async Task SendReceiptEmailAsync_SaleNotFound_ReturnsError()
    {
        _salesRepositoryMock.Setup(r => r.GetByIdAsync(999, CompanyId)).ReturnsAsync((Sale?)null);

        var result = await _service.SendReceiptEmailAsync(999, CompanyId, "test@example.com");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
        _emailServiceMock.Verify(e => e.SendReceiptEmailAsync(It.IsAny<string>(), It.IsAny<ReceiptResponse>()), Times.Never);
    }

    [Fact]
    public async Task SendReceiptEmailAsync_HappyPath_CallsEmailServiceAndReturnsSuccess()
    {
        var sale = BuildSale();
        SetupDefaultMocks(sale);
        _emailServiceMock.Setup(e => e.SendReceiptEmailAsync("test@example.com", It.IsAny<ReceiptResponse>()))
            .ReturnsAsync(new EmailSendResult { Success = true });

        var result = await _service.SendReceiptEmailAsync(sale.Id, CompanyId, "test@example.com");

        result.Success.Should().BeTrue();
        _emailServiceMock.Verify(e => e.SendReceiptEmailAsync("test@example.com", It.IsAny<ReceiptResponse>()), Times.Once);
    }

    [Fact]
    public async Task SendReceiptEmailAsync_EmailServiceFails_ReturnsError()
    {
        var sale = BuildSale();
        SetupDefaultMocks(sale);
        _emailServiceMock.Setup(e => e.SendReceiptEmailAsync(It.IsAny<string>(), It.IsAny<ReceiptResponse>()))
            .ReturnsAsync(new EmailSendResult { Success = false, ErrorMessage = "SMTP error" });

        var result = await _service.SendReceiptEmailAsync(sale.Id, CompanyId, "test@example.com");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("SMTP error");
    }
}
