using System.Collections.Specialized;
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

public class ConsignmentFunctionsTests
{
    private readonly Mock<IConsignmentService> _serviceMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger<ConsignmentFunctions>> _loggerMock;
    private readonly ConsignmentFunctions _functions;

    private const int CompanyId = 42;
    private readonly IReadOnlyDictionary<string, string> _companyHeaders =
        new Dictionary<string, string> { { "X-Company-Id", "42" } };

    public ConsignmentFunctionsTests()
    {
        _serviceMock = new Mock<IConsignmentService>();
        _loggerMock = new Mock<ILogger<ConsignmentFunctions>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();

        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        _functions = new ConsignmentFunctions(_serviceMock.Object, _loggerFactoryMock.Object);
    }

    private static ConsignmentItem CreateItem(int id = 1, string status = "pending", decimal splitPercent = 60m) =>
        new ConsignmentItem
        {
            Id = id,
            CompanyId = CompanyId,
            CustomerId = 5,
            Description = "Vintage Game",
            AskingPrice = 50m,
            SplitPercent = splitPercent,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };

    #region GetAllConsignmentItems Tests

    [Fact]
    public async Task GetAllConsignmentItems_NoFilter_Returns200WithList()
    {
        var items = new List<ConsignmentItem> { CreateItem(1), CreateItem(2) };
        _serviceMock
            .Setup(s => s.GetAllAsync(CompanyId, null))
            .ReturnsAsync(ApiResponse<List<ConsignmentItem>>.SuccessResponse(items));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.GetAllConsignmentItems(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<ConsignmentItem>>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllConsignmentItems_WithStatusFilter_PassesStatusToService()
    {
        var items = new List<ConsignmentItem> { CreateItem(1, "active") };
        _serviceMock
            .Setup(s => s.GetAllAsync(CompanyId, "active"))
            .ReturnsAsync(ApiResponse<List<ConsignmentItem>>.SuccessResponse(items));

        var context = new Mock<FunctionContext>();
        var query = new NameValueCollection { { "status", "active" } };
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders, query);

        var result = await _functions.GetAllConsignmentItems(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        _serviceMock.Verify(s => s.GetAllAsync(CompanyId, "active"), Times.Once);
    }

    [Fact]
    public async Task GetAllConsignmentItems_MissingCompanyHeader_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.GetAllConsignmentItems(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _serviceMock.Verify(s => s.GetAllAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region GetConsignmentItemById Tests

    [Fact]
    public async Task GetConsignmentItemById_Found_Returns200()
    {
        var item = CreateItem(1);
        _serviceMock
            .Setup(s => s.GetByIdAsync(1, CompanyId))
            .ReturnsAsync(ApiResponse<ConsignmentItem>.SuccessResponse(item));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.GetConsignmentItemById(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<ConsignmentItem>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.Id.Should().Be(1);
    }

    [Fact]
    public async Task GetConsignmentItemById_NotFound_Returns404()
    {
        _serviceMock
            .Setup(s => s.GetByIdAsync(99, CompanyId))
            .ReturnsAsync(ApiResponse<ConsignmentItem>.ErrorResponse("Consignment item with ID 99 not found"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.GetConsignmentItemById(req, 99);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<ConsignmentItem>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task GetConsignmentItemById_CrossTenant_Returns404()
    {
        _serviceMock
            .Setup(s => s.GetByIdAsync(1, CompanyId))
            .ReturnsAsync(ApiResponse<ConsignmentItem>.ErrorResponse("Consignment item with ID 1 not found"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.GetConsignmentItemById(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _serviceMock.Verify(s => s.GetByIdAsync(1, CompanyId), Times.Once);
    }

    [Fact]
    public async Task GetConsignmentItemById_MissingCompanyHeader_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.GetConsignmentItemById(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _serviceMock.Verify(s => s.GetByIdAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    #endregion

    #region CreateConsignmentItem Tests

    [Fact]
    public async Task CreateConsignmentItem_ValidRequest_Returns201()
    {
        var item = CreateItem();
        _serviceMock
            .Setup(s => s.CreateAsync(It.IsAny<ConsignmentItem>(), CompanyId))
            .ReturnsAsync(ApiResponse<ConsignmentItem>.SuccessResponse(item, "Consignment item created successfully"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, item, _companyHeaders);

        var result = await _functions.CreateConsignmentItem(req);

        result.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<ConsignmentItem>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CreateConsignmentItem_MissingCustomerId_Returns400()
    {
        var item = new ConsignmentItem
        {
            CustomerId = 0,
            Description = "Test",
            AskingPrice = 10m,
            SplitPercent = 60m,
            Status = "active"
        };

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, item, _companyHeaders);

        var result = await _functions.CreateConsignmentItem(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<ConsignmentItem>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("CustomerId");
    }

    [Fact]
    public async Task CreateConsignmentItem_MissingDescription_Returns400()
    {
        var item = new ConsignmentItem
        {
            CustomerId = 5,
            Description = "",
            AskingPrice = 10m,
            SplitPercent = 60m,
            Status = "active"
        };

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, item, _companyHeaders);

        var result = await _functions.CreateConsignmentItem(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<ConsignmentItem>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("Description");
    }

    [Fact]
    public async Task CreateConsignmentItem_ZeroAskingPrice_Returns400()
    {
        var item = new ConsignmentItem
        {
            CustomerId = 5,
            Description = "Test",
            AskingPrice = 0m,
            SplitPercent = 60m,
            Status = "active"
        };

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, item, _companyHeaders);

        var result = await _functions.CreateConsignmentItem(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<ConsignmentItem>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("AskingPrice");
    }

    [Fact]
    public async Task CreateConsignmentItem_MissingCompanyHeader_Returns401()
    {
        var item = CreateItem();
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, item);

        var result = await _functions.CreateConsignmentItem(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _serviceMock.Verify(s => s.CreateAsync(It.IsAny<ConsignmentItem>(), It.IsAny<int>()), Times.Never);
    }

    #endregion

    #region UpdateConsignmentItem Tests

    [Fact]
    public async Task UpdateConsignmentItem_ValidRequest_Returns200()
    {
        var item = CreateItem();
        _serviceMock
            .Setup(s => s.UpdateAsync(It.IsAny<ConsignmentItem>(), CompanyId))
            .ReturnsAsync(ApiResponse<ConsignmentItem>.SuccessResponse(item, "Consignment item updated successfully"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, item, _companyHeaders);

        var result = await _functions.UpdateConsignmentItem(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<ConsignmentItem>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateConsignmentItem_NotFound_Returns404()
    {
        var item = CreateItem();
        _serviceMock
            .Setup(s => s.UpdateAsync(It.IsAny<ConsignmentItem>(), CompanyId))
            .ReturnsAsync(ApiResponse<ConsignmentItem>.ErrorResponse("Consignment item with ID 99 not found"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, item, _companyHeaders);

        var result = await _functions.UpdateConsignmentItem(req, 99);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<ConsignmentItem>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateConsignmentItem_InvalidStatus_Returns400WithoutCallingService()
    {
        var item = CreateItem();
        item.Status = "badstatus";

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, item, _companyHeaders);

        var result = await _functions.UpdateConsignmentItem(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<ConsignmentItem>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("Invalid status value");
        _serviceMock.Verify(s => s.UpdateAsync(It.IsAny<ConsignmentItem>(), It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("sold")]
    [InlineData("returned")]
    [InlineData("cancelled")]
    public async Task UpdateConsignmentItem_ValidStatus_CallsService(string status)
    {
        var item = CreateItem();
        item.Status = status;
        _serviceMock
            .Setup(s => s.UpdateAsync(It.IsAny<ConsignmentItem>(), CompanyId))
            .ReturnsAsync(ApiResponse<ConsignmentItem>.SuccessResponse(item));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, item, _companyHeaders);

        var result = await _functions.UpdateConsignmentItem(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        _serviceMock.Verify(s => s.UpdateAsync(It.IsAny<ConsignmentItem>(), CompanyId), Times.Once);
    }

    [Fact]
    public async Task UpdateConsignmentItem_ServiceFailureNonNotFound_Returns400()
    {
        var item = CreateItem();
        _serviceMock
            .Setup(s => s.UpdateAsync(It.IsAny<ConsignmentItem>(), CompanyId))
            .ReturnsAsync(ApiResponse<ConsignmentItem>.ErrorResponse("Failed to update consignment item"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, item, _companyHeaders);

        var result = await _functions.UpdateConsignmentItem(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<ConsignmentItem>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateConsignmentItem_MissingCompanyHeader_Returns401()
    {
        var item = CreateItem();
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, item);

        var result = await _functions.UpdateConsignmentItem(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _serviceMock.Verify(s => s.UpdateAsync(It.IsAny<ConsignmentItem>(), It.IsAny<int>()), Times.Never);
    }

    #endregion

    #region MarkConsignmentItemSold Tests

    [Fact]
    public async Task MarkConsignmentItemSold_ActiveItem_Returns200()
    {
        var soldItem = CreateItem(1, "sold");
        soldItem.SalePrice = 100m;
        var markSoldResponse = new MarkSoldResponse
        {
            Item = soldItem,
            PayoutAmount = 70m,
            StoreAmount = 30m
        };

        _serviceMock
            .Setup(s => s.MarkSoldAsync(1, 100m, CompanyId))
            .ReturnsAsync(ApiResponse<MarkSoldResponse>.SuccessResponse(markSoldResponse));

        var context = new Mock<FunctionContext>();
        var body = new { salePrice = 100m };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body, _companyHeaders);

        var result = await _functions.MarkConsignmentItemSold(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<MarkSoldResponse>>(
            responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.PayoutAmount.Should().Be(70m);
        deserialized.Data.StoreAmount.Should().Be(30m);
    }

    [Fact]
    public async Task MarkConsignmentItemSold_NotActiveStatus_Returns409Conflict()
    {
        _serviceMock
            .Setup(s => s.MarkSoldAsync(1, 100m, CompanyId))
            .ReturnsAsync(ApiResponse<MarkSoldResponse>.ErrorResponse(
                "Cannot mark item as sold: current status is 'sold'. Only active items can be marked as sold."));

        var context = new Mock<FunctionContext>();
        var body = new { salePrice = 100m };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body, _companyHeaders);

        var result = await _functions.MarkConsignmentItemSold(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<MarkSoldResponse>>(
            responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task MarkConsignmentItemSold_ZeroSalePrice_Returns400()
    {
        var context = new Mock<FunctionContext>();
        var body = new { salePrice = 0m };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body, _companyHeaders);

        var result = await _functions.MarkConsignmentItemSold(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _serviceMock.Verify(s => s.MarkSoldAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task MarkConsignmentItemSold_MissingCompanyHeader_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var body = new { salePrice = 100m };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body);

        var result = await _functions.MarkConsignmentItemSold(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _serviceMock.Verify(s => s.MarkSoldAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<int>()), Times.Never);
    }

    #endregion

    #region ProcessConsignmentPayout Tests

    [Fact]
    public async Task ProcessConsignmentPayout_SoldItem_Returns200()
    {
        var payout = new ConsignmentPayout { Id = 1, ConsignmentItemId = 1, Amount = 60m, PaidAt = DateTime.UtcNow };
        _serviceMock
            .Setup(s => s.ProcessPayoutAsync(1, It.IsAny<string?>(), CompanyId))
            .ReturnsAsync(ApiResponse<ConsignmentPayout>.SuccessResponse(payout, "Payout processed successfully"));

        var context = new Mock<FunctionContext>();
        var body = new { notes = "Cash payment" };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body, _companyHeaders);

        var result = await _functions.ProcessConsignmentPayout(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<ConsignmentPayout>>(
            responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.Amount.Should().Be(60m);
    }

    [Fact]
    public async Task ProcessConsignmentPayout_NotSoldStatus_Returns409Conflict()
    {
        _serviceMock
            .Setup(s => s.ProcessPayoutAsync(1, It.IsAny<string?>(), CompanyId))
            .ReturnsAsync(ApiResponse<ConsignmentPayout>.ErrorResponse(
                "Cannot process payout: item status is 'active'. Only sold items can receive a payout."));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, new { }, _companyHeaders);

        var result = await _functions.ProcessConsignmentPayout(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<ConsignmentPayout>>(
            responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessConsignmentPayout_AlreadyProcessed_Returns409Conflict()
    {
        _serviceMock
            .Setup(s => s.ProcessPayoutAsync(1, It.IsAny<string?>(), CompanyId))
            .ReturnsAsync(ApiResponse<ConsignmentPayout>.ErrorResponse(
                "Payout has already been processed for consignment item 1"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, new { }, _companyHeaders);

        var result = await _functions.ProcessConsignmentPayout(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ProcessConsignmentPayout_MissingCompanyHeader_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, new { });

        var result = await _functions.ProcessConsignmentPayout(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _serviceMock.Verify(s => s.ProcessPayoutAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int>()), Times.Never);
    }

    #endregion

    #region ReturnConsignmentItemToCustomer Tests

    [Fact]
    public async Task ReturnConsignmentItemToCustomer_ActiveItem_Returns200()
    {
        var returned = CreateItem(1, "returned");
        _serviceMock
            .Setup(s => s.ReturnToCustomerAsync(1, CompanyId))
            .ReturnsAsync(ApiResponse<ConsignmentItem>.SuccessResponse(returned, "Item returned to customer successfully"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.ReturnConsignmentItemToCustomer(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<ConsignmentItem>>(
            responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.Status.Should().Be("returned");
    }

    [Fact]
    public async Task ReturnConsignmentItemToCustomer_NotActiveStatus_Returns409Conflict()
    {
        _serviceMock
            .Setup(s => s.ReturnToCustomerAsync(1, CompanyId))
            .ReturnsAsync(ApiResponse<ConsignmentItem>.ErrorResponse(
                "Cannot return item: current status is 'sold'. Only active items can be returned."));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.ReturnConsignmentItemToCustomer(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<ConsignmentItem>>(
            responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ReturnConsignmentItemToCustomer_MissingCompanyHeader_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.ReturnConsignmentItemToCustomer(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _serviceMock.Verify(s => s.ReturnToCustomerAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    #endregion

    #region Payout Calculation Tests

    [Fact]
    public async Task MarkConsignmentItemSold_SalePrice100_SplitPercent70_CustomerGets70_StoreGets30()
    {
        var soldItem = CreateItem(1, "sold", splitPercent: 70m);
        soldItem.SalePrice = 100m;
        var markSoldResponse = new MarkSoldResponse
        {
            Item = soldItem,
            PayoutAmount = 70m,
            StoreAmount = 30m
        };

        _serviceMock
            .Setup(s => s.MarkSoldAsync(1, 100m, CompanyId))
            .ReturnsAsync(ApiResponse<MarkSoldResponse>.SuccessResponse(markSoldResponse));

        var context = new Mock<FunctionContext>();
        var body = new { salePrice = 100m };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body, _companyHeaders);

        var result = await _functions.MarkConsignmentItemSold(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<MarkSoldResponse>>(
            responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Data!.PayoutAmount.Should().Be(70m);
        deserialized.Data.StoreAmount.Should().Be(30m);
    }

    [Fact]
    public async Task MarkConsignmentItemSold_SalePrice100_SplitPercent100_StoreGetsZero()
    {
        var soldItem = CreateItem(1, "sold", splitPercent: 100m);
        soldItem.SalePrice = 100m;
        var markSoldResponse = new MarkSoldResponse
        {
            Item = soldItem,
            PayoutAmount = 100m,
            StoreAmount = 0m
        };

        _serviceMock
            .Setup(s => s.MarkSoldAsync(1, 100m, CompanyId))
            .ReturnsAsync(ApiResponse<MarkSoldResponse>.SuccessResponse(markSoldResponse));

        var context = new Mock<FunctionContext>();
        var body = new { salePrice = 100m };
        var req = TestHelpers.CreateHttpRequestData(context.Object, body, _companyHeaders);

        var result = await _functions.MarkConsignmentItemSold(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<MarkSoldResponse>>(
            responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Data!.StoreAmount.Should().Be(0m);
        deserialized.Data.PayoutAmount.Should().Be(100m);
    }

    #endregion
}
