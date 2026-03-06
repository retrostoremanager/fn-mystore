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

[RequirePermission("inventory.view")] // Minimum for GET; create/edit/delete require more - use method-level for those
public class InventoryFunctions
{
    private readonly IInventoryService _inventoryService;
    private readonly ILogger _logger;

    public InventoryFunctions(IInventoryService inventoryService, ILoggerFactory loggerFactory)
    {
        _inventoryService = inventoryService;
        _logger = loggerFactory.CreateLogger<InventoryFunctions>();
    }

    [Function("GetAllInventory")]
    public async Task<HttpResponseData> GetAllInventory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Getting all inventory items for company {CompanyId}", companyId);

            var response = await _inventoryService.GetAllInventoryAsync(companyId);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<List<InventoryItem>>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("GetInventoryById")]
    public async Task<HttpResponseData> GetInventoryById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/{id}")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Getting inventory item with ID: {Id} for company {CompanyId}", id, companyId);

            var response = await _inventoryService.GetInventoryByIdAsync(id, companyId);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<InventoryItem>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("CreateInventoryItem")]
    [RequirePermission("inventory.create")]
    public async Task<HttpResponseData> CreateInventoryItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Creating new inventory item for company {CompanyId}", companyId);

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<CreateInventoryItemRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null)
            {
                var errorResponse = ApiResponse<InventoryItem>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var response = await _inventoryService.CreateInventoryItemAsync(request, companyId);
            var statusCode = response.Success ? HttpStatusCode.Created : HttpStatusCode.BadRequest;
            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<InventoryItem>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("UpdateInventoryItem")]
    [RequirePermission("inventory.edit")]
    public async Task<HttpResponseData> UpdateInventoryItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "inventory/{id}")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Updating inventory item with ID: {Id} for company {CompanyId}", id, companyId);

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<UpdateInventoryItemRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null)
            {
                var errorResponse = ApiResponse<InventoryItem>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var response = await _inventoryService.UpdateInventoryItemAsync(id, request, companyId);
            var statusCode = response.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound;
            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<InventoryItem>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("DeleteInventoryItem")]
    [RequirePermission("inventory.delete")]
    public async Task<HttpResponseData> DeleteInventoryItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "inventory/{id}")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Deleting inventory item with ID: {Id} for company {CompanyId}", id, companyId);

            var response = await _inventoryService.DeleteInventoryItemAsync(id, companyId);
            var statusCode = response.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound;
            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<bool>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("SearchInventory")]
    public async Task<HttpResponseData> SearchInventory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/search")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            var query = req.Query["q"];
            if (string.IsNullOrEmpty(query))
            {
                var errorResponse = ApiResponse<List<InventoryItem>>.ErrorResponse("Search query parameter 'q' is required");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            _logger.LogInformation("Searching inventory with query: {Query} for company {CompanyId}", query, companyId);

            var response = await _inventoryService.SearchInventoryAsync(query, companyId);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<List<InventoryItem>>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    private static async Task<HttpResponseData> CreateHttpResponse<T>(
        HttpRequestData req,
        ApiResponse<T> apiResponse,
        HttpStatusCode? statusCode = null)
    {
        var response = req.CreateResponse(statusCode ?? (apiResponse.Success ? HttpStatusCode.OK : HttpStatusCode.BadRequest));
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");

        var json = JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await response.WriteStringAsync(json);
        return response;
    }
}

