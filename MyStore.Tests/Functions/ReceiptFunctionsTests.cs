using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
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
    private readonly Mock<IReceiptService> _receiptServiceMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger<ReceiptFunctions>> _loggerMock;
    private readonly ReceiptFunctions _functions;

    private const int CompanyId = 1;

    private static readonly Dictionary<string, string> CompanyHeaders =
        new() { { "X-Company-Id", CompanyId.ToString() } };

    public ReceiptFunctionsTests()
    {
        _receiptServiceMock = new Mock<IReceiptService>();
        _loggerMock = new Mock<ILogger<ReceiptFunctions>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();

        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        _functions = new ReceiptFunctions(_receiptServiceMock.Object, _loggerFactoryMock.Object);
    }

    #region EmailSaleReceipt Tests

    [Fact]
    public async Task EmailSaleReceipt_ValidRequest_Returns200OK()
    {
        var apiResponse = ApiResponse<bool>.SuccessResponse(true, "Receipt email sent successfully");

        _receiptServiceMock
            .Setup(s => s.SendReceiptEmailAsync(1, CompanyId, "buyer@example.com"))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(
            context.Object,
            new SendReceiptEmailRequest { Email = "buyer@example.com" },
            CompanyHeaders);

        var result = await _functions.EmailSaleReceipt(httpRequest, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<bool>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        _receiptServiceMock.Verify(s => s.SendReceiptEmailAsync(1, CompanyId, "buyer@example.com"), Times.Once);
    }

    [Fact]
    public async Task EmailSaleReceipt_MissingEmail_Returns400BadRequest()
    {
        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(
            context.Object,
            new SendReceiptEmailRequest { Email = string.Empty },
            CompanyHeaders);

        var result = await _functions.EmailSaleReceipt(httpRequest, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<bool>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().ContainEquivalentOf("email");
        _receiptServiceMock.Verify(
            s => s.SendReceiptEmailAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task EmailSaleReceipt_MalformedEmail_Returns400BadRequest()
    {
        var apiResponse = ApiResponse<bool>.ErrorResponse("Email address is not valid");

        _receiptServiceMock
            .Setup(s => s.SendReceiptEmailAsync(1, CompanyId, "not-an-email"))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(
            context.Object,
            new SendReceiptEmailRequest { Email = "not-an-email" },
            CompanyHeaders);

        var result = await _functions.EmailSaleReceipt(httpRequest, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<bool>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().ContainEquivalentOf("not valid");
    }

    [Fact]
    public async Task EmailSaleReceipt_SaleNotFound_Returns404()
    {
        var apiResponse = ApiResponse<bool>.ErrorResponse("Sale with ID 999 not found");

        _receiptServiceMock
            .Setup(s => s.SendReceiptEmailAsync(999, CompanyId, "buyer@example.com"))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(
            context.Object,
            new SendReceiptEmailRequest { Email = "buyer@example.com" },
            CompanyHeaders);

        var result = await _functions.EmailSaleReceipt(httpRequest, 999);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EmailSaleReceipt_MissingCompanyId_Returns401Unauthorized()
    {
        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(
            context.Object,
            new SendReceiptEmailRequest { Email = "buyer@example.com" });

        var result = await _functions.EmailSaleReceipt(httpRequest, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EmailSaleReceipt_EmptyBody_Returns400BadRequest()
    {
        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.EmailSaleReceipt(httpRequest, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<bool>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    #endregion
}
