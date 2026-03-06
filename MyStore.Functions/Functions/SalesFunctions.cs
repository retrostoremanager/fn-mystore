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

[RequirePermission("sales.view")]
public class SalesFunctions
{
    private readonly ISalesService _salesService;
    private readonly ILogger _logger;

    public SalesFunctions(ISalesService salesService, ILoggerFactory loggerFactory)
    {
        _salesService = salesService;
        _logger = loggerFactory.CreateLogger<SalesFunctions>();
    }

    [Function("GetAllSales")]
    public async Task<HttpResponseData> GetAllSales(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sales")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Getting all sales for company {CompanyId}", companyId);

            var response = await _salesService.GetAllSalesAsync(companyId);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<List<Sale>>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("GetSaleById")]
    public async Task<HttpResponseData> GetSaleById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sales/{id}")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Getting sale with ID: {Id} for company {CompanyId}", id, companyId);

            var response = await _salesService.GetSaleByIdAsync(id, companyId);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<Sale>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("GetSalesByCustomer")]
    public async Task<HttpResponseData> GetSalesByCustomer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sales/customer/{customerId}")] HttpRequestData req,
        int customerId)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Getting sales for customer ID: {CustomerId} for company {CompanyId}", customerId, companyId);

            var response = await _salesService.GetSalesByCustomerIdAsync(customerId, companyId);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<List<Sale>>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("GetSalesByDateRange")]
    public async Task<HttpResponseData> GetSalesByDateRange(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sales/date-range")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            var startDateStr = req.Query["startDate"];
            var endDateStr = req.Query["endDate"];

            if (string.IsNullOrEmpty(startDateStr) || string.IsNullOrEmpty(endDateStr))
            {
                var errorResponse = ApiResponse<List<Sale>>.ErrorResponse("Both startDate and endDate query parameters are required");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            if (!DateTime.TryParse(startDateStr, out var startDate) || !DateTime.TryParse(endDateStr, out var endDate))
            {
                var errorResponse = ApiResponse<List<Sale>>.ErrorResponse("Invalid date format. Use ISO 8601 format (e.g., 2024-01-01)");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            _logger.LogInformation("Getting sales from {StartDate} to {EndDate} for company {CompanyId}", startDate, endDate, companyId);

            var response = await _salesService.GetSalesByDateRangeAsync(startDate, endDate, companyId);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<List<Sale>>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("CreateSale")]
    [RequirePermission("sales.create")]
    public async Task<HttpResponseData> CreateSale(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sales")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Creating new sale for company {CompanyId}", companyId);

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<CreateSaleRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null)
            {
                var errorResponse = ApiResponse<Sale>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var response = await _salesService.CreateSaleAsync(request, companyId);
            var statusCode = response.Success ? HttpStatusCode.Created : HttpStatusCode.BadRequest;
            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<Sale>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("DeleteSale")]
    [RequirePermission("sales.refund")]
    public async Task<HttpResponseData> DeleteSale(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "sales/{id}")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Deleting sale with ID: {Id} for company {CompanyId}", id, companyId);

            var response = await _salesService.DeleteSaleAsync(id, companyId);
            var statusCode = response.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound;
            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<bool>.ErrorResponse(ex.Message);
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

