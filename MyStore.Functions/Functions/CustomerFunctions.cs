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

[RequirePermission("customers.view")]
public class CustomerFunctions
{
    private readonly ICustomerService _customerService;
    private readonly ILogger _logger;

    public CustomerFunctions(ICustomerService customerService, ILoggerFactory loggerFactory)
    {
        _customerService = customerService;
        _logger = loggerFactory.CreateLogger<CustomerFunctions>();
    }

    [Function("GetAllCustomers")]
    public async Task<HttpResponseData> GetAllCustomers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customers")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Getting all customers for company {CompanyId}", companyId);

            var response = await _customerService.GetAllCustomersAsync(companyId);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<List<Customer>>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Forbidden);
        }
    }

    [Function("GetCustomerById")]
    public async Task<HttpResponseData> GetCustomerById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customers/{id}")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Getting customer with ID: {Id} for company {CompanyId}", id, companyId);

            var response = await _customerService.GetCustomerByIdAsync(id, companyId);
            if (!response.Success)
                return await CreateHttpResponse(req, response, HttpStatusCode.NotFound);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<Customer>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Forbidden);
        }
    }

    [Function("CreateCustomer")]
    [RequirePermission("customers.edit")]
    public async Task<HttpResponseData> CreateCustomer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "customers")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Creating new customer for company {CompanyId}", companyId);

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<CreateCustomerRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null)
            {
                var errorResponse = ApiResponse<Customer>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var response = await _customerService.CreateCustomerAsync(request, companyId);
            var statusCode = response.Success ? HttpStatusCode.Created : HttpStatusCode.BadRequest;
            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<Customer>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Forbidden);
        }
    }

    [Function("UpdateCustomer")]
    [RequirePermission("customers.edit")]
    public async Task<HttpResponseData> UpdateCustomer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "customers/{id}")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Updating customer with ID: {Id} for company {CompanyId}", id, companyId);

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<UpdateCustomerRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null)
            {
                var errorResponse = ApiResponse<Customer>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var response = await _customerService.UpdateCustomerAsync(id, request, companyId);
            if (!response.Success)
                return await CreateHttpResponse(req, response, HttpStatusCode.NotFound);
            return await CreateHttpResponse(req, response, HttpStatusCode.OK);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<Customer>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Forbidden);
        }
    }

    [Function("DeleteCustomer")]
    [RequirePermission("customers.edit")]
    public async Task<HttpResponseData> DeleteCustomer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "customers/{id}")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Deleting customer with ID: {Id} for company {CompanyId}", id, companyId);

            var response = await _customerService.DeleteCustomerAsync(id, companyId);
            var statusCode = response.Success ? HttpStatusCode.NoContent : HttpStatusCode.NotFound;
            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<bool>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Forbidden);
        }
    }

    [Function("SearchCustomers")]
    public async Task<HttpResponseData> SearchCustomers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customers/search")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            var query = req.Query["q"];
            if (string.IsNullOrEmpty(query))
            {
                var errorResponse = ApiResponse<List<Customer>>.ErrorResponse("Search query parameter 'q' is required");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            _logger.LogInformation("Searching customers with query: {Query} for company {CompanyId}", query, companyId);

            var response = await _customerService.SearchCustomersAsync(query, companyId);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<List<Customer>>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Forbidden);
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

