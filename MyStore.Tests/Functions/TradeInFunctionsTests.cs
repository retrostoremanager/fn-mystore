using System.Collections.Specialized;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Moq;
using MyStore.Functions;
using MyStore.Functions.Attributes;
using MyStore.Models;
using MyStore.Services;
using MyStore.Tests.Helpers;
using Xunit;

namespace MyStore.Tests.Functions;

public class TradeInFunctionsTests
{
    private readonly Mock<ITradeInService> _serviceMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger<TradeInFunctions>> _loggerMock;
    private readonly TradeInFunctions _functions;

    private const int CompanyId = 42;
    private readonly IReadOnlyDictionary<string, string> _companyHeaders =
        new Dictionary<string, string> { { "X-Company-Id", "42" } };

    public TradeInFunctionsTests()
    {
        _serviceMock = new Mock<ITradeInService>();
        _loggerMock = new Mock<ILogger<TradeInFunctions>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();

        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        _functions = new TradeInFunctions(_serviceMock.Object, _loggerFactoryMock.Object);
    }

    private static TradeIn CreateTradeIn(int id = 1, string status = "draft") =>
        new TradeIn
        {
            Id = id,
            CompanyId = CompanyId,
            CustomerId = 5,
            Status = status,
            TotalOfferedValue = 25m,
            Notes = "Test trade-in",
            CreatedAt = DateTime.UtcNow,
            Items = new List<TradeInItem>
            {
                new TradeInItem
                {
                    Id = 1,
                    TradeInId = id,
                    GameTitle = "Sonic the Hedgehog",
                    Platform = "Sega Genesis",
                    Condition = "good",
                    OfferedValue = 25m,
                    CreatedAt = DateTime.UtcNow
                }
            }
        };

    #region GetAllTradeIns Tests

