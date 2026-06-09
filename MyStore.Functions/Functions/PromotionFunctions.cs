using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MyStore.Functions.Attributes;
using MyStore.Functions.Helpers;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Services;

namespace MyStore.Functions;

public class PromotionFunctions
{
    private readonly IPromotionService _promotionService;
    private readonly IUserRepository _userRepository;
    private readonly ILogger _logger;

    public PromotionFunctions(IPromotionService promotionService, IUserRepository userRepository, ILoggerFactory loggerFactory)
    {
        _promotionService = promotionService;
        _userRepository = userRepository;
        _logger = loggerFactory.CreateLogger<PromotionFunctions>();
    }

    [Function("GetAllPromotions")]
    [RequirePermission("promotion.manage")]
    public async Task<HttpResponseData> GetAllPromotions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "promotions")] HttpRequestData req)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<List<Promotion>>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }

        _logger.LogInformation("Getting all promotions for company {CompanyId}", companyId);
        var response = await _promotionService.GetAllAsync(companyId);
        return await CreateHttpResponse(req, response);
    }

    [Function("GetActivePromotions")]
    public async Task<HttpResponseData> GetActivePromotions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "promotions/active")] HttpRequestData req)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<List<Promotion>>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }

        _logger.LogInformation("Getting active promotions for company {CompanyId}", companyId);
        var response = await _promotionService.GetActivePromotionsAsync(companyId);
        return await CreateHttpResponse(req, response);
    }

    [Function("CreatePromotion")]
    [RequirePermission("promotion.manage")]
    public async Task<HttpResponseData> CreatePromotion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "promotions")] HttpRequestData req)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<Promotion>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        CreatePromotionRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<CreatePromotionRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            var errorResponse = ApiResponse<Promotion>.ErrorResponse("Invalid request body");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (request == null)
        {
            var errorResponse = ApiResponse<Promotion>.ErrorResponse("Invalid request body");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        var validationError = ValidatePromotion(request.Name, request.Type, request.DiscountPercent, request.BuyQuantity, request.GetQuantity, request.Scope, request.ScopeValue, request.StartDate);
        if (validationError != null)
        {
            var errorResponse = ApiResponse<Promotion>.ErrorResponse(validationError);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        var email = CompanyHelper.GetEmailFromJwt(req);
        if (!string.IsNullOrEmpty(email))
        {
            var user = await _userRepository.GetByEmailAsync(email, companyId);
            if (user != null)
                request.CreatedBy = user.Id;
        }

        _logger.LogInformation("Creating promotion for company {CompanyId}", companyId);
        var response = await _promotionService.CreateAsync(request, companyId);
        var statusCode = response.Success ? HttpStatusCode.Created : HttpStatusCode.BadRequest;
        return await CreateHttpResponse(req, response, statusCode);
    }

    [Function("UpdatePromotion")]
    [RequirePermission("promotion.manage")]
    public async Task<HttpResponseData> UpdatePromotion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "promotions/{id:int}")] HttpRequestData req,
        int id)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<Promotion>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        UpdatePromotionRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<UpdatePromotionRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            var errorResponse = ApiResponse<Promotion>.ErrorResponse("Invalid request body");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (request == null)
        {
            var errorResponse = ApiResponse<Promotion>.ErrorResponse("Invalid request body");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (request.Name != null && string.IsNullOrWhiteSpace(request.Name))
        {
            var errorResponse = ApiResponse<Promotion>.ErrorResponse("Name is required");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (request.Type != null)
        {
            var validationError = ValidatePromotion(
                request.Name ?? "placeholder",
                request.Type,
                request.DiscountPercent,
                request.BuyQuantity,
                request.GetQuantity,
                request.Scope ?? "all",
                request.ScopeValue,
                request.StartDate ?? DateTime.UtcNow);
            if (validationError != null)
            {
                var errorResponse = ApiResponse<Promotion>.ErrorResponse(validationError);
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }
        }
        else
        {
            if (request.Scope != null && (request.Scope == "category" || request.Scope == "item") && string.IsNullOrWhiteSpace(request.ScopeValue))
            {
                var errorResponse = ApiResponse<Promotion>.ErrorResponse("ScopeValue is required when Scope is 'category' or 'item'");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }
            if (request.DiscountPercent.HasValue && (request.DiscountPercent.Value < 0 || request.DiscountPercent.Value > 100))
            {
                var errorResponse = ApiResponse<Promotion>.ErrorResponse("DiscountPercent must be between 0 and 100");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }
            if (request.BuyQuantity.HasValue && request.BuyQuantity.Value <= 0)
            {
                var errorResponse = ApiResponse<Promotion>.ErrorResponse("BuyQuantity must be greater than zero");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }
            if (request.GetQuantity.HasValue && request.GetQuantity.Value <= 0)
            {
                var errorResponse = ApiResponse<Promotion>.ErrorResponse("GetQuantity must be greater than zero");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }
        }

        _logger.LogInformation("Updating promotion {Id} for company {CompanyId}", id, companyId);
        var response = await _promotionService.UpdateAsync(id, request, companyId);
        if (!response.Success)
        {
            var statusCode = response.Message?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true
                ? HttpStatusCode.NotFound
                : HttpStatusCode.BadRequest;
            return await CreateHttpResponse(req, response, statusCode);
        }
        return await CreateHttpResponse(req, response);
    }

    [Function("DeletePromotion")]
    [RequirePermission("promotion.manage")]
    public async Task<HttpResponseData> DeletePromotion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "promotions/{id:int}")] HttpRequestData req,
        int id)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<bool>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }

        _logger.LogInformation("Deleting promotion {Id} for company {CompanyId}", id, companyId);
        var response = await _promotionService.DeleteAsync(id, companyId);
        var statusCode = response.Success ? HttpStatusCode.NoContent : HttpStatusCode.NotFound;
        return await CreateHttpResponse(req, response, statusCode);
    }

    private static string? ValidatePromotion(
        string name,
        string type,
        decimal? discountPercent,
        int? buyQuantity,
        int? getQuantity,
        string scope,
        string? scopeValue,
        DateTime startDate)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Name is required";

        if (startDate == default)
            return "StartDate is required";

        if (string.IsNullOrWhiteSpace(type))
            return "Type is required";

        var normalizedType = type.Trim().ToLowerInvariant();
        if (normalizedType != "percentage" && normalizedType != "bxgy")
            return "Type must be 'percentage' or 'bxgy'";

        if (normalizedType == "percentage")
        {
            if (!discountPercent.HasValue)
                return "DiscountPercent is required for percentage promotions";
            if (discountPercent.Value < 0 || discountPercent.Value > 100)
                return "DiscountPercent must be between 0 and 100";
        }

        if (normalizedType == "bxgy")
        {
            if (!buyQuantity.HasValue || buyQuantity.Value <= 0)
                return "BuyQuantity must be greater than zero for bxgy promotions";
            if (!getQuantity.HasValue || getQuantity.Value <= 0)
                return "GetQuantity must be greater than zero for bxgy promotions";
        }

        if (string.IsNullOrWhiteSpace(scope))
            return "Scope is required";

        var normalizedScope = scope.Trim().ToLowerInvariant();
        if ((normalizedScope == "category" || normalizedScope == "item") && string.IsNullOrWhiteSpace(scopeValue))
            return "ScopeValue is required when Scope is 'category' or 'item'";

        return null;
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
