using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Moq;
using MyStore.Functions;
using MyStore.Functions.Attributes;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Services;
using MyStore.Tests.Helpers;
using Xunit;

namespace MyStore.Tests.Functions;

public class PromotionFunctionsTests
{
    private readonly Mock<IPromotionService> _promotionServiceMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger<PromotionFunctions>> _loggerMock;
    private readonly PromotionFunctions _functions;

    private const int CompanyId = 42;
    private readonly IReadOnlyDictionary<string, string> _companyHeaders =
        new Dictionary<string, string> { { "X-Company-Id", "42" } };

    public PromotionFunctionsTests()
    {
        _promotionServiceMock = new Mock<IPromotionService>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _loggerMock = new Mock<ILogger<PromotionFunctions>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();

        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        _functions = new PromotionFunctions(_promotionServiceMock.Object, _userRepositoryMock.Object, _loggerFactoryMock.Object);
    }

    private static Promotion CreatePromotion(int id = 1) => new Promotion
    {
        Id = id,
        CompanyId = CompanyId,
        Name = "Spring Sale",
        Type = "percentage",
        DiscountPercent = 10m,
        Scope = "store_wide",
        StartDate = DateTime.UtcNow.AddDays(-1),
        IsActive = true,
        CreatedAt = DateTime.UtcNow
    };

    #region GetAllPromotions

    [Fact]
    public async Task GetAllPromotions_ValidRequest_Returns200WithList()
    {
        var promotions = new List<Promotion> { CreatePromotion(1), CreatePromotion(2) };
        _promotionServiceMock
            .Setup(s => s.GetAllAsync(CompanyId))
            .ReturnsAsync(ApiResponse<List<Promotion>>.SuccessResponse(promotions));

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, _companyHeaders);

