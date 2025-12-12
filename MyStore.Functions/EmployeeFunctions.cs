using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MyStore.Models;
using MyStore.Services;

namespace MyStore.Functions;

public class EmployeeFunctions
{
    private readonly IEmployeeService _employeeService;
    private readonly ILogger _logger;

    public EmployeeFunctions(IEmployeeService employeeService, ILoggerFactory loggerFactory)
    {
        _employeeService = employeeService;
        _logger = loggerFactory.CreateLogger<EmployeeFunctions>();
    }

    [Function("GetAllEmployees")]
    public async Task<HttpResponseData> GetAllEmployees(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "employees")] HttpRequestData req)
    {
        _logger.LogInformation("Getting all employees");

        var response = await _employeeService.GetAllEmployeesAsync();
        return await CreateHttpResponse(req, response);
    }

    [Function("GetEmployeeById")]
    public async Task<HttpResponseData> GetEmployeeById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "employees/{id}")] HttpRequestData req,
        int id)
    {
        _logger.LogInformation("Getting employee with ID: {Id}", id);

        var response = await _employeeService.GetEmployeeByIdAsync(id);
        return await CreateHttpResponse(req, response);
    }

    [Function("CreateEmployee")]
    public async Task<HttpResponseData> CreateEmployee(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "employees")] HttpRequestData req)
    {
        _logger.LogInformation("Creating new employee");

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        var request = JsonSerializer.Deserialize<CreateEmployeeRequest>(requestBody, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (request == null)
        {
            var errorResponse = ApiResponse<Employee>.ErrorResponse("Invalid request body");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        var response = await _employeeService.CreateEmployeeAsync(request);
        var statusCode = response.Success ? HttpStatusCode.Created : HttpStatusCode.BadRequest;
        return await CreateHttpResponse(req, response, statusCode);
    }

    [Function("UpdateEmployee")]
    public async Task<HttpResponseData> UpdateEmployee(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "employees/{id}")] HttpRequestData req,
        int id)
    {
        _logger.LogInformation("Updating employee with ID: {Id}", id);

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        var request = JsonSerializer.Deserialize<UpdateEmployeeRequest>(requestBody, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (request == null)
        {
            var errorResponse = ApiResponse<Employee>.ErrorResponse("Invalid request body");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        var response = await _employeeService.UpdateEmployeeAsync(id, request);
        var statusCode = response.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound;
        return await CreateHttpResponse(req, response, statusCode);
    }

    [Function("DeleteEmployee")]
    public async Task<HttpResponseData> DeleteEmployee(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "employees/{id}")] HttpRequestData req,
        int id)
    {
        _logger.LogInformation("Deleting employee with ID: {Id}", id);

        var response = await _employeeService.DeleteEmployeeAsync(id);
        var statusCode = response.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound;
        return await CreateHttpResponse(req, response, statusCode);
    }

    [Function("SearchEmployees")]
    public async Task<HttpResponseData> SearchEmployees(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "employees/search")] HttpRequestData req)
    {
        var query = req.Query["q"];
        if (string.IsNullOrEmpty(query))
        {
            var errorResponse = ApiResponse<List<Employee>>.ErrorResponse("Search query parameter 'q' is required");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }

        _logger.LogInformation("Searching employees with query: {Query}", query);

        var response = await _employeeService.SearchEmployeesAsync(query);
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

