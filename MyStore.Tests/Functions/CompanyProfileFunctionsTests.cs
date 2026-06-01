using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Moq;
using MyStore.Functions;
using MyStore.Functions.Attributes;
using MyStore.Functions.Services;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Tests.Helpers;
using Xunit;

namespace MyStore.Tests.Functions;

public class CompanyProfileFunctionsTests
{
    private readonly Mock<ICompanyRepository> _companyRepoMock;
    private readonly Mock<ILocationRepository> _locationRepoMock;
    private readonly Mock<IInventoryRepository> _inventoryRepoMock;
    private readonly FakeLogoStorageService _fakeLogoStorage;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger> _loggerMock;
    private readonly CompanyProfileFunctions _functions;

    private const int TestCompanyId = 42;

    public CompanyProfileFunctionsTests()
    {
        _companyRepoMock = new Mock<ICompanyRepository>();
        _locationRepoMock = new Mock<ILocationRepository>();
        _inventoryRepoMock = new Mock<IInventoryRepository>();
        _fakeLogoStorage = new FakeLogoStorageService();
        _loggerMock = new Mock<ILogger>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();

        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        _functions = new CompanyProfileFunctions(
            _companyRepoMock.Object,
            _locationRepoMock.Object,
            _inventoryRepoMock.Object,
            _fakeLogoStorage,
            _loggerFactoryMock.Object);
    }

    private static FunctionContext CreateAuthenticatedContext() =>
        TestHelpers.CreateMockFunctionContextWithJwt(TestCompanyId);

    private static IReadOnlyDictionary<string, string> CompanyIdHeaders() =>
        new Dictionary<string, string> { ["X-Company-Id"] = TestCompanyId.ToString() };

    private static CompanyProfile CreateTestProfile(int companyId = TestCompanyId) => new()
    {
        Id = companyId,
        CompanyName = "Test Store",
        CompanyAddress = "123 Main St",
        CompanyCity = "Springfield",
        CompanyState = "IL",
        CompanyZipCode = "62701",
        CompanyPhone = "555-1234",
        Locale = "en-US",
        LogoUrl = "https://example.com/logo.png"
    };

    #region GetProfile Tests

    [Fact]
    public async Task GetProfile_AuthenticatedCompany_Returns200WithProfile()
    {
        var profile = CreateTestProfile();
        var locations = new List<Location>
        {
            new() { Id = 1, CompanyId = TestCompanyId, Name = "Main Location" }
        };

        _companyRepoMock
            .Setup(r => r.GetProfileAsync(TestCompanyId))
            .ReturnsAsync(profile);
        _locationRepoMock
            .Setup(r => r.GetByCompanyIdAsync(TestCompanyId))
            .ReturnsAsync(locations);

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyIdHeaders());

        var result = await _functions.GetProfile(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<CompanyProfileResponse>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().NotBeNull();
        deserialized.Data!.Profile.CompanyName.Should().Be("Test Store");
        deserialized.Data.Locations.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetProfile_MissingCompanyId_Returns401Unauthorized()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object);

        var result = await _functions.GetProfile(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<CompanyProfile>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task GetProfile_ProfileNotFound_Returns404NotFound()
    {
        _companyRepoMock
            .Setup(r => r.GetProfileAsync(TestCompanyId))
            .ReturnsAsync((CompanyProfile?)null);
        _locationRepoMock
            .Setup(r => r.GetByCompanyIdAsync(TestCompanyId))
            .ReturnsAsync(new List<Location>());

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyIdHeaders());

        var result = await _functions.GetProfile(req);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<CompanyProfile>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task GetProfile_ServiceException_Returns500()
    {
        _companyRepoMock
            .Setup(r => r.GetProfileAsync(TestCompanyId))
            .ThrowsAsync(new Exception("DB error"));

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyIdHeaders());

        var result = await _functions.GetProfile(req);

        result.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task GetProfile_ResponseHasCorrectContentType()
    {
        _companyRepoMock
            .Setup(r => r.GetProfileAsync(TestCompanyId))
            .ReturnsAsync(CreateTestProfile());
        _locationRepoMock
            .Setup(r => r.GetByCompanyIdAsync(TestCompanyId))
            .ReturnsAsync(new List<Location>());

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyIdHeaders());

        var result = await _functions.GetProfile(req);

        result.Headers.Should().ContainKey("Content-Type");
        result.Headers.GetValues("Content-Type").Should().Contain("application/json; charset=utf-8");
    }

