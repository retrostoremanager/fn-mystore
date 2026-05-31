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

[RequirePermission("promotion.view")]
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
        var request = JsonSerializer.Deserialize<CreatePromotionRequest>(requestBody, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (request == null)
        {
            var errorResponse = ApiResponse<Promotion>.ErrorResponse("Invalid request body");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            var errorResponse = ApiResponse<Promotion>.ErrorResponse("Name is required");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Type))
        {
            var errorResponse = ApiResponse<Promotion>.ErrorResponse("Type is required");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Scope))
        {
            var errorResponse = ApiResponse<Promotion>.ErrorResponse("Scope is required");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (request.StartDate == default)
        {
            var errorResponse = ApiResponse<Promotion>.ErrorResponse("StartDate is required");
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
        var request = JsonSerializer.Deserialize<UpdatePromotionRequest>(requestBody, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (request == null)
        {
            var errorResponse = ApiResponse<Promotion>.ErrorResponse("Invalid request body");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
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
