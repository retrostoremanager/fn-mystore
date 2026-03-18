using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MyStore.Functions.Attributes;
using MyStore.Functions.Helpers;
using MyStore.Models;
using MyStore.Services;

namespace MyStore.Functions;

[RequirePermission("users.view")]
public class UserFunctions
{
    private readonly IUserService _userService;
    private readonly ILogger _logger;

    public UserFunctions(IUserService userService, ILoggerFactory loggerFactory)
    {
        _userService = userService;
        _logger = loggerFactory.CreateLogger<UserFunctions>();
    }

    [Function("GetAllUsers")]
    public async Task<HttpResponseData> GetAllUsers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Getting all users for company {CompanyId}", companyId);

            var response = await _userService.GetAllUsersAsync(companyId);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<List<User>>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("GetUserById")]
    public async Task<HttpResponseData> GetUserById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/{id}")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Getting user with ID: {Id} for company {CompanyId}", id, companyId);

            var response = await _userService.GetUserByIdAsync(id, companyId);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<User>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("CreateUser")]
    [RequirePermission("users.manage")]
    public async Task<HttpResponseData> CreateUser(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Creating new user for company {CompanyId}", companyId);

            string body;
            using (var reader = new StreamReader(req.Body, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync();
            }

            var request = JsonSerializer.Deserialize<CreateUserRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null)
            {
                var errorResponse = ApiResponse<User>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var response = await _userService.CreateUserAsync(request, companyId);
            var statusCode = response.Success ? HttpStatusCode.Created : HttpStatusCode.BadRequest;
            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<User>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("ResendInvite")]
    [RequirePermission("users.manage")]
    public async Task<HttpResponseData> ResendInvite(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/{id}/resend-invite")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Resending invite for user {Id} in company {CompanyId}", id, companyId);

            var response = await _userService.ResendInviteAsync(id, companyId);
            var statusCode = response.Success ? HttpStatusCode.OK : HttpStatusCode.BadRequest;
            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<bool>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("UpdateUser")]
    [RequirePermission("users.manage")]
    public async Task<HttpResponseData> UpdateUser(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users/{id}")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Updating user with ID: {Id} for company {CompanyId}", id, companyId);

            string body;
            using (var reader = new StreamReader(req.Body, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync();
            }

            var request = JsonSerializer.Deserialize<UpdateUserRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null)
            {
                var errorResponse = ApiResponse<User>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var response = await _userService.UpdateUserAsync(id, request, companyId);
            var statusCode = response.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound;
            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<User>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("DeleteUser")]
    [RequirePermission("users.remove")]
    public async Task<HttpResponseData> DeleteUser(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "users/{id}")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Removing user with ID: {Id} for company {CompanyId}", id, companyId);

            var response = await _userService.DeleteUserAsync(id, companyId);
            var statusCode = response.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound;
            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<bool>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("SearchUsers")]
    public async Task<HttpResponseData> SearchUsers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/search")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            var query = req.Query["q"];
            if (string.IsNullOrEmpty(query))
            {
                var errorResponse = ApiResponse<List<User>>.ErrorResponse("Search query parameter 'q' is required");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            _logger.LogInformation("Searching users with query: {Query} for company {CompanyId}", query, companyId);

            var response = await _userService.SearchUsersAsync(query, companyId);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<List<User>>.ErrorResponse(ex.Message);
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
