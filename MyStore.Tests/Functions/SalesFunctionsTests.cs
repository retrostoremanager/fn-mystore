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

public class SalesFunctionsTests
{
    private readonly Mock<ISalesService> _salesServiceMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger<SalesFunctions>> _loggerMock;
    private readonly SalesFunctions _functions;

    private const int CompanyId = 1;
    private const int OtherCompanyId = 99;

    private static readonly Dictionary<string, string> CompanyHeaders =
        new() { { "X-Company-Id", CompanyId.ToString() } };

    public SalesFunctionsTests()
    {
        _salesServiceMock = new Mock<ISalesService>();
        _loggerMock = new Mock<ILogger<SalesFunctions>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();

        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        _functions = new SalesFunctions(_salesServiceMock.Object, _loggerFactoryMock.Object);
    }

    private static Sale CreateSale(int id = 1, int companyId = CompanyId) => new Sale
    {
        Id = id,
        CompanyId = companyId,
        CustomerId = 10,
        PaymentMethod = "Cash",
        SaleDate = DateTime.UtcNow,
        Subtotal = 50m,
        Tax = 5m,
        Total = 55m,
        Items = new List<SaleItem>
        {
            new SaleItem { Id = 1, SaleId = id, InventoryItemId = 100, Quantity = 2, UnitPrice = 25m, TotalPrice = 50m }
        }
    };

    private static CreateSaleRequest CreateValidSaleRequest() => new CreateSaleRequest
    {
        CustomerId = 10,
        PaymentMethod = "Cash",
        Tax = 5m,
        Items = new List<CreateSaleItemRequest>
        {
            new CreateSaleItemRequest { InventoryItemId = 100, Quantity = 2, UnitPrice = 25m }
        }
    };

    #region CreateSale Tests

