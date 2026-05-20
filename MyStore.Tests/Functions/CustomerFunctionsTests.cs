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

public class CustomerFunctionsTests
{
    private readonly Mock<ICustomerService> _customerServiceMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger<CustomerFunctions>> _loggerMock;
    private readonly CustomerFunctions _functions;

    private const int CompanyId = 42;
    private readonly IReadOnlyDictionary<string, string> _companyHeaders =
        new Dictionary<string, string> { { "X-Company-Id", "42" } };

    public CustomerFunctionsTests()
    {
        _customerServiceMock = new Mock<ICustomerService>();
        _loggerMock = new Mock<ILogger<CustomerFunctions>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();

        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        _functions = new CustomerFunctions(_customerServiceMock.Object, _loggerFactoryMock.Object);
    }

    private static Customer CreateCustomer(int id = 1, int companyId = CompanyId) => new Customer
    {
        Id = id,
        CompanyId = companyId,
        FirstName = "John",
        LastName = "Doe",
        Email = "john.doe@example.com",
        Phone = "555-1234",
        CreatedDate = DateTime.UtcNow
    };

    #region GetAllCustomers Tests

    [Fact]
    public async Task GetAllCustomers_ValidRequest_Returns200WithList()
    {
        var customers = new List<Customer> { CreateCustomer(1), CreateCustomer(2) };
        var apiResponse = ApiResponse<List<Customer>>.SuccessResponse(customers);

        _customerServiceMock
            .Setup(s => s.GetAllCustomersAsync(CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.GetAllCustomers(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Customer>>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllCustomers_ScopedToCompany_PassesCompanyIdToService()
    {
        _customerServiceMock
            .Setup(s => s.GetAllCustomersAsync(CompanyId))
            .ReturnsAsync(ApiResponse<List<Customer>>.SuccessResponse(new List<Customer>()));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        await _functions.GetAllCustomers(req);

        _customerServiceMock.Verify(s => s.GetAllCustomersAsync(CompanyId), Times.Once);
    }

    [Fact]
    public async Task GetAllCustomers_CompanyIdFromJwt_ReturnsSuccessWithoutHeader()
    {
        var customers = new List<Customer> { CreateCustomer(1) };
        _customerServiceMock
            .Setup(s => s.GetAllCustomersAsync(CompanyId))
            .ReturnsAsync(ApiResponse<List<Customer>>.SuccessResponse(customers));

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null);

        var result = await _functions.GetAllCustomers(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        _customerServiceMock.Verify(s => s.GetAllCustomersAsync(CompanyId), Times.Once);
    }

    [Fact]
    public async Task GetAllCustomers_ServiceException_PropagatesException()
    {
        _customerServiceMock
            .Setup(s => s.GetAllCustomersAsync(CompanyId))
            .ThrowsAsync(new Exception("DB error"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var act = async () => await _functions.GetAllCustomers(req);
        await act.Should().ThrowAsync<Exception>().WithMessage("DB error");
    }

    #endregion

    #region GetCustomerById Tests

    [Fact]
    public async Task GetCustomerById_ExistingCustomer_Returns200WithCustomer()
    {
        var customer = CreateCustomer(1);
        var apiResponse = ApiResponse<Customer>.SuccessResponse(customer);

        _customerServiceMock
            .Setup(s => s.GetCustomerByIdAsync(1, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.GetCustomerById(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Customer>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.Id.Should().Be(1);
        deserialized.Data.CompanyId.Should().Be(CompanyId);
    }

    [Fact]
    public async Task GetCustomerById_NotFound_Returns404()
    {
        var apiResponse = ApiResponse<Customer>.ErrorResponse("Customer not found");

        _customerServiceMock
            .Setup(s => s.GetCustomerByIdAsync(99, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.GetCustomerById(req, 99);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Customer>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task GetCustomerById_CustomerBelongsToDifferentCompany_Returns403Forbidden()
    {
        _customerServiceMock
            .Setup(s => s.GetCustomerByIdAsync(1, CompanyId))
            .ThrowsAsync(new UnauthorizedAccessException("Cross-tenant access denied"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.GetCustomerById(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Customer>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("Cross-tenant access denied");
    }

    [Fact]
    public async Task GetCustomerById_DifferentCompany_ServiceScopesQuery()
    {
        _customerServiceMock
            .Setup(s => s.GetCustomerByIdAsync(1, CompanyId))
            .ReturnsAsync(ApiResponse<Customer>.ErrorResponse("Customer not found"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        await _functions.GetCustomerById(req, 1);

        _customerServiceMock.Verify(s => s.GetCustomerByIdAsync(1, CompanyId), Times.Once);
        _customerServiceMock.Verify(s => s.GetCustomerByIdAsync(1, It.Is<int>(id => id != CompanyId)), Times.Never);
    }

    [Fact]
    public async Task GetCustomerById_CompanyIdFromJwt_ReturnsSuccessWithoutHeader()
    {
        var customer = CreateCustomer(1);
        _customerServiceMock
            .Setup(s => s.GetCustomerByIdAsync(1, CompanyId))
            .ReturnsAsync(ApiResponse<Customer>.SuccessResponse(customer));

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null);

        var result = await _functions.GetCustomerById(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        _customerServiceMock.Verify(s => s.GetCustomerByIdAsync(1, CompanyId), Times.Once);
    }

    [Fact]
    public async Task GetCustomerById_LogsInformation()
    {
        _customerServiceMock
            .Setup(s => s.GetCustomerByIdAsync(1, CompanyId))
            .ReturnsAsync(ApiResponse<Customer>.SuccessResponse(CreateCustomer(1)));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        await _functions.GetCustomerById(req, 1);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Getting customer with ID")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region CreateCustomer Tests

    [Fact]
    public async Task CreateCustomer_ValidRequest_Returns201Created()
    {
        var request = new CreateCustomerRequest { FirstName = "Jane", LastName = "Smith", Email = "jane@example.com" };
        var created = new Customer { Id = 10, CompanyId = CompanyId, FirstName = "Jane", LastName = "Smith", Email = "jane@example.com", CreatedDate = DateTime.UtcNow };
        var apiResponse = ApiResponse<Customer>.SuccessResponse(created);

        _customerServiceMock
            .Setup(s => s.CreateCustomerAsync(It.IsAny<CreateCustomerRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, request, _companyHeaders);

        var result = await _functions.CreateCustomer(req);

        result.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Customer>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.Id.Should().Be(10);
    }

    [Fact]
    public async Task CreateCustomer_ServiceValidationFailure_Returns400()
    {
        var request = new CreateCustomerRequest { FirstName = "", LastName = "" };
        var apiResponse = ApiResponse<Customer>.ErrorResponse("FirstName is required");

        _customerServiceMock
            .Setup(s => s.CreateCustomerAsync(It.IsAny<CreateCustomerRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, request, _companyHeaders);

        var result = await _functions.CreateCustomer(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Customer>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task CreateCustomer_NullJsonBody_Returns400()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestDataWithRawBody("null", _companyHeaders, context.Object);

        var result = await _functions.CreateCustomer(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Customer>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("Invalid request body");
    }

    [Fact]
    public async Task CreateCustomer_ScopedToCompany_PassesCompanyIdToService()
    {
        var request = new CreateCustomerRequest { FirstName = "Jane", LastName = "Smith" };

        _customerServiceMock
            .Setup(s => s.CreateCustomerAsync(It.IsAny<CreateCustomerRequest>(), CompanyId))
            .ReturnsAsync(ApiResponse<Customer>.SuccessResponse(CreateCustomer()));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, request, _companyHeaders);

        await _functions.CreateCustomer(req);

        _customerServiceMock.Verify(s => s.CreateCustomerAsync(It.IsAny<CreateCustomerRequest>(), CompanyId), Times.Once);
    }

    [Fact]
    public async Task CreateCustomer_CompanyIdFromJwt_ReturnsSuccessWithoutHeader()
    {
        var request = new CreateCustomerRequest { FirstName = "Jane", LastName = "Smith", Email = "jane@example.com" };
        var created = new Customer { Id = 10, CompanyId = CompanyId, FirstName = "Jane", LastName = "Smith", Email = "jane@example.com", CreatedDate = DateTime.UtcNow };
        _customerServiceMock
            .Setup(s => s.CreateCustomerAsync(It.IsAny<CreateCustomerRequest>(), CompanyId))
            .ReturnsAsync(ApiResponse<Customer>.SuccessResponse(created));

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, request);

        var result = await _functions.CreateCustomer(req);

        result.StatusCode.Should().Be(HttpStatusCode.Created);
        _customerServiceMock.Verify(s => s.CreateCustomerAsync(It.IsAny<CreateCustomerRequest>(), CompanyId), Times.Once);
    }

    [Fact]
    public async Task CreateCustomer_ServiceException_PropagatesException()
    {
        var request = new CreateCustomerRequest { FirstName = "Jane", LastName = "Smith" };

        _customerServiceMock
            .Setup(s => s.CreateCustomerAsync(It.IsAny<CreateCustomerRequest>(), CompanyId))
            .ThrowsAsync(new Exception("DB error"));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, request, _companyHeaders);

        var act = async () => await _functions.CreateCustomer(req);
        await act.Should().ThrowAsync<Exception>().WithMessage("DB error");
    }

    #endregion

    #region UpdateCustomer Tests

    [Fact]
    public async Task UpdateCustomer_ValidRequest_Returns200OK()
    {
        var request = new UpdateCustomerRequest { FirstName = "Updated", LastName = "Name" };
        var updated = CreateCustomer(1);
        updated.FirstName = "Updated";
        var apiResponse = ApiResponse<Customer>.SuccessResponse(updated);

        _customerServiceMock
            .Setup(s => s.UpdateCustomerAsync(1, It.IsAny<UpdateCustomerRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, request, _companyHeaders);

        var result = await _functions.UpdateCustomer(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Customer>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateCustomer_NotFound_Returns404()
    {
        var request = new UpdateCustomerRequest { FirstName = "Updated" };
        var apiResponse = ApiResponse<Customer>.ErrorResponse("Customer not found");

        _customerServiceMock
            .Setup(s => s.UpdateCustomerAsync(99, It.IsAny<UpdateCustomerRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, request, _companyHeaders);

        var result = await _functions.UpdateCustomer(req, 99);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Customer>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateCustomer_NullJsonBody_Returns400()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestDataWithRawBody("null", _companyHeaders, context.Object);

        var result = await _functions.UpdateCustomer(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Customer>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("Invalid request body");
    }

    [Fact]
    public async Task UpdateCustomer_ScopedToCompany_PassesCompanyIdToService()
    {
        var request = new UpdateCustomerRequest { FirstName = "Updated" };

        _customerServiceMock
            .Setup(s => s.UpdateCustomerAsync(1, It.IsAny<UpdateCustomerRequest>(), CompanyId))
            .ReturnsAsync(ApiResponse<Customer>.SuccessResponse(CreateCustomer()));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, request, _companyHeaders);

        await _functions.UpdateCustomer(req, 1);

        _customerServiceMock.Verify(s => s.UpdateCustomerAsync(1, It.IsAny<UpdateCustomerRequest>(), CompanyId), Times.Once);
    }

    [Fact]
    public async Task UpdateCustomer_CompanyIdFromJwt_ReturnsSuccessWithoutHeader()
    {
        var request = new UpdateCustomerRequest { FirstName = "Updated" };
        var updated = CreateCustomer(1);
        updated.FirstName = "Updated";
        _customerServiceMock
            .Setup(s => s.UpdateCustomerAsync(1, It.IsAny<UpdateCustomerRequest>(), CompanyId))
            .ReturnsAsync(ApiResponse<Customer>.SuccessResponse(updated));

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, request);

        var result = await _functions.UpdateCustomer(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        _customerServiceMock.Verify(s => s.UpdateCustomerAsync(1, It.IsAny<UpdateCustomerRequest>(), CompanyId), Times.Once);
    }

    #endregion

    #region DeleteCustomer Tests

    [Fact]
    public async Task DeleteCustomer_ExistingCustomer_Returns204NoContent()
    {
        var apiResponse = ApiResponse<bool>.SuccessResponse(true);

        _customerServiceMock
            .Setup(s => s.DeleteCustomerAsync(1, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.DeleteCustomer(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteCustomer_NotFound_Returns404()
    {
        var apiResponse = ApiResponse<bool>.ErrorResponse("Customer not found");

        _customerServiceMock
            .Setup(s => s.DeleteCustomerAsync(99, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.DeleteCustomer(req, 99);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<bool>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteCustomer_ScopedToCompany_PassesCompanyIdToService()
    {
        _customerServiceMock
            .Setup(s => s.DeleteCustomerAsync(1, CompanyId))
            .ReturnsAsync(ApiResponse<bool>.SuccessResponse(true));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        await _functions.DeleteCustomer(req, 1);

        _customerServiceMock.Verify(s => s.DeleteCustomerAsync(1, CompanyId), Times.Once);
        _customerServiceMock.Verify(s => s.DeleteCustomerAsync(1, It.Is<int>(id => id != CompanyId)), Times.Never);
    }

    [Fact]
    public async Task DeleteCustomer_CompanyIdFromJwt_ReturnsSuccessWithoutHeader()
    {
        _customerServiceMock
            .Setup(s => s.DeleteCustomerAsync(1, CompanyId))
            .ReturnsAsync(ApiResponse<bool>.SuccessResponse(true));

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null);

        var result = await _functions.DeleteCustomer(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _customerServiceMock.Verify(s => s.DeleteCustomerAsync(1, CompanyId), Times.Once);
    }

    [Fact]
    public async Task DeleteCustomer_LogsInformation()
    {
        _customerServiceMock
            .Setup(s => s.DeleteCustomerAsync(1, CompanyId))
            .ReturnsAsync(ApiResponse<bool>.SuccessResponse(true));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        await _functions.DeleteCustomer(req, 1);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Deleting customer with ID")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region SearchCustomers Tests

    [Fact]
    public async Task SearchCustomers_ValidQuery_Returns200WithResults()
    {
        var customers = new List<Customer> { CreateCustomer(1) };
        var apiResponse = ApiResponse<List<Customer>>.SuccessResponse(customers);

        _customerServiceMock
            .Setup(s => s.SearchCustomersAsync("john", CompanyId))
            .ReturnsAsync(apiResponse);

        var query = new NameValueCollection { { "q", "john" } };
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders, query);

        var result = await _functions.SearchCustomers(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Customer>>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().HaveCount(1);
    }

    [Fact]
    public async Task SearchCustomers_MissingQueryParam_Returns400()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.SearchCustomers(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Customer>>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("required");
    }

    [Fact]
    public async Task SearchCustomers_ScopedToCompany_PassesCompanyIdToService()
    {
        _customerServiceMock
            .Setup(s => s.SearchCustomersAsync("jane", CompanyId))
            .ReturnsAsync(ApiResponse<List<Customer>>.SuccessResponse(new List<Customer>()));

        var query = new NameValueCollection { { "q", "jane" } };
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders, query);

        await _functions.SearchCustomers(req);

        _customerServiceMock.Verify(s => s.SearchCustomersAsync("jane", CompanyId), Times.Once);
    }

    [Fact]
    public async Task SearchCustomers_CompanyIdFromJwt_ReturnsSuccessWithoutHeader()
    {
        var customers = new List<Customer> { CreateCustomer(1) };
        _customerServiceMock
            .Setup(s => s.SearchCustomersAsync("john", CompanyId))
            .ReturnsAsync(ApiResponse<List<Customer>>.SuccessResponse(customers));

        var query = new NameValueCollection { { "q", "john" } };
        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, null, query);

        var result = await _functions.SearchCustomers(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        _customerServiceMock.Verify(s => s.SearchCustomersAsync("john", CompanyId), Times.Once);
    }

    #endregion

    #region Response Shape Tests

    [Fact]
    public async Task GetAllCustomers_ResponseUsesCamelCase()
    {
        _customerServiceMock
            .Setup(s => s.GetAllCustomersAsync(CompanyId))
            .ReturnsAsync(ApiResponse<List<Customer>>.SuccessResponse(new List<Customer> { CreateCustomer() }));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.GetAllCustomers(req);

        var body = await TestHelpers.ReadResponseBody(result);
        body.Should().Contain("success");
        body.Should().Contain("data");
        body.Should().NotContain("\"Success\"");
        body.Should().NotContain("\"Data\"");
    }

    [Fact]
    public async Task GetAllCustomers_ResponseHasCorrectContentType()
    {
        _customerServiceMock
            .Setup(s => s.GetAllCustomersAsync(CompanyId))
            .ReturnsAsync(ApiResponse<List<Customer>>.SuccessResponse(new List<Customer>()));

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, _companyHeaders);

        var result = await _functions.GetAllCustomers(req);

        result.Headers.Should().ContainKey("Content-Type");
        result.Headers.GetValues("Content-Type").Should().Contain("application/json; charset=utf-8");
    }

    #endregion
}
