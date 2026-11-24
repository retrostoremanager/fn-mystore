using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyStore.Models;
using MyStore.Services;

namespace MyStore.Functions;

/// <summary>
/// Internal admin operations, never reachable through the public web app. Every endpoint here
/// is gated on a shared secret (the AdminApiKey app setting) checked via the X-Admin-Key
/// header -- there is no public sign-up-equivalent path to any of this.
/// </summary>
public class AdminFunctions
{
    private readonly IAdminService _adminService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminFunctions> _logger;

    public AdminFunctions(IAdminService adminService, IConfiguration configuration, ILogger<AdminFunctions> logger)
    {
        _adminService = adminService;
        _configuration = configuration;
        _logger = logger;
    }

    // Creates a company with no Stripe involvement -- for internal test/demo accounts (e.g.
    // trying out a paid add-on) where asking for a real card makes no sense. Immediately
    // Active, with a synthetic subscription row so add-on entitlement checks work.
    [Function("CreateTestCompany")]
    public async Task<HttpResponseData> CreateTestCompany(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal-admin/test-companies")] HttpRequestData req)
    {
        if (!IsAuthorized(req))
        {
            _logger.LogWarning("CreateTestCompany: missing or invalid X-Admin-Key");
            return await CreateHttpResponse(req, ApiResponse<CreateTestCompanyResponse>.ErrorResponse("Unauthorized"), HttpStatusCode.Unauthorized);
        }

        try
        {
            var requestBody = await req.ReadAsStringAsync() ?? string.Empty;
            var request = JsonSerializer.Deserialize<CreateTestCompanyRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request is null)
            {
                return await CreateHttpResponse(req, ApiResponse<CreateTestCompanyResponse>.ErrorResponse("Invalid request body"), HttpStatusCode.BadRequest);
            }

            var result = await _adminService.CreateTestCompanyAsync(request);
            return await CreateHttpResponse(req, ApiResponse<CreateTestCompanyResponse>.SuccessResponse(result, "Test company created."), HttpStatusCode.OK);
        }
        catch (ArgumentException ex)
        {
            return await CreateHttpResponse(req, ApiResponse<CreateTestCompanyResponse>.ErrorResponse(ex.Message), HttpStatusCode.BadRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating test company");
            return await CreateHttpResponse(req, ApiResponse<CreateTestCompanyResponse>.ErrorResponse("An error occurred while creating the test company."), HttpStatusCode.InternalServerError);
        }
    }

    private bool IsAuthorized(HttpRequestData req)
    {
        var configuredKey = _configuration["AdminApiKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            _logger.LogError("AdminApiKey is not configured; refusing all admin requests.");
            return false;
        }

        if (!req.Headers.TryGetValues("X-Admin-Key", out var values))
            return false;

        var providedKey = values.FirstOrDefault() ?? string.Empty;
        var providedBytes = Encoding.UTF8.GetBytes(providedKey);
        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);
        return providedBytes.Length == configuredBytes.Length && CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);
    }

    private static async Task<HttpResponseData> CreateHttpResponse<T>(HttpRequestData req, ApiResponse<T> apiResponse, HttpStatusCode statusCode)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
        return response;
    }
}
