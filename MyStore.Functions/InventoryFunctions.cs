using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MyStore.Models;
using MyStore.Services;

namespace MyStore.Functions;

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
        _logger.LogInformation("Getting all inventory items");

        var response = await _inventoryService.GetAllInventoryAsync();
        return await CreateHttpResponse(req, response);
    }

    [Function("GetInventoryById")]
    public async Task<HttpResponseData> GetInventoryById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/{id}")] HttpRequestData req,
        int id)
    {
        _logger.LogInformation("Getting inventory item with ID: {Id}", id);

        var response = await _inventoryService.GetInventoryByIdAsync(id);
        return await CreateHttpResponse(req, response);
    }

    [Function("CreateInventoryItem")]
    public async Task<HttpResponseData> CreateInventoryItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory")] HttpRequestData req)
    {
        _logger.LogInformation("Creating new inventory item");

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

        var response = await _inventoryService.CreateInventoryItemAsync(request);
        var statusCode = response.Success ? HttpStatusCode.Created : HttpStatusCode.BadRequest;
        return await CreateHttpResponse(req, response, statusCode);
    }

    [Function("UpdateInventoryItem")]
    public async Task<HttpResponseData> UpdateInventoryItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "inventory/{id}")] HttpRequestData req,
        int id)
    {
        _logger.LogInformation("Updating inventory item with ID: {Id}", id);

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

        var response = await _inventoryService.UpdateInventoryItemAsync(id, request);
        var statusCode = response.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound;
        return await CreateHttpResponse(req, response, statusCode);
    }

    [Function("DeleteInventoryItem")]
    public async Task<HttpResponseData> DeleteInventoryItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "inventory/{id}")] HttpRequestData req,
        int id)
    {
        _logger.LogInformation("Deleting inventory item with ID: {Id}", id);

        var response = await _inventoryService.DeleteInventoryItemAsync(id);
        var statusCode = response.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound;
        return await CreateHttpResponse(req, response, statusCode);
    }

    [Function("SearchInventory")]
    public async Task<HttpResponseData> SearchInventory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/search")] HttpRequestData req)
    {
        var query = req.Query["q"];
        if (string.IsNullOrEmpty(query))
        {
            var errorResponse = ApiResponse<List<InventoryItem>>.ErrorResponse("Search query parameter 'q' is required");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        _logger.LogInformation("Searching inventory with query: {Query}", query);

        var response = await _inventoryService.SearchInventoryAsync(query);
        return await CreateHttpResponse(req, response);
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

