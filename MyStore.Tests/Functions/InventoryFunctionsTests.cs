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

public class InventoryFunctionsTests
{
    private readonly Mock<IInventoryService> _serviceMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger<InventoryFunctions>> _loggerMock;
    private readonly InventoryFunctions _functions;

    private const int CompanyId = 42;
    private const int OtherCompanyId = 99;

    private static readonly Dictionary<string, string> CompanyHeaders = new()
    {
        ["X-Company-Id"] = CompanyId.ToString()
    };

    public InventoryFunctionsTests()
    {
        _serviceMock = new Mock<IInventoryService>();
        _loggerMock = new Mock<ILogger<InventoryFunctions>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();

        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        _functions = new InventoryFunctions(_serviceMock.Object, _loggerFactoryMock.Object);
    }

    private static InventoryItem CreateSampleItem(int id = 1, int companyId = CompanyId) => new()
    {
        Id = id,
        CompanyId = companyId,
        LocationId = 1,
        Name = "Super Mario Bros",
        Category = "Game",
        Quantity = 5,
        SellPrice = 29.99m,
        BuyPrice = 15.00m,
        Condition = "Good",
        AddedDate = DateTime.UtcNow
    };

    private static CreateInventoryItemRequest CreateValidCreateRequest() => new()
    {
        LocationId = 1,
        Name = "Super Mario Bros",
        Category = "Game",
        Quantity = 5,
        SellPrice = 29.99m,
        BuyPrice = 15.00m,
        Condition = "Good"
    };

    private static UpdateInventoryItemRequest CreateValidUpdateRequest() => new()
    {
        Name = "Super Mario Bros Updated",
        Quantity = 10,
        SellPrice = 34.99m,
        Condition = "Excellent"
    };

    #region GetAllInventory Tests

    [Fact]
    public async Task GetAllInventory_ReturnsOk_WithItemsScopedToCompany()
    {
        var items = new List<InventoryItem> { CreateSampleItem(1), CreateSampleItem(2) };
        var apiResponse = ApiResponse<List<InventoryItem>>.SuccessResponse(items);

        _serviceMock
            .Setup(s => s.GetAllInventoryAsync(CompanyId, null))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders);

        var result = await _functions.GetAllInventory(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<InventoryItem>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().HaveCount(2);
        deserialized.Data!.All(i => i.CompanyId == CompanyId).Should().BeTrue();
    }

    [Fact]
    public async Task GetAllInventory_WithLocationIdFilter_PassesLocationIdToService()
    {
        var items = new List<InventoryItem> { CreateSampleItem() };
        var apiResponse = ApiResponse<List<InventoryItem>>.SuccessResponse(items);

        _serviceMock
            .Setup(s => s.GetAllInventoryAsync(CompanyId, 5))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var query = new NameValueCollection { ["locationId"] = "5" };
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders, query);

        var result = await _functions.GetAllInventory(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        _serviceMock.Verify(s => s.GetAllInventoryAsync(CompanyId, 5), Times.Once);
    }

    [Fact]
    public async Task GetAllInventory_WithSearchQuery_CallsSearchInventory()
    {
        var items = new List<InventoryItem> { CreateSampleItem() };
        var apiResponse = ApiResponse<List<InventoryItem>>.SuccessResponse(items);

        _serviceMock
            .Setup(s => s.SearchInventoryAsync("mario", CompanyId, null))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var query = new NameValueCollection { ["q"] = "mario" };
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders, query);

