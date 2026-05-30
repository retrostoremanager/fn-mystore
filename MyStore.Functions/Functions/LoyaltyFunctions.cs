using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MyStore.Functions.Attributes;
using MyStore.Functions.Helpers;
using MyStore.Models;
using MyStore.Services;

namespace MyStore.Functions;

public class LoyaltyFunctions
{
    private readonly ILoyaltyService _loyaltyService;
    private readonly ILogger _logger;

    public LoyaltyFunctions(ILoyaltyService loyaltyService, ILoggerFactory loggerFactory)
    {
        _loyaltyService = loyaltyService;
        _logger = loggerFactory.CreateLogger<LoyaltyFunctions>();
    }

    [Function("GetLoyaltySettings")]
    [RequirePermission("loyalty.manage")]
    public async Task<HttpResponseData> GetLoyaltySettings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "loyalty/settings")] HttpRequestData req)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<LoyaltySettings>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }

        _logger.LogInformation("Getting loyalty settings for company {CompanyId}", companyId);
        var response = await _loyaltyService.GetSettingsAsync(companyId);
        return await CreateHttpResponse(req, response);
    }

    [Function("UpdateLoyaltySettings")]
    [RequirePermission("loyalty.manage")]
    public async Task<HttpResponseData> UpdateLoyaltySettings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "loyalty/settings")] HttpRequestData req)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<LoyaltySettings>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        LoyaltySettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<LoyaltySettings>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            var errorResponse = ApiResponse<LoyaltySettings>.ErrorResponse("Invalid request body");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (settings == null)
        {
            var errorResponse = ApiResponse<LoyaltySettings>.ErrorResponse("Invalid request body");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (settings.IsEnabled)
        {
            if (settings.PointsPerDollarSpent <= 0)
            {
                var errorResponse = ApiResponse<LoyaltySettings>.ErrorResponse("PointsPerDollarSpent must be greater than zero when loyalty is enabled");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }
            if (settings.PointsPerDollarTradeIn <= 0)
            {
                var errorResponse = ApiResponse<LoyaltySettings>.ErrorResponse("PointsPerDollarTradeIn must be greater than zero when loyalty is enabled");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }
            if (settings.RedemptionRate <= 0)
            {
                var errorResponse = ApiResponse<LoyaltySettings>.ErrorResponse("RedemptionRate must be greater than zero when loyalty is enabled");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }
        }

        _logger.LogInformation("Updating loyalty settings for company {CompanyId}", companyId);
        var response = await _loyaltyService.UpdateSettingsAsync(settings, companyId);
        if (!response.Success)
            return await CreateHttpResponse(req, response, HttpStatusCode.BadRequest);
        return await CreateHttpResponse(req, response);
    }

    [Function("GetCustomerLoyaltyBalance")]
    [RequirePermission("customers.view")]
    public async Task<HttpResponseData> GetCustomerLoyaltyBalance(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customers/{id:int}/loyalty")] HttpRequestData req,
        int id)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<LoyaltyBalanceResponse>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }

        _logger.LogInformation("Getting loyalty balance for customer {CustomerId} in company {CompanyId}", id, companyId);
        var response = await _loyaltyService.GetBalanceAsync(companyId, id);
        if (!response.Success)
            return await CreateHttpResponse(req, response, HttpStatusCode.NotFound);
        return await CreateHttpResponse(req, response);
    }

    [Function("RedeemLoyaltyPoints")]
    [RequirePermission("customers.edit")]
    public async Task<HttpResponseData> RedeemLoyaltyPoints(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "customers/{id:int}/loyalty/redeem")] HttpRequestData req,
        int id)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<RedeemPointsResponse>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        RedeemPointsRequest? redeemRequest;
        try
        {
            redeemRequest = JsonSerializer.Deserialize<RedeemPointsRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            var errorResponse = ApiResponse<RedeemPointsResponse>.ErrorResponse("Invalid request body");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (redeemRequest == null || redeemRequest.PointsToRedeem <= 0)
        {
            var errorResponse = ApiResponse<RedeemPointsResponse>.ErrorResponse("points must be greater than zero");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        _logger.LogInformation("Redeeming {Points} loyalty points for customer {CustomerId} in company {CompanyId}", redeemRequest.PointsToRedeem, id, companyId);
        var response = await _loyaltyService.RedeemAsync(companyId, id, redeemRequest.PointsToRedeem);
        if (!response.Success)
            return await CreateHttpResponse(req, response, HttpStatusCode.BadRequest);
        return await CreateHttpResponse(req, response);
    }

    private static async Task<HttpResponseData> CreateHttpResponse<T>(
        HttpRequestData req,
        ApiResponse<T> apiResponse,
        HttpStatusCode? statusCode = null)
    {
        var resolvedStatus = statusCode ?? (apiResponse.Success ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        var response = req.CreateResponse(resolvedStatus);

        if (resolvedStatus == HttpStatusCode.NoContent)
            return response;

        response.Headers.Add("Content-Type", "application/json; charset=utf-8");

        var json = JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await response.WriteStringAsync(json);
        return response;
    }
}