        var result = await _functions.GetAllPromotions(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Promotion>>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllPromotions_MissingCompanyHeader_Returns401()
    {
        var req = TestHelpers.CreateHttpRequestData(new Mock<FunctionContext>().Object);

        var result = await _functions.GetAllPromotions(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region GetActivePromotions

    [Fact]
    public async Task GetActivePromotions_ValidRequest_Returns200WithActiveList()
    {
        var promotions = new List<Promotion> { CreatePromotion(1) };
        _promotionServiceMock
            .Setup(s => s.GetActivePromotionsAsync(CompanyId))
            .ReturnsAsync(ApiResponse<List<Promotion>>.SuccessResponse(promotions));

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, _companyHeaders);

        var result = await _functions.GetActivePromotions(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Promotion>>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetActivePromotions_MissingCompanyHeader_Returns401()
    {
        var req = TestHelpers.CreateHttpRequestData(new Mock<FunctionContext>().Object);

        var result = await _functions.GetActivePromotions(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region CreatePromotion

    [Fact]
    public async Task CreatePromotion_ValidRequest_Returns201WithPromotion()
    {
        var createRequest = new CreatePromotionRequest
        {
            Name = "Spring Sale",
            Type = "percentage",
            DiscountPercent = 10m,
            Scope = "store_wide",
            StartDate = DateTime.UtcNow,
            IsActive = true
        };
        var promotion = CreatePromotion(1);
        _promotionServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<CreatePromotionRequest>(), CompanyId))
            .ReturnsAsync(ApiResponse<Promotion>.SuccessResponse(promotion));

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, createRequest, _companyHeaders);

        var result = await _functions.CreatePromotion(req);

        result.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Promotion>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.Name.Should().Be("Spring Sale");
    }

    [Fact]
    public async Task CreatePromotion_MissingName_Returns400()
    {
        var createRequest = new CreatePromotionRequest
        {
            Name = "",
            Type = "percentage",
            Scope = "store_wide",
            StartDate = DateTime.UtcNow
        };

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, createRequest, _companyHeaders);

        var result = await _functions.CreatePromotion(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreatePromotion_MissingCompanyHeader_Returns401()
    {
        var req = TestHelpers.CreateHttpRequestData(new Mock<FunctionContext>().Object);

        var result = await _functions.CreatePromotion(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreatePromotion_MissingType_Returns400()
    {
        var createRequest = new CreatePromotionRequest
        {
            Name = "Spring Sale",
            Type = "",
            Scope = "store_wide",
            StartDate = DateTime.UtcNow
        };

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, createRequest, _companyHeaders);

        var result = await _functions.CreatePromotion(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreatePromotion_MissingScope_Returns400()
    {
        var createRequest = new CreatePromotionRequest
        {
            Name = "Spring Sale",
            Type = "percentage",
            Scope = "",
            StartDate = DateTime.UtcNow
        };

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, createRequest, _companyHeaders);

        var result = await _functions.CreatePromotion(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreatePromotion_MissingStartDate_Returns400()
    {
        var createRequest = new CreatePromotionRequest
        {
            Name = "Spring Sale",
            Type = "percentage",
            Scope = "store_wide",
            StartDate = default
        };

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, createRequest, _companyHeaders);

        var result = await _functions.CreatePromotion(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Promotion>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        deserialized!.Message.Should().Contain("StartDate is required");
    }

    [Fact]
    public async Task CreatePromotion_NullJsonBody_Returns400()
    {
        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestDataWithRawBody("null", _companyHeaders, context);

        var result = await _functions.CreatePromotion(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Promotion>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        deserialized!.Message.Should().Contain("Invalid request body");
    }

    #endregion

    #region UpdatePromotion

    [Fact]
    public async Task UpdatePromotion_ValidRequest_Returns200WithUpdated()
    {
        var updateRequest = new UpdatePromotionRequest { Name = "Updated Sale" };
        var updated = CreatePromotion(1);
        updated.Name = "Updated Sale";
        _promotionServiceMock
            .Setup(s => s.UpdateAsync(1, It.IsAny<UpdatePromotionRequest>(), CompanyId))
            .ReturnsAsync(ApiResponse<Promotion>.SuccessResponse(updated));

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, updateRequest, _companyHeaders);

        var result = await _functions.UpdatePromotion(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Promotion>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.Name.Should().Be("Updated Sale");
    }

    [Fact]
    public async Task UpdatePromotion_NotFound_Returns404()
    {
        var updateRequest = new UpdatePromotionRequest { Name = "Updated" };
        _promotionServiceMock
            .Setup(s => s.UpdateAsync(99, It.IsAny<UpdatePromotionRequest>(), CompanyId))
            .ReturnsAsync(ApiResponse<Promotion>.ErrorResponse("Promotion not found"));

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, updateRequest, _companyHeaders);

        var result = await _functions.UpdatePromotion(req, 99);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdatePromotion_NullJsonBody_Returns400()
    {
        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestDataWithRawBody("null", _companyHeaders, context);

        var result = await _functions.UpdatePromotion(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Promotion>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        deserialized!.Message.Should().Contain("Invalid request body");
    }

    [Fact]
    public async Task UpdatePromotion_MissingCompanyHeader_Returns401()
    {
        var req = TestHelpers.CreateHttpRequestData(new Mock<FunctionContext>().Object);

        var result = await _functions.UpdatePromotion(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region DeletePromotion

    [Fact]
    public async Task DeletePromotion_ValidRequest_Returns204()
    {
        _promotionServiceMock
            .Setup(s => s.DeleteAsync(1, CompanyId))
            .ReturnsAsync(ApiResponse<bool>.SuccessResponse(true));

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, _companyHeaders);

        var result = await _functions.DeletePromotion(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeletePromotion_NotFound_Returns404()
    {
        _promotionServiceMock
            .Setup(s => s.DeleteAsync(99, CompanyId))
            .ReturnsAsync(ApiResponse<bool>.ErrorResponse("Promotion not found"));

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, _companyHeaders);

        var result = await _functions.DeletePromotion(req, 99);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeletePromotion_MissingCompanyHeader_Returns401()
    {
        var req = TestHelpers.CreateHttpRequestData(new Mock<FunctionContext>().Object);

        var result = await _functions.DeletePromotion(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Response Format

    [Fact]
    public async Task GetAllPromotions_ResponseHasCorrectContentType()
    {
        _promotionServiceMock
            .Setup(s => s.GetAllAsync(CompanyId))
            .ReturnsAsync(ApiResponse<List<Promotion>>.SuccessResponse(new List<Promotion>()));

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, _companyHeaders);

        var result = await _functions.GetAllPromotions(req);

        result.Headers.GetValues("Content-Type").Should().Contain("application/json; charset=utf-8");
    }

    [Fact]
    public async Task GetAllPromotions_ResponseUsesCamelCase()
    {
        _promotionServiceMock
            .Setup(s => s.GetAllAsync(CompanyId))
            .ReturnsAsync(ApiResponse<List<Promotion>>.SuccessResponse(new List<Promotion>()));

        var context = TestHelpers.CreateMockFunctionContextWithJwt(CompanyId);
        var req = TestHelpers.CreateHttpRequestData(context, null, _companyHeaders);

        var result = await _functions.GetAllPromotions(req);

        var body = await TestHelpers.ReadResponseBody(result);
        body.Should().Contain("\"success\"");
        body.Should().Contain("\"data\"");
        body.Should().NotContain("\"Success\"");
        body.Should().NotContain("\"Data\"");
    }

    #endregion

    #region Additional Validation

    [Fact]
    public async Task CreatePromotion_BxgyWithoutQuantities_Returns400()
    {
        var createRequest = new CreatePromotionRequest
        {
            Name = "Buy More Get More",
            Type = "bxgy",
            Scope = "store_wide",
            StartDate = DateTime.UtcNow,
        };

        var req = TestHelpers.CreateHttpRequestData(TestHelpers.CreateMockFunctionContextWithJwt(CompanyId), createRequest, _companyHeaders);

        var result = await _functions.CreatePromotion(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Promotion>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        deserialized!.Message.Should().Contain("BuyQuantity");
    }

    [Fact]
    public async Task CreatePromotion_DiscountPercentOver100_Returns400()
    {
        var createRequest = new CreatePromotionRequest
        {
            Name = "Too Generous",
            Type = "percentage",
            DiscountPercent = 150m,
            Scope = "store_wide",
            StartDate = DateTime.UtcNow,
        };

        var req = TestHelpers.CreateHttpRequestData(TestHelpers.CreateMockFunctionContextWithJwt(CompanyId), createRequest, _companyHeaders);

        var result = await _functions.CreatePromotion(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Promotion>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        deserialized!.Message.Should().Contain("DiscountPercent");
    }

    [Fact]
    public async Task CreatePromotion_CategoryScopeWithoutScopeValue_Returns400()
    {
        var createRequest = new CreatePromotionRequest
        {
            Name = "Cat Sale",
            Type = "percentage",
            DiscountPercent = 10m,
            Scope = "category",
            StartDate = DateTime.UtcNow,
        };

        var req = TestHelpers.CreateHttpRequestData(TestHelpers.CreateMockFunctionContextWithJwt(CompanyId), createRequest, _companyHeaders);

        var result = await _functions.CreatePromotion(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<Promotion>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        deserialized!.Message.Should().Contain("ScopeValue");
    }

    #endregion

    #region Permission Attribute Tests

    [Fact]
    public void GetAllPromotions_MethodLevel_RequiresPromotionManagePermission()
    {
        var method = typeof(PromotionFunctions).GetMethod(nameof(PromotionFunctions.GetAllPromotions));
        var attrs = method!
            .GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: false)
            .Cast<RequirePermissionAttribute>()
            .ToList();
        attrs.Should().ContainSingle(a => a.Permission == "promotion.manage");
    }

    [Fact]
    public void CreatePromotion_MethodLevel_RequiresPromotionManagePermission()
    {
        var method = typeof(PromotionFunctions).GetMethod(nameof(PromotionFunctions.CreatePromotion));
        var attrs = method!
            .GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: false)
            .Cast<RequirePermissionAttribute>()
            .ToList();
        attrs.Should().ContainSingle(a => a.Permission == "promotion.manage");
    }

    [Fact]
    public void UpdatePromotion_MethodLevel_RequiresPromotionManagePermission()
    {
        var method = typeof(PromotionFunctions).GetMethod(nameof(PromotionFunctions.UpdatePromotion));
        var attrs = method!
            .GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: false)
            .Cast<RequirePermissionAttribute>()
            .ToList();
        attrs.Should().ContainSingle(a => a.Permission == "promotion.manage");
    }

    [Fact]
    public void DeletePromotion_MethodLevel_RequiresPromotionManagePermission()
    {
        var method = typeof(PromotionFunctions).GetMethod(nameof(PromotionFunctions.DeletePromotion));
        var attrs = method!
            .GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: false)
            .Cast<RequirePermissionAttribute>()
            .ToList();
        attrs.Should().ContainSingle(a => a.Permission == "promotion.manage");
    }

    [Fact]
    public void GetActivePromotions_MethodLevel_DoesNotRequireManagePermission()
    {
        var method = typeof(PromotionFunctions).GetMethod(nameof(PromotionFunctions.GetActivePromotions));
        var attrs = method!
            .GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: false)
            .Cast<RequirePermissionAttribute>()
            .ToList();
        attrs.Should().NotContain(a => a.Permission == "promotion.manage");
    }

    #endregion
}
