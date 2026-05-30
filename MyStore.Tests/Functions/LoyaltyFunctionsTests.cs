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

public class LoyaltyFunctionsTests
{
    private readonly Mock<ILoyaltyService> _serviceMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger<LoyaltyFunctions>> _loggerMock;
    private readonly LoyaltyFunctions _functions;

    private const int CompanyId = 42;
    private const int CustomerId = 5;
    private readonly IReadOnlyDictionary<string, string> _companyHeaders =
        new Dictionary<string, string> { { "X-Company-Id", "42" } };

    public LoyaltyFunctionsTests()
    {
        _serviceMock = new Mock<ILoyaltyService>();
        _loggerMock = new Mock<ILogger<LoyaltyFunctions>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();

        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        _functions = new LoyaltyFunctions(_serviceMock.Object, _loggerFactoryMock.Object);
    }

    private static LoyaltySettings CreateSettings(bool isEnabled = true) => new LoyaltySettings
    {
        Id = 1,
        CompanyId = CompanyId,
        PointsPerDollarSpent = 1m,
        PointsPerDollarTradeIn = 2m,
        RedemptionRate = 100m,
        IsEnabled = isEnabled,
    };

    #region GetLoyaltySettings Tests

    [Fact]
    public async Task GetLoyaltySettings_Returns200WithSettings()
    {
        var settings = CreateSettings();
        _serviceMock
            .Setup(s => s.GetSettingsAsync(CompanyId))
            .ReturnsAsync(ApiResponse<LoyaltySettings>.SuccessResponse(settings));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.GetLoyaltySettings(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<LoyaltySettings>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetLoyaltySettings_MissingCompanyHeader_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.GetLoyaltySettings(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _serviceMock.Verify(s => s.GetSettingsAsync(It.IsAny<int>()), Times.Never);
    }

    #endregion

    #region UpdateLoyaltySettings Tests

    [Fact]
    public async Task UpdateLoyaltySettings_ValidRequest_Returns200()
    {
        var settings = CreateSettings();
        _serviceMock
            .Setup(s => s.UpdateSettingsAsync(It.IsAny<LoyaltySettings>(), CompanyId))
            .ReturnsAsync(ApiResponse<LoyaltySettings>.SuccessResponse(settings, "Loyalty settings updated successfully"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, settings, _companyHeaders);

        var result = await _functions.UpdateLoyaltySettings(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<LoyaltySettings>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateLoyaltySettings_EnabledWithZeroPointsPerDollarSpent_Returns400()
    {
        var settings = new LoyaltySettings
        {
            IsEnabled = true,
            PointsPerDollarSpent = 0m,
            PointsPerDollarTradeIn = 1m,
            RedemptionRate = 100m,
        };

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, settings, _companyHeaders);

        var result = await _functions.UpdateLoyaltySettings(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<LoyaltySettings>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("PointsPerDollarSpent");
        _serviceMock.Verify(s => s.UpdateSettingsAsync(It.IsAny<LoyaltySettings>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpdateLoyaltySettings_EnabledWithZeroPointsPerDollarTradeIn_Returns400()
    {
        var settings = new LoyaltySettings
        {
            IsEnabled = true,
            PointsPerDollarSpent = 1m,
            PointsPerDollarTradeIn = 0m,
            RedemptionRate = 100m,
        };

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, settings, _companyHeaders);

        var result = await _functions.UpdateLoyaltySettings(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<LoyaltySettings>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("PointsPerDollarTradeIn");
        _serviceMock.Verify(s => s.UpdateSettingsAsync(It.IsAny<LoyaltySettings>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpdateLoyaltySettings_EnabledWithZeroRedemptionRate_Returns400()
    {
        var settings = new LoyaltySettings
        {
            IsEnabled = true,
            PointsPerDollarSpent = 1m,
            PointsPerDollarTradeIn = 1m,
            RedemptionRate = 0m,
        };

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, settings, _companyHeaders);

        var result = await _functions.UpdateLoyaltySettings(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<LoyaltySettings>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("RedemptionRate");
        _serviceMock.Verify(s => s.UpdateSettingsAsync(It.IsAny<LoyaltySettings>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpdateLoyaltySettings_DisabledWithZeroRates_Returns200()
    {
        var settings = new LoyaltySettings
        {
            IsEnabled = false,
            PointsPerDollarSpent = 0m,
            PointsPerDollarTradeIn = 0m,
            RedemptionRate = 0m,
        };
        _serviceMock
            .Setup(s => s.UpdateSettingsAsync(It.IsAny<LoyaltySettings>(), CompanyId))
            .ReturnsAsync(ApiResponse<LoyaltySettings>.SuccessResponse(settings));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, settings, _companyHeaders);

        var result = await _functions.UpdateLoyaltySettings(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        _serviceMock.Verify(s => s.UpdateSettingsAsync(It.IsAny<LoyaltySettings>(), CompanyId), Times.Once);
    }

    [Fact]
    public async Task UpdateLoyaltySettings_MissingCompanyHeader_Returns401()
    {
        var settings = CreateSettings();
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, settings);

        var result = await _functions.UpdateLoyaltySettings(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _serviceMock.Verify(s => s.UpdateSettingsAsync(It.IsAny<LoyaltySettings>(), It.IsAny<int>()), Times.Never);
    }

    #endregion

    #region GetCustomerLoyaltyBalance Tests

    [Fact]
    public async Task GetCustomerLoyaltyBalance_Returns200WithBalanceAndTransactions()
    {
        var balanceResponse = new LoyaltyBalanceResponse
        {
            CustomerId = CustomerId,
            Balance = 150,
            Transactions = new List<LoyaltyTransaction>
            {
                new LoyaltyTransaction
                {
                    Id = 1,
                    Points = 100,
                    TransactionType = "earn_sale",
                    CreatedAt = DateTime.UtcNow,
                },
                new LoyaltyTransaction
                {
                    Id = 2,
                    Points = 50,
                    TransactionType = "earn_tradein",
                    CreatedAt = DateTime.UtcNow,
                },
            }
        };
        _serviceMock
            .Setup(s => s.GetBalanceAsync(CompanyId, CustomerId))
            .ReturnsAsync(ApiResponse<LoyaltyBalanceResponse>.SuccessResponse(balanceResponse));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.GetCustomerLoyaltyBalance(req, CustomerId);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<LoyaltyBalanceResponse>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.Balance.Should().Be(150);
        deserialized.Data.Transactions.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetCustomerLoyaltyBalance_CustomerNotFound_Returns404()
    {
        _serviceMock
            .Setup(s => s.GetBalanceAsync(CompanyId, 99))
            .ReturnsAsync(ApiResponse<LoyaltyBalanceResponse>.ErrorResponse("Customer not found"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.GetCustomerLoyaltyBalance(req, 99);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<LoyaltyBalanceResponse>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task GetCustomerLoyaltyBalance_MissingCompanyHeader_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.GetCustomerLoyaltyBalance(req, CustomerId);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _serviceMock.Verify(s => s.GetBalanceAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    #endregion

    #region RedeemLoyaltyPoints Tests

    [Fact]
    public async Task RedeemLoyaltyPoints_ValidRequest_Returns200()
    {
        var redeemResponse = new RedeemPointsResponse
        {
            PointsRedeemed = 100,
            CreditAmount = 1.00m,
            NewBalance = 50,
        };
        _serviceMock
            .Setup(s => s.RedeemAsync(CompanyId, CustomerId, 100))
            .ReturnsAsync(ApiResponse<RedeemPointsResponse>.SuccessResponse(redeemResponse, "Redeemed 100 points for $1.00 store credit"));

        var context = new Mock<FunctionContext>();
        var body = new RedeemPointsRequest { PointsToRedeem = 100 };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body, _companyHeaders);

        var result = await _functions.RedeemLoyaltyPoints(req, CustomerId);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<RedeemPointsResponse>>(
            responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.PointsRedeemed.Should().Be(100);
        deserialized.Data.NewBalance.Should().Be(50);
    }

    [Fact]
    public async Task RedeemLoyaltyPoints_InsufficientBalance_Returns400()
    {
        _serviceMock
            .Setup(s => s.RedeemAsync(CompanyId, CustomerId, 500))
            .ReturnsAsync(ApiResponse<RedeemPointsResponse>.ErrorResponse("Insufficient loyalty points. Available: 150, Requested: 500"));

        var context = new Mock<FunctionContext>();
        var body = new RedeemPointsRequest { PointsToRedeem = 500 };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body, _companyHeaders);

        var result = await _functions.RedeemLoyaltyPoints(req, CustomerId);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<RedeemPointsResponse>>(
            responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("Insufficient");
    }

    [Fact]
    public async Task RedeemLoyaltyPoints_ZeroPoints_Returns400WithoutCallingService()
    {
        var context = new Mock<FunctionContext>();
        var body = new RedeemPointsRequest { PointsToRedeem = 0 };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body, _companyHeaders);

        var result = await _functions.RedeemLoyaltyPoints(req, CustomerId);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _serviceMock.Verify(s => s.RedeemAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task RedeemLoyaltyPoints_MissingCompanyHeader_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var body = new RedeemPointsRequest { PointsToRedeem = 100 };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body);

        var result = await _functions.RedeemLoyaltyPoints(req, CustomerId);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _serviceMock.Verify(s => s.RedeemAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    #endregion
}