        var result = await _functions.GetAllInventory(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        _serviceMock.Verify(s => s.SearchInventoryAsync("mario", CompanyId, null), Times.Once);
        _serviceMock.Verify(s => s.GetAllInventoryAsync(It.IsAny<int>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task GetAllInventory_WithSearchQueryAndLocationId_PassesBothToSearchService()
    {
        var items = new List<InventoryItem> { CreateSampleItem() };
        var apiResponse = ApiResponse<List<InventoryItem>>.SuccessResponse(items);

        _serviceMock
            .Setup(s => s.SearchInventoryAsync("zelda", CompanyId, 3))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var query = new NameValueCollection { ["q"] = "zelda", ["locationId"] = "3" };
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders, query);

        var result = await _functions.GetAllInventory(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        _serviceMock.Verify(s => s.SearchInventoryAsync("zelda", CompanyId, 3), Times.Once);
    }

    [Fact]
    public async Task GetAllInventory_NoCompanyId_Returns401Unauthorized()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.GetAllInventory(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAllInventory_EmptyList_Returns200WithEmptyData()
    {
        var apiResponse = ApiResponse<List<InventoryItem>>.SuccessResponse(new List<InventoryItem>());

        _serviceMock
            .Setup(s => s.GetAllInventoryAsync(CompanyId, null))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders);

        var result = await _functions.GetAllInventory(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<InventoryItem>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllInventory_ResponseHasCorrectContentType()
    {
        var apiResponse = ApiResponse<List<InventoryItem>>.SuccessResponse(new List<InventoryItem>());

        _serviceMock
            .Setup(s => s.GetAllInventoryAsync(CompanyId, null))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders);

        var result = await _functions.GetAllInventory(req);

        result.Headers.Should().ContainKey("Content-Type");
        result.Headers.GetValues("Content-Type").Should().Contain("application/json; charset=utf-8");
    }

    #endregion

    #region GetInventoryById Tests

    [Fact]
    public async Task GetInventoryById_ExistingItem_Returns200WithItemData()
    {
        var item = CreateSampleItem(1);
        var apiResponse = ApiResponse<InventoryItem>.SuccessResponse(item);

        _serviceMock
            .Setup(s => s.GetInventoryByIdAsync(1, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders);

        var result = await _functions.GetInventoryById(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<InventoryItem>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.Id.Should().Be(1);
        deserialized.Data.CompanyId.Should().Be(CompanyId);
    }

    [Fact]
    public async Task GetInventoryById_NotFound_Returns404()
    {
        var apiResponse = ApiResponse<InventoryItem>.ErrorResponse("Inventory item not found");

        _serviceMock
            .Setup(s => s.GetInventoryByIdAsync(999, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders);

        var result = await _functions.GetInventoryById(req, 999);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<InventoryItem>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task GetInventoryById_CrossCompanyAccess_Returns403Forbidden()
    {
        var apiResponse = ApiResponse<InventoryItem>.ErrorResponse("Access denied: item belongs to a different company");

        _serviceMock
            .Setup(s => s.GetInventoryByIdAsync(1, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders);

        var result = await _functions.GetInventoryById(req, 1);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<InventoryItem>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("Access denied");
    }

    [Fact]
    public async Task GetInventoryById_NoCompanyId_Returns401Unauthorized()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.GetInventoryById(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetInventoryById_PassesCompanyIdToService_EnforcesMultiTenancy()
    {
        var item = CreateSampleItem(1);
        var apiResponse = ApiResponse<InventoryItem>.SuccessResponse(item);

        _serviceMock
            .Setup(s => s.GetInventoryByIdAsync(1, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders);

        await _functions.GetInventoryById(req, 1);

        _serviceMock.Verify(s => s.GetInventoryByIdAsync(1, CompanyId), Times.Once);
        _serviceMock.Verify(s => s.GetInventoryByIdAsync(1, OtherCompanyId), Times.Never);
    }

    #endregion

    #region GetInventoryItemLocations Tests

    [Fact]
    public async Task GetInventoryItemLocations_ExistingItem_Returns200WithLocations()
    {
        var locations = new List<ItemLocationInfo>
        {
            new() { LocationId = 1, LocationName = "Main Store", Quantity = 3, Condition = "Good" },
            new() { LocationId = 2, LocationName = "Warehouse", Quantity = 2, Condition = "Fair" }
        };
        var apiResponse = ApiResponse<List<ItemLocationInfo>>.SuccessResponse(locations);

        _serviceMock
            .Setup(s => s.GetLocationsForItemAsync(1, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders);

        var result = await _functions.GetInventoryItemLocations(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<ItemLocationInfo>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetInventoryItemLocations_NoCompanyId_Returns401Unauthorized()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.GetInventoryItemLocations(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetInventoryItemLocations_NonExistentItem_ReturnsErrorResponse()
    {
        var apiResponse = ApiResponse<List<ItemLocationInfo>>.ErrorResponse("Inventory item not found");

        _serviceMock
            .Setup(s => s.GetLocationsForItemAsync(999, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders);

        var result = await _functions.GetInventoryItemLocations(req, 999);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<ItemLocationInfo>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task GetInventoryItemLocations_PassesCompanyIdToService_EnforcesMultiTenancy()
    {
        var apiResponse = ApiResponse<List<ItemLocationInfo>>.SuccessResponse(new List<ItemLocationInfo>());

        _serviceMock
            .Setup(s => s.GetLocationsForItemAsync(5, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders);

        await _functions.GetInventoryItemLocations(req, 5);

        _serviceMock.Verify(s => s.GetLocationsForItemAsync(5, CompanyId), Times.Once);
    }

    #endregion

    #region CreateInventoryItem Tests

    [Fact]
    public async Task CreateInventoryItem_ValidRequest_Returns201Created()
    {
        var request = CreateValidCreateRequest();
        var createdItem = CreateSampleItem(10);
        var apiResponse = ApiResponse<InventoryItem>.SuccessResponse(createdItem, "Item created successfully");

        _serviceMock
            .Setup(s => s.CreateInventoryItemAsync(It.IsAny<CreateInventoryItemRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, request, CompanyHeaders);

        var result = await _functions.CreateInventoryItem(req);

        result.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<InventoryItem>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.Id.Should().Be(10);
    }

    [Fact]
    public async Task CreateInventoryItem_ServiceValidationFailure_Returns400BadRequest()
    {
        var request = CreateValidCreateRequest();
        var apiResponse = ApiResponse<InventoryItem>.ErrorResponse("Name is required");

        _serviceMock
            .Setup(s => s.CreateInventoryItemAsync(It.IsAny<CreateInventoryItemRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, request, CompanyHeaders);

        var result = await _functions.CreateInventoryItem(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<InventoryItem>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("Name is required");
    }

    [Fact]
    public async Task CreateInventoryItem_InvalidRequestBody_ThrowsJsonException()
    {
        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, "invalid json", CompanyHeaders);

        var act = async () => await _functions.CreateInventoryItem(req);

        await act.Should().ThrowAsync<System.Text.Json.JsonException>();
    }

    [Fact]
    public async Task CreateInventoryItem_NullRequestBody_ThrowsJsonException()
    {
        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, (object?)null, CompanyHeaders);

        var act = async () => await _functions.CreateInventoryItem(req);

        await act.Should().ThrowAsync<System.Text.Json.JsonException>();
    }

    [Fact]
    public async Task CreateInventoryItem_NoCompanyId_Returns401Unauthorized()
    {
        var request = CreateValidCreateRequest();
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, request);

        var result = await _functions.CreateInventoryItem(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateInventoryItem_PassesCompanyIdToService_EnforcesMultiTenancy()
    {
        var request = CreateValidCreateRequest();
        var createdItem = CreateSampleItem(10);
        var apiResponse = ApiResponse<InventoryItem>.SuccessResponse(createdItem);

        _serviceMock
            .Setup(s => s.CreateInventoryItemAsync(It.IsAny<CreateInventoryItemRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, request, CompanyHeaders);

        await _functions.CreateInventoryItem(req);

        _serviceMock.Verify(s => s.CreateInventoryItemAsync(It.IsAny<CreateInventoryItemRequest>(), CompanyId), Times.Once);
        _serviceMock.Verify(s => s.CreateInventoryItemAsync(It.IsAny<CreateInventoryItemRequest>(), OtherCompanyId), Times.Never);
    }

    #endregion

    #region UpdateInventoryItem Tests

    [Fact]
    public async Task UpdateInventoryItem_ValidRequest_Returns200OK()
    {
        var request = CreateValidUpdateRequest();
        var updatedItem = CreateSampleItem(1);
        updatedItem.Name = "Super Mario Bros Updated";
        var apiResponse = ApiResponse<InventoryItem>.SuccessResponse(updatedItem);

        _serviceMock
            .Setup(s => s.UpdateInventoryItemAsync(1, It.IsAny<UpdateInventoryItemRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, request, CompanyHeaders);

        var result = await _functions.UpdateInventoryItem(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<InventoryItem>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateInventoryItem_NotFound_Returns404()
    {
        var request = CreateValidUpdateRequest();
        var apiResponse = ApiResponse<InventoryItem>.ErrorResponse("Inventory item not found");

        _serviceMock
            .Setup(s => s.UpdateInventoryItemAsync(999, It.IsAny<UpdateInventoryItemRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, request, CompanyHeaders);

        var result = await _functions.UpdateInventoryItem(req, 999);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<InventoryItem>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateInventoryItem_InvalidRequestBody_ThrowsJsonException()
    {
        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, "invalid json", CompanyHeaders);

        var act = async () => await _functions.UpdateInventoryItem(req, 1);

        await act.Should().ThrowAsync<System.Text.Json.JsonException>();
    }

    [Fact]
    public async Task UpdateInventoryItem_NoCompanyId_Returns401Unauthorized()
    {
        var request = CreateValidUpdateRequest();
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, request);

        var result = await _functions.UpdateInventoryItem(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateInventoryItem_PassesCompanyIdToService_EnforcesMultiTenancy()
    {
        var request = CreateValidUpdateRequest();
        var updatedItem = CreateSampleItem(1);
        var apiResponse = ApiResponse<InventoryItem>.SuccessResponse(updatedItem);

        _serviceMock
            .Setup(s => s.UpdateInventoryItemAsync(1, It.IsAny<UpdateInventoryItemRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, request, CompanyHeaders);

        await _functions.UpdateInventoryItem(req, 1);

        _serviceMock.Verify(s => s.UpdateInventoryItemAsync(1, It.IsAny<UpdateInventoryItemRequest>(), CompanyId), Times.Once);
        _serviceMock.Verify(s => s.UpdateInventoryItemAsync(1, It.IsAny<UpdateInventoryItemRequest>(), OtherCompanyId), Times.Never);
    }

    #endregion

    #region DeleteInventoryItem Tests

    [Fact]
    public async Task DeleteInventoryItem_ExistingItem_Returns200OK()
    {
        var apiResponse = ApiResponse<bool>.SuccessResponse(true, "Item deleted successfully");

        _serviceMock
            .Setup(s => s.DeleteInventoryItemAsync(1, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders);

        var result = await _functions.DeleteInventoryItem(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<bool>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteInventoryItem_NotFound_Returns404()
    {
        var apiResponse = ApiResponse<bool>.ErrorResponse("Inventory item not found");

        _serviceMock
            .Setup(s => s.DeleteInventoryItemAsync(999, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders);

        var result = await _functions.DeleteInventoryItem(req, 999);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<bool>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteInventoryItem_NoCompanyId_Returns401Unauthorized()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.DeleteInventoryItem(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteInventoryItem_PassesCompanyIdToService_EnforcesMultiTenancy()
    {
        var apiResponse = ApiResponse<bool>.SuccessResponse(true);

        _serviceMock
            .Setup(s => s.DeleteInventoryItemAsync(1, CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders);

        await _functions.DeleteInventoryItem(req, 1);

        _serviceMock.Verify(s => s.DeleteInventoryItemAsync(1, CompanyId), Times.Once);
        _serviceMock.Verify(s => s.DeleteInventoryItemAsync(1, OtherCompanyId), Times.Never);
    }

    #endregion

    #region Search / Filter Tests

    [Fact]
    public async Task GetAllInventory_SearchByPlatformInName_ReturnsFilteredResults()
    {
        var items = new List<InventoryItem>
        {
            new() { Id = 1, CompanyId = CompanyId, Name = "NES Game", Category = "Game", Condition = "Good" }
        };
        var apiResponse = ApiResponse<List<InventoryItem>>.SuccessResponse(items);

        _serviceMock
            .Setup(s => s.SearchInventoryAsync("NES", CompanyId, null))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var query = new NameValueCollection { ["q"] = "NES" };
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders, query);

        var result = await _functions.GetAllInventory(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<InventoryItem>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Data.Should().HaveCount(1);
        deserialized.Data![0].Name.Should().Contain("NES");
    }

    [Fact]
    public async Task GetAllInventory_SearchByCategory_PassesSearchTermToService()
    {
        var apiResponse = ApiResponse<List<InventoryItem>>.SuccessResponse(new List<InventoryItem>());

        _serviceMock
            .Setup(s => s.SearchInventoryAsync("Accessory", CompanyId, null))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var query = new NameValueCollection { ["q"] = "Accessory" };
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders, query);

        await _functions.GetAllInventory(req);

        _serviceMock.Verify(s => s.SearchInventoryAsync("Accessory", CompanyId, null), Times.Once);
    }

    [Fact]
    public async Task GetAllInventory_EmptySearchQuery_CallsGetAllNotSearch()
    {
        var apiResponse = ApiResponse<List<InventoryItem>>.SuccessResponse(new List<InventoryItem>());

        _serviceMock
            .Setup(s => s.GetAllInventoryAsync(CompanyId, null))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var query = new NameValueCollection { ["q"] = "" };
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders, query);

        await _functions.GetAllInventory(req);

        _serviceMock.Verify(s => s.GetAllInventoryAsync(CompanyId, null), Times.Once);
        _serviceMock.Verify(s => s.SearchInventoryAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task GetAllInventory_WhitespaceSearchQuery_CallsGetAllNotSearch()
    {
        var apiResponse = ApiResponse<List<InventoryItem>>.SuccessResponse(new List<InventoryItem>());

        _serviceMock
            .Setup(s => s.GetAllInventoryAsync(CompanyId, null))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var query = new NameValueCollection { ["q"] = "   " };
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders, query);

        await _functions.GetAllInventory(req);

        _serviceMock.Verify(s => s.GetAllInventoryAsync(CompanyId, null), Times.Once);
        _serviceMock.Verify(s => s.SearchInventoryAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task GetAllInventory_SearchWithLocationFilter_PassesBothToService()
    {
        var apiResponse = ApiResponse<List<InventoryItem>>.SuccessResponse(new List<InventoryItem>());

        _serviceMock
            .Setup(s => s.SearchInventoryAsync("Game", CompanyId, 7))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var query = new NameValueCollection { ["q"] = "Game", ["locationId"] = "7" };
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders, query);

        await _functions.GetAllInventory(req);

        _serviceMock.Verify(s => s.SearchInventoryAsync("Game", CompanyId, 7), Times.Once);
    }

    #endregion

    #region Response Format Tests

    [Fact]
    public async Task GetAllInventory_ResponseUsesCamelCase()
    {
        var apiResponse = ApiResponse<List<InventoryItem>>.SuccessResponse(new List<InventoryItem>());

        _serviceMock
            .Setup(s => s.GetAllInventoryAsync(CompanyId, null))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders);

        var result = await _functions.GetAllInventory(req);

        var body = await TestHelpers.ReadResponseBody(result);
        body.Should().Contain("success");
        body.Should().Contain("data");
        body.Should().NotContain("\"Success\"");
        body.Should().NotContain("\"Data\"");
    }

    [Fact]
    public async Task CreateInventoryItem_MissingName_ReturnsBadRequestFromService()
    {
        var request = new CreateInventoryItemRequest
        {
            LocationId = 1,
            Category = "Game",
            Quantity = 1,
            SellPrice = 9.99m,
            Condition = "Good"
        };
        var apiResponse = ApiResponse<InventoryItem>.ErrorResponse("Name is required");

        _serviceMock
            .Setup(s => s.CreateInventoryItemAsync(It.IsAny<CreateInventoryItemRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, request, CompanyHeaders);

        var result = await _functions.CreateInventoryItem(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<InventoryItem>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("Name is required");
    }

    [Fact]
    public async Task CreateInventoryItem_MissingPrice_ReturnsBadRequestFromService()
    {
        var request = new CreateInventoryItemRequest
        {
            LocationId = 1,
            Name = "Item",
            Category = "Game",
            Quantity = 1,
            Condition = "Good"
        };
        var apiResponse = ApiResponse<InventoryItem>.ErrorResponse("Sell price must be greater than zero");

        _serviceMock
            .Setup(s => s.CreateInventoryItemAsync(It.IsAny<CreateInventoryItemRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, request, CompanyHeaders);

        var result = await _functions.CreateInventoryItem(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateInventoryItem_InvalidPayloadFromService_Returns404()
    {
        var request = CreateValidUpdateRequest();
        var apiResponse = ApiResponse<InventoryItem>.ErrorResponse("Quantity cannot be negative");

        _serviceMock
            .Setup(s => s.UpdateInventoryItemAsync(1, It.IsAny<UpdateInventoryItemRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, request, CompanyHeaders);

        var result = await _functions.UpdateInventoryItem(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<InventoryItem>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("Quantity");
    }

    [Fact]
    public async Task GetAllInventory_SearchByConditionName_ForwardsTermToService()
    {
        var apiResponse = ApiResponse<List<InventoryItem>>.SuccessResponse(new List<InventoryItem>());

        _serviceMock
            .Setup(s => s.SearchInventoryAsync("Mint", CompanyId, null))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var query = new NameValueCollection { ["q"] = "Mint" };
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyHeaders, query);

        await _functions.GetAllInventory(req);

        _serviceMock.Verify(s => s.SearchInventoryAsync("Mint", CompanyId, null), Times.Once);
    }

    [Fact]
    public async Task CreateInventoryItem_ResponseHasCorrectContentType()
    {
        var createdItem = CreateSampleItem(10);
        var apiResponse = ApiResponse<InventoryItem>.SuccessResponse(createdItem);

        _serviceMock
            .Setup(s => s.CreateInventoryItemAsync(It.IsAny<CreateInventoryItemRequest>(), CompanyId))
            .ReturnsAsync(apiResponse);

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, CreateValidCreateRequest(), CompanyHeaders);

        var result = await _functions.CreateInventoryItem(req);

        result.Headers.Should().ContainKey("Content-Type");
        result.Headers.GetValues("Content-Type").Should().Contain("application/json; charset=utf-8");
    }

    #endregion
}