    [Fact]
    public async Task CreateSale_ValidRequest_Returns201Created()
    {
        var request = CreateValidSaleRequest();
        var sale = CreateSale();
        var apiResponse = ApiResponse<Sale>.SuccessResponse(sale, "Sale created successfully");

        _salesServiceMock
            .Setup(s => s.CreateSaleAsync(It.IsAny<CreateSaleRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, request, CompanyHeaders);

        var result = await _functions.CreateSale(httpRequest);

        result.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Sale>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().NotBeNull();
        deserialized.Data!.Id.Should().Be(1);
        deserialized.Data.CompanyId.Should().Be(CompanyId);
    }

    [Fact]
    public async Task CreateSale_MultipleLineItems_Returns201WithCorrectTotal()
    {
        var request = new CreateSaleRequest
        {
            CustomerId = 10,
            PaymentMethod = "Card",
            Tax = 10m,
            Items = new List<CreateSaleItemRequest>
            {
                new CreateSaleItemRequest { InventoryItemId = 100, Quantity = 2, UnitPrice = 25m },
                new CreateSaleItemRequest { InventoryItemId = 101, Quantity = 1, UnitPrice = 50m }
            }
        };

        var sale = new Sale
        {
            Id = 1,
            CompanyId = CompanyId,
            CustomerId = 10,
            PaymentMethod = "Card",
            SaleDate = DateTime.UtcNow,
            Subtotal = 100m,
            Tax = 10m,
            Total = 110m,
            Items = new List<SaleItem>
            {
                new SaleItem { InventoryItemId = 100, Quantity = 2, UnitPrice = 25m, TotalPrice = 50m },
                new SaleItem { InventoryItemId = 101, Quantity = 1, UnitPrice = 50m, TotalPrice = 50m }
            }
        };

        var apiResponse = ApiResponse<Sale>.SuccessResponse(sale);

        _salesServiceMock
            .Setup(s => s.CreateSaleAsync(It.IsAny<CreateSaleRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, request, CompanyHeaders);

        var result = await _functions.CreateSale(httpRequest);

        result.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Sale>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Data!.Total.Should().Be(110m);
        deserialized.Data.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateSale_ServiceReturnsFailure_Returns400BadRequest()
    {
        var request = CreateValidSaleRequest();
        var apiResponse = ApiResponse<Sale>.ErrorResponse("Insufficient inventory quantity");

        _salesServiceMock
            .Setup(s => s.CreateSaleAsync(It.IsAny<CreateSaleRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, request, CompanyHeaders);

        var result = await _functions.CreateSale(httpRequest);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Sale>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("Insufficient inventory");
    }

    [Fact]
    public async Task CreateSale_EmptyItems_Returns400BadRequest()
    {
        var request = new CreateSaleRequest
        {
            CustomerId = 10,
            PaymentMethod = "Cash",
            Items = new List<CreateSaleItemRequest>()
        };

        var apiResponse = ApiResponse<Sale>.ErrorResponse("Sale must contain at least one item");

        _salesServiceMock
            .Setup(s => s.CreateSaleAsync(It.IsAny<CreateSaleRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, request, CompanyHeaders);

        var result = await _functions.CreateSale(httpRequest);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Sale>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task CreateSale_EmptyBody_Returns400BadRequest()
    {
        var request = new CreateSaleRequest
        {
            CustomerId = 0,
            PaymentMethod = string.Empty,
            Items = new List<CreateSaleItemRequest>()
        };

        var apiResponse = ApiResponse<Sale>.ErrorResponse("Invalid request body");

        _salesServiceMock
            .Setup(s => s.CreateSaleAsync(It.IsAny<CreateSaleRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, request, CompanyHeaders);

        var result = await _functions.CreateSale(httpRequest);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Sale>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task CreateSale_MissingCompanyId_Returns401Unauthorized()
    {
        var request = CreateValidSaleRequest();
        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, request);

        var result = await _functions.CreateSale(httpRequest);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateSale_CallsServiceWithCorrectCompanyId()
    {
        var request = CreateValidSaleRequest();
        var apiResponse = ApiResponse<Sale>.SuccessResponse(CreateSale());

        _salesServiceMock
            .Setup(s => s.CreateSaleAsync(It.IsAny<CreateSaleRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, request, CompanyHeaders);

        await _functions.CreateSale(httpRequest);

        _salesServiceMock.Verify(s => s.CreateSaleAsync(It.IsAny<CreateSaleRequest>(), CompanyId), Times.Once);
    }

    [Fact]
    public async Task CreateSale_ServiceException_Returns500WithErrorBody()
    {
        var request = CreateValidSaleRequest();

        _salesServiceMock
            .Setup(s => s.CreateSaleAsync(It.IsAny<CreateSaleRequest>(), CompanyId))
            .ThrowsAsync(new Exception("Database error"));

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, request, CompanyHeaders);

        var response = await _functions.CreateSale(httpRequest);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await TestHelpers.ReadResponseBody(response);
        body.Should().Contain("An unexpected error occurred");
    }

    #endregion

    #region GetAllSales Tests

    [Fact]
    public async Task GetAllSales_ValidRequest_Returns200WithSalesList()
    {
        var sales = new List<Sale> { CreateSale(1), CreateSale(2) };
        var apiResponse = ApiResponse<List<Sale>>.SuccessResponse(sales);

        _salesServiceMock
            .Setup(s => s.GetAllSalesAsync(CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetAllSales(httpRequest);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Sale>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllSales_ScopedToCompany_OnlyReturnsCompanySales()
    {
        var sales = new List<Sale> { CreateSale(1, CompanyId), CreateSale(2, CompanyId) };
        var apiResponse = ApiResponse<List<Sale>>.SuccessResponse(sales);

        _salesServiceMock
            .Setup(s => s.GetAllSalesAsync(CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetAllSales(httpRequest);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Sale>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Data.Should().AllSatisfy(s => s.CompanyId.Should().Be(CompanyId));

        _salesServiceMock.Verify(s => s.GetAllSalesAsync(CompanyId), Times.Once);
        _salesServiceMock.Verify(s => s.GetAllSalesAsync(OtherCompanyId), Times.Never);
    }

    [Fact]
    public async Task GetAllSales_EmptyList_Returns200WithEmptyArray()
    {
        var apiResponse = ApiResponse<List<Sale>>.SuccessResponse(new List<Sale>());

        _salesServiceMock
            .Setup(s => s.GetAllSalesAsync(CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetAllSales(httpRequest);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Sale>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllSales_MissingCompanyId_Returns401Unauthorized()
    {
        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.GetAllSales(httpRequest);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAllSales_ResponseHasCorrectContentType()
    {
        var apiResponse = ApiResponse<List<Sale>>.SuccessResponse(new List<Sale>());

        _salesServiceMock
            .Setup(s => s.GetAllSalesAsync(CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetAllSales(httpRequest);

        result.Headers.Should().ContainKey("Content-Type");
        result.Headers.GetValues("Content-Type").Should().Contain("application/json; charset=utf-8");
    }

    #endregion

    #region GetSaleById Tests

    [Fact]
    public async Task GetSaleById_ExistingSale_Returns200WithSaleAndLineItems()
    {
        var sale = CreateSale(1);
        var apiResponse = ApiResponse<Sale>.SuccessResponse(sale);

        _salesServiceMock
            .Setup(s => s.GetSaleByIdAsync(1, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetSaleById(httpRequest, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Sale>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().NotBeNull();
        deserialized.Data!.Id.Should().Be(1);
        deserialized.Data.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetSaleById_NotFound_ReturnsBadRequest()
    {
        var apiResponse = ApiResponse<Sale>.ErrorResponse("Sale not found");

        _salesServiceMock
            .Setup(s => s.GetSaleByIdAsync(999, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetSaleById(httpRequest, 999);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Sale>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task GetSaleById_CrossCompanyAccess_ReturnsBadRequestWithDeniedMessage()
    {
        var apiResponse = ApiResponse<Sale>.ErrorResponse("Access denied: sale belongs to a different company");

        _salesServiceMock
            .Setup(s => s.GetSaleByIdAsync(1, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetSaleById(httpRequest, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Sale>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().ContainEquivalentOf("denied");
    }

    [Fact]
    public async Task GetSaleById_MissingCompanyId_Returns401Unauthorized()
    {
        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.GetSaleById(httpRequest, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSaleById_CallsServiceWithCorrectCompanyId()
    {
        var apiResponse = ApiResponse<Sale>.SuccessResponse(CreateSale());

        _salesServiceMock
            .Setup(s => s.GetSaleByIdAsync(1, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        await _functions.GetSaleById(httpRequest, 1);

        _salesServiceMock.Verify(s => s.GetSaleByIdAsync(1, CompanyId), Times.Once);
    }

    #endregion

    #region GetSalesByDateRange Tests

    [Fact]
    public async Task GetSalesByDateRange_ValidDates_Returns200WithFilteredSales()
    {
        var sales = new List<Sale> { CreateSale(1) };
        var apiResponse = ApiResponse<List<Sale>>.SuccessResponse(sales);

        _salesServiceMock
            .Setup(s => s.GetSalesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var query = new NameValueCollection
        {
            { "startDate", "2024-01-01" },
            { "endDate", "2024-01-31" }
        };
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders, query);

        var result = await _functions.GetSalesByDateRange(httpRequest);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Sale>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetSalesByDateRange_MissingStartDate_Returns400BadRequest()
    {
        var context = new Mock<FunctionContext>();
        var query = new NameValueCollection { { "endDate", "2024-01-31" } };
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders, query);

        var result = await _functions.GetSalesByDateRange(httpRequest);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Sale>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("startDate");
    }

    [Fact]
    public async Task GetSalesByDateRange_MissingEndDate_Returns400BadRequest()
    {
        var context = new Mock<FunctionContext>();
        var query = new NameValueCollection { { "startDate", "2024-01-01" } };
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders, query);

        var result = await _functions.GetSalesByDateRange(httpRequest);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Sale>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task GetSalesByDateRange_InvalidDateFormat_Returns400BadRequest()
    {
        var context = new Mock<FunctionContext>();
        var query = new NameValueCollection
        {
            { "startDate", "not-a-date" },
            { "endDate", "also-not-a-date" }
        };
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders, query);

        var result = await _functions.GetSalesByDateRange(httpRequest);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Sale>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().ContainEquivalentOf("Invalid date format");
    }

    [Fact]
    public async Task GetSalesByDateRange_ScopedToCompany_PassesCorrectCompanyId()
    {
        var apiResponse = ApiResponse<List<Sale>>.SuccessResponse(new List<Sale>());

        _salesServiceMock
            .Setup(s => s.GetSalesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var query = new NameValueCollection
        {
            { "startDate", "2024-01-01" },
            { "endDate", "2024-01-31" }
        };
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders, query);

        await _functions.GetSalesByDateRange(httpRequest);

        _salesServiceMock.Verify(
            s => s.GetSalesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), CompanyId),
            Times.Once);
        _salesServiceMock.Verify(
            s => s.GetSalesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), OtherCompanyId),
            Times.Never);
    }

    #endregion

    #region GetSalesByCustomer Tests

    [Fact]
    public async Task GetSalesByCustomer_ValidRequest_Returns200WithCustomerSales()
    {
        var sales = new List<Sale> { CreateSale(1) };
        var apiResponse = ApiResponse<List<Sale>>.SuccessResponse(sales);

        _salesServiceMock
            .Setup(s => s.GetSalesByCustomerIdAsync(10, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetSalesByCustomer(httpRequest, 10);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Sale>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetSalesByCustomer_ScopedToCompany_PassesCorrectCompanyId()
    {
        var apiResponse = ApiResponse<List<Sale>>.SuccessResponse(new List<Sale>());

        _salesServiceMock
            .Setup(s => s.GetSalesByCustomerIdAsync(10, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        await _functions.GetSalesByCustomer(httpRequest, 10);

        _salesServiceMock.Verify(s => s.GetSalesByCustomerIdAsync(10, CompanyId), Times.Once);
        _salesServiceMock.Verify(s => s.GetSalesByCustomerIdAsync(10, OtherCompanyId), Times.Never);
    }

    [Fact]
    public async Task GetSalesByCustomer_MissingCompanyId_Returns401Unauthorized()
    {
        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.GetSalesByCustomer(httpRequest, 10);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Financial Field Regression Tests (bugs #82–#87)

    [Fact]
    public async Task CreateSale_Response_IncludesSubtotalTaxAndTotalFields()
    {
        var sale = new Sale
        {
            Id = 1,
            CompanyId = CompanyId,
            CustomerId = 10,
            PaymentMethod = "Cash",
            SaleDate = DateTime.UtcNow,
            Subtotal = 100m,
            Tax = 8m,
            Total = 108m,
            Items = new List<SaleItem>()
        };
        var apiResponse = ApiResponse<Sale>.SuccessResponse(sale);

        _salesServiceMock
            .Setup(s => s.CreateSaleAsync(It.IsAny<CreateSaleRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, CreateValidSaleRequest(), CompanyHeaders);

        var result = await _functions.CreateSale(httpRequest);

        result.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Sale>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Data!.Subtotal.Should().Be(100m, because: "subtotal field must not be lost or renamed");
        deserialized.Data.Tax.Should().Be(8m, because: "tax field must be present in response");
        deserialized.Data.Total.Should().Be(108m, because: "total field must not be confused with subtotal");
        deserialized.Data.Total.Should().Be(deserialized.Data.Subtotal + deserialized.Data.Tax,
            because: "total must equal subtotal + tax");
    }

    [Fact]
    public async Task CreateSale_ZeroTax_SubtotalEqualsTotal()
    {
        var sale = new Sale
        {
            Id = 1,
            CompanyId = CompanyId,
            CustomerId = 10,
            PaymentMethod = "Cash",
            SaleDate = DateTime.UtcNow,
            Subtotal = 50m,
            Tax = 0m,
            Total = 50m,
            Items = new List<SaleItem>()
        };
        var apiResponse = ApiResponse<Sale>.SuccessResponse(sale);

        _salesServiceMock
            .Setup(s => s.CreateSaleAsync(It.IsAny<CreateSaleRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, CreateValidSaleRequest(), CompanyHeaders);

        var result = await _functions.CreateSale(httpRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Sale>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Data!.Tax.Should().Be(0m, because: "tax field must be zero when no tax applied");
        deserialized.Data.Subtotal.Should().Be(deserialized.Data.Total,
            because: "when tax is zero, subtotal equals total");
    }

    [Fact]
    public async Task GetAllSales_SubtotalNotMappedToTotalAmount_ValueIsCorrect()
    {
        var sale = new Sale
        {
            Id = 1,
            CompanyId = CompanyId,
            CustomerId = 10,
            PaymentMethod = "Cash",
            SaleDate = DateTime.UtcNow,
            Subtotal = 75m,
            Tax = 6m,
            Total = 81m,
            Items = new List<SaleItem>()
        };
        var apiResponse = ApiResponse<List<Sale>>.SuccessResponse(new List<Sale> { sale });

        _salesServiceMock
            .Setup(s => s.GetAllSalesAsync(CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetAllSales(httpRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Sale>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var returnedSale = deserialized!.Data![0];
        returnedSale.Subtotal.Should().Be(75m, because: "subtotal must not be merged with total_amount column");
        returnedSale.Total.Should().Be(81m, because: "total must not be the same value as subtotal");
        returnedSale.Subtotal.Should().NotBe(returnedSale.Total, because: "subtotal and total must be distinct when tax > 0");
    }

    [Fact]
    public async Task GetSaleById_Response_IncludesSubtotalTaxAndTotalFields()
    {
        var sale = new Sale
        {
            Id = 1,
            CompanyId = CompanyId,
            CustomerId = 10,
            PaymentMethod = "Cash",
            SaleDate = DateTime.UtcNow,
            Subtotal = 200m,
            Tax = 16m,
            Total = 216m,
            Items = new List<SaleItem>
            {
                new SaleItem { Id = 1, SaleId = 1, InventoryItemId = 100, Quantity = 4, UnitPrice = 50m, TotalPrice = 200m }
            }
        };
        var apiResponse = ApiResponse<Sale>.SuccessResponse(sale);

        _salesServiceMock
            .Setup(s => s.GetSaleByIdAsync(1, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetSaleById(httpRequest, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Sale>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Data!.Subtotal.Should().Be(200m, because: "subtotal field must be returned correctly");
        deserialized.Data.Tax.Should().Be(16m, because: "tax field must be returned correctly");
        deserialized.Data.Total.Should().Be(216m, because: "total field must equal subtotal + tax");
        deserialized.Data.Items[0].TotalPrice.Should().Be(200m, because: "line item totalPrice must be present");
        deserialized.Data.Items[0].UnitPrice.Should().Be(50m, because: "line item unitPrice must be present");
    }

    [Fact]
    public async Task CreateSale_ResponseJson_ContainsCamelCaseFinancialFieldNames()
    {
        var apiResponse = ApiResponse<Sale>.SuccessResponse(CreateSale());

        _salesServiceMock
            .Setup(s => s.CreateSaleAsync(It.IsAny<CreateSaleRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, CreateValidSaleRequest(), CompanyHeaders);

        var result = await _functions.CreateSale(httpRequest);

        var body = await TestHelpers.ReadResponseBody(result);

        body.Should().Contain("\"subtotal\"", because: "subtotal must be camelCase in JSON");
        body.Should().Contain("\"tax\"", because: "tax must be camelCase in JSON");
        body.Should().Contain("\"total\"", because: "total must be camelCase in JSON");
        body.Should().NotContain("\"Subtotal\"", because: "PascalCase field names must not appear");
        body.Should().NotContain("\"Total\"", because: "PascalCase field names must not appear");
    }

    #endregion

    #region DeleteSale Tests

    [Fact]
    public async Task DeleteSale_ExistingSale_Returns200OK()
    {
        var apiResponse = ApiResponse<bool>.SuccessResponse(true, "Sale deleted successfully");

        _salesServiceMock
            .Setup(s => s.DeleteSaleAsync(1, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.DeleteSale(httpRequest, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteSale_NotFound_Returns404()
    {
        var apiResponse = ApiResponse<bool>.ErrorResponse("Sale not found");

        _salesServiceMock
            .Setup(s => s.DeleteSaleAsync(999, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.DeleteSale(httpRequest, 999);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteSale_MissingCompanyId_Returns401Unauthorized()
    {
        var context = new Mock<FunctionContext>();
        var httpRequest = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.DeleteSale(httpRequest, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion
}
