using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MyStore.Functions;
using MyStore.Models;
using MyStore.Services;
using MyStore.Tests.Helpers;
using Xunit;

namespace MyStore.Tests.Functions;

public class ReceiptFunctionsTests
{
    private readonly Mock<IReceiptService> _receiptServiceMock = new();
    private readonly Mock<ILoggerFactory> _loggerFactoryMock = new();
    private readonly Mock<ILogger<ReceiptFunctions>> _loggerMock = new();
    private readonly ReceiptFunctions _functions;

    private const int CompanyId = 1;

    public ReceiptFunctionsTests()
    {
        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        _functions = new ReceiptFunctions(_receiptServiceMock.Object, _loggerFactoryMock.Object);
    }

    private static ReceiptResponse BuildReceiptResponse(int saleId = 1042)
    {
        return new ReceiptResponse
        {
            ReceiptNumber = $"REC-{saleId}",
            Date = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            StoreName = "Retro Games",
            StoreAddress = "123 Main St",
            StorePhone = "555-1234",
            Items = new List<ReceiptLineItem>
            {
                new ReceiptLineItem { Name = "Mega Drive", Qty = 1, UnitPrice = 100m, LineTotal = 100m }
            },
            Subtotal = 100m,
            TaxLabel = "Sales Tax",
            TaxRate = 0.085m,
            TaxAmount = 8.50m,
            Total = 108.50m,
            PaymentMethod = "Cash",
            EmployeeName = "Jane Doe"
        };
    }

    [Fact]
    public async Task GetSaleReceipt_AuthenticatedAndFound_Returns200WithRecPrefixedReceiptNumber()
    {
        var receipt = BuildReceiptResponse(1042);
        _receiptServiceMock.Setup(s => s.GetReceiptAsync(1042, CompanyId))
            .ReturnsAsync(ApiResponse<ReceiptResponse>.SuccessResponse(receipt));

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, body: null);

        var result = await _functions.GetSaleReceipt(req, 1042);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var apiResp = JsonSerializer.Deserialize<ApiResponse<ReceiptResponse>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        apiResp.Should().NotBeNull();
        apiResp!.Success.Should().BeTrue();
        apiResp.Data!.ReceiptNumber.Should().Be("REC-1042");
        apiResp.Data.ReceiptNumber.Should().StartWith("REC-");
    }

    [Fact]
    public async Task GetSaleReceipt_NoJwt_Returns401()
    {
        var context = TestHelpers.CreateMockFunctionContext();
        var req = TestHelpers.CreateHttpRequestData(context, body: null);

        var result = await _functions.GetSaleReceipt(req, 1042);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSaleReceipt_SaleNotFoundOrWrongCompany_Returns404()
    {
        _receiptServiceMock.Setup(s => s.GetReceiptAsync(999, CompanyId))
            .ReturnsAsync(ApiResponse<ReceiptResponse>.ErrorResponse("Sale with ID 999 not found"));

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, body: null);

        var result = await _functions.GetSaleReceipt(req, 999);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EmailSaleReceipt_ValidEmail_Returns200AndCallsService()
    {
        _receiptServiceMock
            .Setup(s => s.SendReceiptEmailAsync(1042, CompanyId, "customer@example.com"))
            .ReturnsAsync(ApiResponse<bool>.SuccessResponse(true, "Receipt email sent successfully"));

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, new SendReceiptEmailRequest { Email = "customer@example.com" });

        var result = await _functions.EmailSaleReceipt(req, 1042);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        _receiptServiceMock.Verify(s => s.SendReceiptEmailAsync(1042, CompanyId, "customer@example.com"), Times.Once);
    }

    [Fact]
    public async Task EmailSaleReceipt_MissingEmail_Returns400()
    {
        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, new SendReceiptEmailRequest { Email = "" });

        var result = await _functions.EmailSaleReceipt(req, 1042);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _receiptServiceMock.Verify(
            s => s.SendReceiptEmailAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()),
            Times.Never);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing@domain")]
    [InlineData("@nodomain.com")]
    [InlineData("spaces in@example.com")]
    public async Task EmailSaleReceipt_InvalidEmailFormat_Returns400(string invalidEmail)
    {
        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, new SendReceiptEmailRequest { Email = invalidEmail });

        var result = await _functions.EmailSaleReceipt(req, 1042);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _receiptServiceMock.Verify(
            s => s.SendReceiptEmailAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task EmailSaleReceipt_NoJwt_Returns401()
    {
        var context = TestHelpers.CreateMockFunctionContext();
        var req = TestHelpers.CreateHttpRequestData(context, new SendReceiptEmailRequest { Email = "customer@example.com" });

        var result = await _functions.EmailSaleReceipt(req, 1042);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EmailSaleReceipt_SaleNotFound_Returns404()
    {
        _receiptServiceMock
            .Setup(s => s.SendReceiptEmailAsync(999, CompanyId, "customer@example.com"))
            .ReturnsAsync(ApiResponse<bool>.ErrorResponse("Sale with ID 999 not found"));

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, new SendReceiptEmailRequest { Email = "customer@example.com" });

        var result = await _functions.EmailSaleReceipt(req, 999);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