    #endregion

    #region UpdateProfile Tests

    [Fact]
    public async Task UpdateProfile_ValidRequest_Returns200WithUpdatedProfile()
    {
        var updateRequest = new CompanyProfileUpdateRequest
        {
            CompanyName = "Updated Store",
            CompanyCity = "Chicago"
        };

        var updatedProfile = CreateTestProfile();
        updatedProfile.CompanyName = "Updated Store";
        updatedProfile.CompanyCity = "Chicago";

        _companyRepoMock
            .Setup(r => r.UpdateProfileAsync(TestCompanyId, It.IsAny<CompanyProfileUpdateRequest>()))
            .Returns(Task.CompletedTask);
        _companyRepoMock
            .Setup(r => r.GetProfileAsync(TestCompanyId))
            .ReturnsAsync(updatedProfile);

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, updateRequest, CompanyIdHeaders());

        var result = await _functions.UpdateProfile(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<CompanyProfile>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().NotBeNull();
        deserialized.Data!.CompanyName.Should().Be("Updated Store");
    }

    [Fact]
    public async Task UpdateProfile_EmptyBody_Returns400BadRequest()
    {
        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestDataWithRawBody(string.Empty, CompanyIdHeaders(), context: context);

        var result = await _functions.UpdateProfile(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<CompanyProfile>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("Invalid request body");
    }

    [Fact]
    public async Task UpdateProfile_NullDeserializedBody_Returns400BadRequest()
    {
        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestDataWithRawBody("null", CompanyIdHeaders(), context: context);

        var result = await _functions.UpdateProfile(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateProfile_MissingCompanyId_Returns401Unauthorized()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, new CompanyProfileUpdateRequest { CompanyName = "X" }, null);

        var result = await _functions.UpdateProfile(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateProfile_ServiceException_Returns500()
    {
        _companyRepoMock
            .Setup(r => r.UpdateProfileAsync(TestCompanyId, It.IsAny<CompanyProfileUpdateRequest>()))
            .ThrowsAsync(new Exception("DB error"));

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, new CompanyProfileUpdateRequest { CompanyName = "X" }, CompanyIdHeaders());

        var result = await _functions.UpdateProfile(req);

        result.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task UpdateProfile_CompanyAddress2_IsPersisted()
    {
        var updateRequest = new CompanyProfileUpdateRequest
        {
            CompanyName = "Test Store",
            CompanyAddress = "123 Main St",
            CompanyAddress2 = "Suite 400",
            CompanyCity = "Springfield"
        };

        CompanyProfileUpdateRequest? capturedRequest = null;
        _companyRepoMock
            .Setup(r => r.UpdateProfileAsync(TestCompanyId, It.IsAny<CompanyProfileUpdateRequest>()))
            .Callback<int, CompanyProfileUpdateRequest>((_, r) => capturedRequest = r)
            .Returns(Task.CompletedTask);

        var updatedProfile = CreateTestProfile();
        updatedProfile.CompanyAddress2 = "Suite 400";
        _companyRepoMock
            .Setup(r => r.GetProfileAsync(TestCompanyId))
            .ReturnsAsync(updatedProfile);

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, updateRequest, CompanyIdHeaders());

        var result = await _functions.UpdateProfile(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.CompanyAddress2.Should().Be("Suite 400", because: "the secondary address line must be passed through to the repository for persistence");

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<CompanyProfile>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        deserialized!.Data!.CompanyAddress2.Should().Be("Suite 400");

        _companyRepoMock.Verify(
            r => r.UpdateProfileAsync(TestCompanyId, It.Is<CompanyProfileUpdateRequest>(req => req.CompanyAddress2 == "Suite 400")),
            Times.Once);
    }

    #endregion

    #region GetTaxSettings Tests

    [Fact]
    public async Task GetTaxSettings_AuthenticatedCompany_Returns200WithTaxSettings()
    {
        var taxSettings = new TaxSettingsResponse
        {
            TaxEnabled = true,
            TaxRate = 0.0875m,
            TaxLabel = "Sales Tax"
        };

        _companyRepoMock
            .Setup(r => r.GetTaxSettingsAsync(TestCompanyId))
            .ReturnsAsync(taxSettings);

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyIdHeaders());

        var result = await _functions.GetTaxSettings(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<TaxSettingsResponse>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().NotBeNull();
        deserialized.Data!.TaxEnabled.Should().BeTrue();
        deserialized.Data.TaxRate.Should().Be(0.0875m);
        deserialized.Data.TaxLabel.Should().Be("Sales Tax");
    }

    [Fact]
    public async Task GetTaxSettings_MissingCompanyId_Returns401Unauthorized()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object);

        var result = await _functions.GetTaxSettings(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetTaxSettings_CompanyNotFound_Returns404NotFound()
    {
        _companyRepoMock
            .Setup(r => r.GetTaxSettingsAsync(TestCompanyId))
            .ReturnsAsync((TaxSettingsResponse?)null);

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyIdHeaders());

        var result = await _functions.GetTaxSettings(req);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTaxSettings_ServiceException_Returns500()
    {
        _companyRepoMock
            .Setup(r => r.GetTaxSettingsAsync(TestCompanyId))
            .ThrowsAsync(new Exception("DB error"));

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, null, CompanyIdHeaders());

        var result = await _functions.GetTaxSettings(req);

        result.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    #endregion

    #region UpdateTaxSettings Tests

    [Fact]
    public async Task UpdateTaxSettings_ValidRequest_Returns200WithUpdatedSettings()
    {
        var updateRequest = new TaxSettingsRequest
        {
            TaxEnabled = true,
            TaxRate = 0.07m,
            TaxLabel = "GST"
        };

        var updatedSettings = new TaxSettingsResponse
        {
            TaxEnabled = true,
            TaxRate = 0.07m,
            TaxLabel = "GST"
        };

        _companyRepoMock
            .Setup(r => r.UpdateTaxSettingsAsync(TestCompanyId, It.IsAny<TaxSettingsRequest>()))
            .Returns(Task.CompletedTask);
        _companyRepoMock
            .Setup(r => r.GetTaxSettingsAsync(TestCompanyId))
            .ReturnsAsync(updatedSettings);

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, updateRequest, CompanyIdHeaders());

        var result = await _functions.UpdateTaxSettings(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<TaxSettingsResponse>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().NotBeNull();
        deserialized.Data!.TaxRate.Should().Be(0.07m);
        deserialized.Data.TaxLabel.Should().Be("GST");
    }

    [Fact]
    public async Task UpdateTaxSettings_NegativeTaxRate_Returns400BadRequest()
    {
        var updateRequest = new TaxSettingsRequest
        {
            TaxEnabled = true,
            TaxRate = -0.05m,
            TaxLabel = "Sales Tax"
        };

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, updateRequest, CompanyIdHeaders());

        var result = await _functions.UpdateTaxSettings(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<TaxSettingsResponse>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("taxRate");
    }

    [Fact]
    public async Task UpdateTaxSettings_TaxRateEqualToOne_Returns400BadRequest()
    {
        var updateRequest = new TaxSettingsRequest
        {
            TaxEnabled = true,
            TaxRate = 1.0m,
            TaxLabel = "Sales Tax"
        };

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, updateRequest, CompanyIdHeaders());

        var result = await _functions.UpdateTaxSettings(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateTaxSettings_MissingCompanyId_Returns401Unauthorized()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, new TaxSettingsRequest { TaxRate = 0.05m }, null);

        var result = await _functions.UpdateTaxSettings(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateTaxSettings_EmptyBody_Returns400BadRequest()
    {
        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestDataWithRawBody(string.Empty, CompanyIdHeaders(), context: context);

        var result = await _functions.UpdateTaxSettings(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateTaxSettings_ServiceException_Returns500()
    {
        var updateRequest = new TaxSettingsRequest
        {
            TaxEnabled = false,
            TaxRate = 0.05m,
            TaxLabel = "Sales Tax"
        };

        _companyRepoMock
            .Setup(r => r.UpdateTaxSettingsAsync(TestCompanyId, It.IsAny<TaxSettingsRequest>()))
            .ThrowsAsync(new Exception("DB error"));

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, updateRequest, CompanyIdHeaders());

        var result = await _functions.UpdateTaxSettings(req);

        result.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    #endregion

    #region UploadLogo Tests

    [Fact]
    public async Task UploadLogo_ValidBase64Png_Returns200WithProfile()
    {
        var fileBytes = Encoding.UTF8.GetBytes("fake-png-content");
        var base64 = Convert.ToBase64String(fileBytes);

        var uploadRequest = new LogoUploadRequest
        {
            File = base64,
            FileName = "logo.png",
            ContentType = "image/png"
        };

        _fakeLogoStorage.UploadResult = "https://blob.example.com/42/logo.png";

        var updatedProfile = CreateTestProfile();
        updatedProfile.LogoUrl = "https://blob.example.com/42/logo.png";

        _companyRepoMock
            .Setup(r => r.UpdateProfileAsync(TestCompanyId, It.IsAny<CompanyProfileUpdateRequest>()))
            .Returns(Task.CompletedTask);
        _companyRepoMock
            .Setup(r => r.GetProfileAsync(TestCompanyId))
            .ReturnsAsync(updatedProfile);

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, uploadRequest, CompanyIdHeaders());

        var result = await _functions.UploadLogo(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<CompanyProfile>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().NotBeNull();
        deserialized.Data!.LogoUrl.Should().Be("https://blob.example.com/42/logo.png");
    }

    [Fact]
    public async Task UploadLogo_EmptyBody_Returns400BadRequest()
    {
        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestDataWithRawBody(string.Empty, CompanyIdHeaders(), context: context);

        var result = await _functions.UploadLogo(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<CompanyProfile>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("required");
    }

    [Fact]
    public async Task UploadLogo_NoFileProvided_Returns400BadRequest()
    {
        var uploadRequest = new LogoUploadRequest
        {
            File = string.Empty,
            FileName = "logo.png",
            ContentType = "image/png"
        };

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, uploadRequest, CompanyIdHeaders());

        var result = await _functions.UploadLogo(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<CompanyProfile>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("required");
    }

    [Fact]
    public async Task UploadLogo_MissingFileName_Returns400BadRequest()
    {
        var fileBytes = Encoding.UTF8.GetBytes("fake-png-content");
        var uploadRequest = new LogoUploadRequest
        {
            File = Convert.ToBase64String(fileBytes),
            FileName = string.Empty,
            ContentType = "image/png"
        };

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, uploadRequest, CompanyIdHeaders());

        var result = await _functions.UploadLogo(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<CompanyProfile>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task UploadLogo_InvalidBase64_Returns400BadRequest()
    {
        var uploadRequest = new LogoUploadRequest
        {
            File = "not-valid-base64!!!",
            FileName = "logo.png",
            ContentType = "image/png"
        };

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, uploadRequest, CompanyIdHeaders());

        var result = await _functions.UploadLogo(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<CompanyProfile>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("Invalid base64");
    }

    [Fact]
    public async Task UploadLogo_UnsupportedMimeType_Returns400BadRequest()
    {
        var fileBytes = Encoding.UTF8.GetBytes("fake-gif-content");
        var uploadRequest = new LogoUploadRequest
        {
            File = Convert.ToBase64String(fileBytes),
            FileName = "logo.gif",
            ContentType = "image/gif"
        };

        _fakeLogoStorage.UploadException = new ArgumentException("Invalid content type. Allowed: image/png, image/jpeg, image/svg+xml. Got: image/gif");

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, uploadRequest, CompanyIdHeaders());

        var result = await _functions.UploadLogo(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<CompanyProfile>>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task UploadLogo_MissingCompanyId_Returns401Unauthorized()
    {
        var fileBytes = Encoding.UTF8.GetBytes("fake-png-content");
        var uploadRequest = new LogoUploadRequest
        {
            File = Convert.ToBase64String(fileBytes),
            FileName = "logo.png",
            ContentType = "image/png"
        };

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, uploadRequest, null);

        var result = await _functions.UploadLogo(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UploadLogo_ServiceException_Returns500()
    {
        var fileBytes = Encoding.UTF8.GetBytes("fake-png-content");
        var uploadRequest = new LogoUploadRequest
        {
            File = Convert.ToBase64String(fileBytes),
            FileName = "logo.png",
            ContentType = "image/png"
        };

        _fakeLogoStorage.UploadException = new Exception("Blob storage unreachable");

        var context = CreateAuthenticatedContext();
        var req = TestHelpers.CreateHttpRequestData(context, uploadRequest, CompanyIdHeaders());

        var result = await _functions.UploadLogo(req);

        result.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    #endregion

    #region Permission Attribute Tests

    [Fact]
    public void GetTaxSettings_RequiresCompanyViewPermission()
    {
        var method = typeof(CompanyProfileFunctions).GetMethod(
            nameof(CompanyProfileFunctions.GetTaxSettings),
            BindingFlags.Public | BindingFlags.Instance);

        method.Should().NotBeNull();
        var attributes = method!.GetCustomAttributes<RequirePermissionAttribute>(inherit: false).ToList();
        attributes.Should().Contain(
            a => a.Permission == "company.view",
            because: "GET /company/tax must enforce the company.view permission via RbacMiddleware. A user lacking this permission must receive 403 Forbidden.");
    }

    [Fact]
    public void UpdateTaxSettings_RequiresCompanyEditPermission()
    {
        var method = typeof(CompanyProfileFunctions).GetMethod(
            nameof(CompanyProfileFunctions.UpdateTaxSettings),
            BindingFlags.Public | BindingFlags.Instance);

        method.Should().NotBeNull();
        var attributes = method!.GetCustomAttributes<RequirePermissionAttribute>(inherit: false).ToList();
        attributes.Should().Contain(
            a => a.Permission == "company.edit",
            because: "PUT /company/tax must enforce the company.edit permission via RbacMiddleware. A user lacking this permission must receive 403 Forbidden.");
    }

    #endregion
}

/// <summary>
/// Testable subclass of LogoStorageService that bypasses blob storage.
/// </summary>
internal class FakeLogoStorageService : LogoStorageService
{
    public string UploadResult { get; set; } = "https://fake-blob/logo.png";
    public Exception? UploadException { get; set; }
    public bool DeleteCalled { get; private set; }

    public FakeLogoStorageService() : base(CreateFakeContainerClient())
    {
    }

    private static BlobContainerClient CreateFakeContainerClient()
    {
        return new BlobContainerClient(new Uri("https://fake.blob.core.windows.net/company-logos"), null);
    }

    public override Task<string> UploadAsync(int companyId, byte[] fileBytes, string fileName, string contentType)
    {
        if (UploadException != null)
            throw UploadException;
        return Task.FromResult(UploadResult);
    }

    public override Task DeleteAsync(int companyId)
    {
        DeleteCalled = true;
        return Task.CompletedTask;
    }
}
