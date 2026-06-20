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

[RequirePermission("consignment.view")]
public class ConsignmentFunctions
{
    private readonly IConsignmentService _consignmentService;
    private readonly ILogger _logger;

    public ConsignmentFunctions(IConsignmentService consignmentService, ILoggerFactory loggerFactory)
    {
        _consignmentService = consignmentService;
        _logger = loggerFactory.CreateLogger<ConsignmentFunctions>();
    }

    [Function("GetAllConsignmentItems")]
    public async Task<HttpResponseData> GetAllConsignmentItems(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "consignment")] HttpRequestData req)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<List<ConsignmentItem>>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }

        var status = req.Query["status"];
        _logger.LogInformation("Getting all consignment items for company {CompanyId}", companyId);
        var response = await _consignmentService.GetAllAsync(companyId, status);
        return await CreateHttpResponse(req, response);
    }

    [Function("GetConsignmentItemById")]
    public async Task<HttpResponseData> GetConsignmentItemById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "consignment/{id:int}")] HttpRequestData req,
        int id)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<ConsignmentItem>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }

        _logger.LogInformation("Getting consignment item {Id} for company {CompanyId}", id, companyId);
        var response = await _consignmentService.GetByIdAsync(id, companyId);
        if (!response.Success)
            return await CreateHttpResponse(req, response, HttpStatusCode.NotFound);
        return await CreateHttpResponse(req, response);
    }

    [Function("CreateConsignmentItem")]
    [RequirePermission("consignment.edit")]
    public async Task<HttpResponseData> CreateConsignmentItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "consignment")] HttpRequestData req)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<ConsignmentItem>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        ConsignmentItem? item;
        try
        {
            item = JsonSerializer.Deserialize<ConsignmentItem>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            var errorResponse = ApiResponse<ConsignmentItem>.ErrorResponse("Invalid request body");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (item == null)
        {
            var errorResponse = ApiResponse<ConsignmentItem>.ErrorResponse("Invalid request body");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (item.CustomerId <= 0)
        {
            var errorResponse = ApiResponse<ConsignmentItem>.ErrorResponse("CustomerId is required");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (string.IsNullOrWhiteSpace(item.Description))
        {
            var errorResponse = ApiResponse<ConsignmentItem>.ErrorResponse("Description is required");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (item.AskingPrice <= 0)
        {
            var errorResponse = ApiResponse<ConsignmentItem>.ErrorResponse("AskingPrice must be greater than zero");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (item.SplitPercent < 0 || item.SplitPercent > 100)
        {
            var errorResponse = ApiResponse<ConsignmentItem>.ErrorResponse("SplitPercent must be between 0 and 100");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        _logger.LogInformation("Creating consignment item for company {CompanyId}", companyId);
        var response = await _consignmentService.CreateAsync(item, companyId);
        var statusCode = response.Success ? HttpStatusCode.Created : HttpStatusCode.BadRequest;
        return await CreateHttpResponse(req, response, statusCode);
    }

    [Function("UpdateConsignmentItem")]
    [RequirePermission("consignment.edit")]
    public async Task<HttpResponseData> UpdateConsignmentItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "consignment/{id:int}")] HttpRequestData req,
        int id)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<ConsignmentItem>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        UpdateConsignmentItemRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<UpdateConsignmentItemRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            var errorResponse = ApiResponse<ConsignmentItem>.ErrorResponse("Invalid request body");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (request == null)
        {
            var errorResponse = ApiResponse<ConsignmentItem>.ErrorResponse("Invalid request body");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            var errorResponse = ApiResponse<ConsignmentItem>.ErrorResponse("Description is required");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (request.AskingPrice <= 0)
        {
            var errorResponse = ApiResponse<ConsignmentItem>.ErrorResponse("AskingPrice must be greater than zero");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (request.SplitPercent < 0 || request.SplitPercent > 100)
        {
            var errorResponse = ApiResponse<ConsignmentItem>.ErrorResponse("SplitPercent must be between 0 and 100");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        _logger.LogInformation("Updating consignment item {Id} for company {CompanyId}", id, companyId);

        var existingResponse = await _consignmentService.GetByIdAsync(id, companyId);
        if (!existingResponse.Success || existingResponse.Data == null)
        {
            return await CreateHttpResponse(req, ApiResponse<ConsignmentItem>.ErrorResponse(existingResponse.Message ?? "Not found"), HttpStatusCode.NotFound);
        }

        var existing = existingResponse.Data;
        if (!string.Equals(existing.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            var errorResponse = ApiResponse<ConsignmentItem>.ErrorResponse(
                $"Cannot update consignment item: current status is '{existing.Status}'. Only active items can be updated.");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        existing.Description = request.Description;
        existing.AskingPrice = request.AskingPrice;
        existing.SplitPercent = request.SplitPercent;

        var response = await _consignmentService.UpdateAsync(existing, companyId);
        if (!response.Success)
        {
            var statusCode = IsNotFound(response.Message) ? HttpStatusCode.NotFound : HttpStatusCode.BadRequest;
            return await CreateHttpResponse(req, response, statusCode);
        }
        return await CreateHttpResponse(req, response);
    }

    [Function("MarkConsignmentItemSold")]
    [RequirePermission("consignment.edit")]
    public async Task<HttpResponseData> MarkConsignmentItemSold(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "consignment/{id:int}/sold")] HttpRequestData req,
        int id)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<MarkSoldResponse>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        MarkSoldRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<MarkSoldRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            var errorResponse = ApiResponse<MarkSoldResponse>.ErrorResponse("Invalid request body");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        if (request == null || request.SalePrice <= 0)
        {
            var errorResponse = ApiResponse<MarkSoldResponse>.ErrorResponse("salePrice is required and must be greater than zero");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        var userEmail = CompanyHelper.GetEmailFromRequest(req, req.FunctionContext);
        _logger.LogInformation("Marking consignment item {Id} as sold for company {CompanyId}", id, companyId);
        var response = await _consignmentService.MarkSoldAsync(id, request.SalePrice, companyId, userEmail);
        if (!response.Success)
        {
            var statusCode = IsStatusConflict(response.Message) ? HttpStatusCode.Conflict : HttpStatusCode.NotFound;
            return await CreateHttpResponse(req, response, statusCode);
        }
        return await CreateHttpResponse(req, response);
    }

    [Function("ProcessConsignmentPayout")]
    [RequirePermission("consignment.edit")]
    public async Task<HttpResponseData> ProcessConsignmentPayout(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "consignment/{id:int}/payout")] HttpRequestData req,
        int id)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<ConsignmentPayout>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        var request = JsonSerializer.Deserialize<ProcessPayoutRequest>(requestBody, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        _logger.LogInformation("Processing payout for consignment item {Id} for company {CompanyId}", id, companyId);
        var response = await _consignmentService.ProcessPayoutAsync(id, request?.Notes, companyId);
        if (!response.Success)
        {
            var statusCode = IsStatusConflict(response.Message) ? HttpStatusCode.Conflict : HttpStatusCode.NotFound;
            return await CreateHttpResponse(req, response, statusCode);
        }
        return await CreateHttpResponse(req, response);
    }

    [Function("GetConsignmentPayouts")]
    public async Task<HttpResponseData> GetConsignmentPayouts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "consignment/{id:int}/payouts")] HttpRequestData req,
        int id)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<List<ConsignmentPayout>>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }

        _logger.LogInformation("Getting payouts for consignment item {Id} for company {CompanyId}", id, companyId);
        var response = await _consignmentService.GetPayoutsAsync(id, companyId);
        if (!response.Success)
            return await CreateHttpResponse(req, response, HttpStatusCode.NotFound);
        return await CreateHttpResponse(req, response);
    }

    [Function("ReturnConsignmentItemToCustomer")]
    [RequirePermission("consignment.edit")]
    public async Task<HttpResponseData> ReturnConsignmentItemToCustomer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "consignment/{id:int}/return")] HttpRequestData req,
        int id)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<ConsignmentItem>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }

        _logger.LogInformation("Returning consignment item {Id} to customer for company {CompanyId}", id, companyId);
        var response = await _consignmentService.ReturnToCustomerAsync(id, companyId);
        if (!response.Success)
        {
            var statusCode = IsStatusConflict(response.Message) ? HttpStatusCode.Conflict : HttpStatusCode.NotFound;
            return await CreateHttpResponse(req, response, statusCode);
        }
        return await CreateHttpResponse(req, response);
    }

    private static bool IsNotFound(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        return message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStatusConflict(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        return message.Contains("status is", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Cannot mark", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Cannot process", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Cannot return", StringComparison.OrdinalIgnoreCase)
            || message.Contains("already been processed", StringComparison.OrdinalIgnoreCase);
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

internal class MarkSoldRequest
{
    public decimal SalePrice { get; set; }
}

internal class ProcessPayoutRequest
{
    public string? Notes { get; set; }
}

internal class UpdateConsignmentItemRequest
{
    public string Description { get; set; } = string.Empty;
    public decimal AskingPrice { get; set; }
    public decimal SplitPercent { get; set; }
}