    [Fact]
    public async Task GetAllTradeIns_NoFilter_Returns200WithList()
    {
        var items = new List<TradeIn> { CreateTradeIn(1), CreateTradeIn(2) };
        _serviceMock
            .Setup(s => s.GetAllAsync(CompanyId, null, null, null))
            .ReturnsAsync(ApiResponse<List<TradeIn>>.SuccessResponse(items));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.GetAllTradeIns(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<TradeIn>>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllTradeIns_WithStatusFilter_PassesStatusToService()
    {
        var items = new List<TradeIn> { CreateTradeIn(1, "completed") };
        _serviceMock
            .Setup(s => s.GetAllAsync(CompanyId, "completed", null, null))
            .ReturnsAsync(ApiResponse<List<TradeIn>>.SuccessResponse(items));

        var context = new Mock<FunctionContext>();
        var query = new NameValueCollection { { "status", "completed" } };
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders, query);

        var result = await _functions.GetAllTradeIns(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        _serviceMock.Verify(s => s.GetAllAsync(CompanyId, "completed", null, null), Times.Once);
    }

    [Fact]
    public async Task GetAllTradeIns_WithDateFilters_ParsesAndPassesDates()
    {
        var items = new List<TradeIn> { CreateTradeIn(1) };
        _serviceMock
            .Setup(s => s.GetAllAsync(CompanyId, null, It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(ApiResponse<List<TradeIn>>.SuccessResponse(items));

        var context = new Mock<FunctionContext>();
        var query = new NameValueCollection
        {
            { "dateFrom", "2024-01-01" },
            { "dateTo", "2024-12-31" }
        };
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders, query);

        var result = await _functions.GetAllTradeIns(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        _serviceMock.Verify(s => s.GetAllAsync(CompanyId, null, It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Once);
    }

    [Fact]
    public async Task GetAllTradeIns_MissingCompanyHeader_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.GetAllTradeIns(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _serviceMock.Verify(s => s.GetAllAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Never);
    }

    #endregion

    #region CreateTradeIn Tests

    [Fact]
    public async Task CreateTradeIn_ValidRequest_Returns201()
    {
        var tradeIn = CreateTradeIn();
        _serviceMock
            .Setup(s => s.CreateDraftAsync(It.IsAny<TradeIn>(), CompanyId))
            .ReturnsAsync(ApiResponse<TradeIn>.SuccessResponse(tradeIn, "Trade-in draft created successfully"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, tradeIn, _companyHeaders);

        var result = await _functions.CreateTradeIn(req);

        result.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<TradeIn>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CreateTradeIn_InvalidBody_Returns400()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestDataWithRawBody("not-json", _companyHeaders);

        var result = await _functions.CreateTradeIn(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _serviceMock.Verify(s => s.CreateDraftAsync(It.IsAny<TradeIn>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CreateTradeIn_MissingCompanyHeader_Returns401()
    {
        var tradeIn = CreateTradeIn();
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, tradeIn);

        var result = await _functions.CreateTradeIn(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _serviceMock.Verify(s => s.CreateDraftAsync(It.IsAny<TradeIn>(), It.IsAny<int>()), Times.Never);
    }

    #endregion

    #region GetTradeInById Tests

    [Fact]
    public async Task GetTradeInById_Found_Returns200()
    {
        var tradeIn = CreateTradeIn(1);
        _serviceMock
            .Setup(s => s.GetByIdAsync(1, CompanyId))
            .ReturnsAsync(ApiResponse<TradeIn>.SuccessResponse(tradeIn));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.GetTradeInById(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<TradeIn>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.Id.Should().Be(1);
    }

    [Fact]
    public async Task GetTradeInById_NotFound_Returns404()
    {
        _serviceMock
            .Setup(s => s.GetByIdAsync(99, CompanyId))
            .ReturnsAsync(ApiResponse<TradeIn>.ErrorResponse("Trade-in with ID 99 not found"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.GetTradeInById(req, 99);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<TradeIn>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task GetTradeInById_CrossTenant_Returns404()
    {
        _serviceMock
            .Setup(s => s.GetByIdAsync(1, CompanyId))
            .ReturnsAsync(ApiResponse<TradeIn>.ErrorResponse("Trade-in with ID 1 not found"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.GetTradeInById(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _serviceMock.Verify(s => s.GetByIdAsync(1, CompanyId), Times.Once);
    }

    [Fact]
    public async Task GetTradeInById_MissingCompanyHeader_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.GetTradeInById(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _serviceMock.Verify(s => s.GetByIdAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    #endregion

    #region UpdateTradeIn Tests

    [Fact]
    public async Task UpdateTradeIn_ValidRequest_Returns200()
    {
        var tradeIn = CreateTradeIn(1);
        _serviceMock
            .Setup(s => s.UpdateTradeInAsync(1, CompanyId, It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<List<TradeInItem>>()))
            .ReturnsAsync(ApiResponse<TradeIn>.SuccessResponse(tradeIn, "Trade-in updated successfully"));

        var context = new Mock<FunctionContext>();
        var updateBody = new { notes = "Updated notes", customerId = 5, items = new List<object>() };
        var req = TestHelpers.CreateHttpRequestData(context.Object, updateBody, _companyHeaders);

        var result = await _functions.UpdateTradeIn(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<TradeIn>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateTradeIn_NotDraftStatus_Returns400()
    {
        _serviceMock
            .Setup(s => s.UpdateTradeInAsync(1, CompanyId, It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<List<TradeInItem>>()))
            .ReturnsAsync(ApiResponse<TradeIn>.ErrorResponse("Cannot update a trade-in with status 'completed'. Only draft trade-ins can be modified."));

        var context = new Mock<FunctionContext>();
        var updateBody = new { notes = "Updated notes" };
        var req = TestHelpers.CreateHttpRequestData(context.Object, updateBody, _companyHeaders);

        var result = await _functions.UpdateTradeIn(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<TradeIn>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateTradeIn_NotFound_Returns404()
    {
        _serviceMock
            .Setup(s => s.UpdateTradeInAsync(99, CompanyId, It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<List<TradeInItem>>()))
            .ReturnsAsync(ApiResponse<TradeIn>.ErrorResponse("Trade-in with ID 99 not found"));

        var context = new Mock<FunctionContext>();
        var updateBody = new { notes = "Updated notes" };
        var req = TestHelpers.CreateHttpRequestData(context.Object, updateBody, _companyHeaders);

        var result = await _functions.UpdateTradeIn(req, 99);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateTradeIn_InvalidBody_Returns400()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestDataWithRawBody("not-json", _companyHeaders);

        var result = await _functions.UpdateTradeIn(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _serviceMock.Verify(s => s.UpdateTradeInAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<List<TradeInItem>>()), Times.Never);
    }

    [Fact]
    public async Task UpdateTradeIn_MissingCompanyHeader_Returns401()
    {
        var updateBody = new { notes = "Updated notes" };
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, updateBody);

        var result = await _functions.UpdateTradeIn(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _serviceMock.Verify(s => s.UpdateTradeInAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<List<TradeInItem>>()), Times.Never);
    }

    #endregion

    #region CompleteTradeIn Tests

    [Fact]
    public async Task CompleteTradeIn_CashPayment_Returns200()
    {
        var completed = CreateTradeIn(1, "completed");
        _serviceMock
            .Setup(s => s.CompleteAsync(1, CompanyId, "cash"))
            .ReturnsAsync(ApiResponse<TradeIn>.SuccessResponse(completed, "Trade-in completed successfully"));

        var context = new Mock<FunctionContext>();
        var body = new { paymentType = "cash" };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body, _companyHeaders);

        var result = await _functions.CompleteTradeIn(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<TradeIn>>(
            responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.Status.Should().Be("completed");
    }

    [Fact]
    public async Task CompleteTradeIn_StoreCreditPayment_Returns200()
    {
        var completed = CreateTradeIn(1, "completed");
        _serviceMock
            .Setup(s => s.CompleteAsync(1, CompanyId, "store_credit"))
            .ReturnsAsync(ApiResponse<TradeIn>.SuccessResponse(completed, "Trade-in completed successfully"));

        var context = new Mock<FunctionContext>();
        var body = new { paymentType = "store_credit" };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body, _companyHeaders);

        var result = await _functions.CompleteTradeIn(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CompleteTradeIn_MissingPaymentType_Returns400()
    {
        var context = new Mock<FunctionContext>();
        var body = new { };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body, _companyHeaders);

        var result = await _functions.CompleteTradeIn(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<TradeIn>>(
            responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("paymentType");
        _serviceMock.Verify(s => s.CompleteAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CompleteTradeIn_InvalidPaymentType_Returns400()
    {
        var context = new Mock<FunctionContext>();
        var body = new { paymentType = "bitcoin" };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body, _companyHeaders);

        var result = await _functions.CompleteTradeIn(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<TradeIn>>(
            responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("cash");
        _serviceMock.Verify(s => s.CompleteAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CompleteTradeIn_AlreadyCompleted_Returns400()
    {
        _serviceMock
            .Setup(s => s.CompleteAsync(1, CompanyId, "cash"))
            .ReturnsAsync(ApiResponse<TradeIn>.ErrorResponse("Cannot complete a trade-in with status 'completed'. Only draft trade-ins can be completed."));

        var context = new Mock<FunctionContext>();
        var body = new { paymentType = "cash" };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body, _companyHeaders);

        var result = await _functions.CompleteTradeIn(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<TradeIn>>(
            responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task CompleteTradeIn_AlreadyRejected_Returns400()
    {
        _serviceMock
            .Setup(s => s.CompleteAsync(1, CompanyId, "cash"))
            .ReturnsAsync(ApiResponse<TradeIn>.ErrorResponse("Cannot complete a trade-in with status 'rejected'. Only draft trade-ins can be completed."));

        var context = new Mock<FunctionContext>();
        var body = new { paymentType = "cash" };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body, _companyHeaders);

        var result = await _functions.CompleteTradeIn(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CompleteTradeIn_NotFound_Returns404()
    {
        _serviceMock
            .Setup(s => s.CompleteAsync(99, CompanyId, "cash"))
            .ReturnsAsync(ApiResponse<TradeIn>.ErrorResponse("Trade-in with ID 99 not found"));

        var context = new Mock<FunctionContext>();
        var body = new { paymentType = "cash" };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body, _companyHeaders);

        var result = await _functions.CompleteTradeIn(req, 99);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CompleteTradeIn_MissingCompanyHeader_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var body = new { paymentType = "cash" };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body);

        var result = await _functions.CompleteTradeIn(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _serviceMock.Verify(s => s.CompleteAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region RejectTradeIn Tests

    [Fact]
    public async Task RejectTradeIn_DraftTradeIn_Returns200()
    {
        var rejected = CreateTradeIn(1, "rejected");
        _serviceMock
            .Setup(s => s.RejectAsync(1, CompanyId))
            .ReturnsAsync(ApiResponse<TradeIn>.SuccessResponse(rejected, "Trade-in rejected successfully"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.RejectTradeIn(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<TradeIn>>(
            responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.Status.Should().Be("rejected");
    }

    [Fact]
    public async Task RejectTradeIn_AlreadyCompleted_Returns400()
    {
        _serviceMock
            .Setup(s => s.RejectAsync(1, CompanyId))
            .ReturnsAsync(ApiResponse<TradeIn>.ErrorResponse("Cannot reject a trade-in with status 'completed'. Only draft trade-ins can be rejected."));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.RejectTradeIn(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<TradeIn>>(
            responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task RejectTradeIn_AlreadyRejected_Returns400()
    {
        _serviceMock
            .Setup(s => s.RejectAsync(1, CompanyId))
            .ReturnsAsync(ApiResponse<TradeIn>.ErrorResponse("Cannot reject a trade-in with status 'rejected'. Only draft trade-ins can be rejected."));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.RejectTradeIn(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RejectTradeIn_NotFound_Returns404()
    {
        _serviceMock
            .Setup(s => s.RejectAsync(99, CompanyId))
            .ReturnsAsync(ApiResponse<TradeIn>.ErrorResponse("Trade-in with ID 99 not found"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.RejectTradeIn(req, 99);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RejectTradeIn_MissingCompanyHeader_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.RejectTradeIn(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _serviceMock.Verify(s => s.RejectAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    #endregion

    #region ParseTradeInImage Tests

    [Fact]
    public async Task ParseTradeInImage_ValidRequest_Returns200WithItems()
    {
        var result = new ParseImageResult
        {
            Items = new List<ParsedTradeInItem>
            {
                new ParsedTradeInItem
                {
                    GameTitle = "Super Mario Bros",
                    Platform = "NES",
                    Condition = "good",
                    OfferedValue = null
                }
            }
        };
        _serviceMock
            .Setup(s => s.ParseImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(ApiResponse<ParseImageResult>.SuccessResponse(result));

        var context = new Mock<FunctionContext>();
        var body = new { imageBase64 = "dGVzdA==", mimeType = "image/jpeg" };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body, _companyHeaders);

        var response = await _functions.ParseTradeInImage(req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseBody = await TestHelpers.ReadResponseBody(response);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<ParseImageResult>>(
            responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.Items.Should().HaveCount(1);
        deserialized.Data.Items[0].GameTitle.Should().Be("Super Mario Bros");
    }

    [Fact]
    public async Task ParseTradeInImage_MissingImageBase64_Returns400()
    {
        var context = new Mock<FunctionContext>();
        var body = new { imageBase64 = "", mimeType = "image/jpeg" };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body, _companyHeaders);

        var response = await _functions.ParseTradeInImage(req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _serviceMock.Verify(s => s.ParseImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ParseTradeInImage_InvalidBody_Returns400()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestDataWithRawBody("not-json", _companyHeaders);

        var response = await _functions.ParseTradeInImage(req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _serviceMock.Verify(s => s.ParseImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ParseTradeInImage_MissingCompanyHeader_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var body = new { imageBase64 = "dGVzdA==", mimeType = "image/jpeg" };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body);

        var response = await _functions.ParseTradeInImage(req);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _serviceMock.Verify(s => s.ParseImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    #endregion

    #region Permission Attribute Tests

    [Fact]
    public void TradeInFunctions_ClassLevel_RequiresTradeInViewPermission()
    {
        var attrs = typeof(TradeInFunctions)
            .GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: false)
            .Cast<RequirePermissionAttribute>()
            .ToList();

        attrs.Should().ContainSingle(a => a.Permission == "trade_in.view");
    }

    [Fact]
    public void CreateTradeIn_MethodLevel_RequiresTradeInCreatePermission()
    {
        var method = typeof(TradeInFunctions).GetMethod(nameof(TradeInFunctions.CreateTradeIn));
        var attrs = method!
            .GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: false)
            .Cast<RequirePermissionAttribute>()
            .ToList();

        attrs.Should().ContainSingle(a => a.Permission == "trade_in.create");
    }

    [Fact]
    public void UpdateTradeIn_MethodLevel_RequiresTradeInCreatePermission()
    {
        var method = typeof(TradeInFunctions).GetMethod(nameof(TradeInFunctions.UpdateTradeIn));
        var attrs = method!
            .GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: false)
            .Cast<RequirePermissionAttribute>()
            .ToList();

        attrs.Should().ContainSingle(a => a.Permission == "trade_in.create");
    }

    [Fact]
    public void CompleteTradeIn_MethodLevel_RequiresTradeInCompletePermission()
    {
        var method = typeof(TradeInFunctions).GetMethod(nameof(TradeInFunctions.CompleteTradeIn));
        var attrs = method!
            .GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: false)
            .Cast<RequirePermissionAttribute>()
            .ToList();

        attrs.Should().ContainSingle(a => a.Permission == "trade_in.complete");
    }

    [Fact]
    public void RejectTradeIn_MethodLevel_RequiresTradeInCompletePermission()
    {
        var method = typeof(TradeInFunctions).GetMethod(nameof(TradeInFunctions.RejectTradeIn));
        var attrs = method!
            .GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: false)
            .Cast<RequirePermissionAttribute>()
            .ToList();

        attrs.Should().ContainSingle(a => a.Permission == "trade_in.complete");
    }

    #endregion
}
