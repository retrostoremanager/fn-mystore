using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MyStore.Functions.Attributes;
using MyStore.Functions.Helpers;
using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Functions;

[RequirePermission("users.view")]
public class RoleFunctions
{
    private readonly IRoleRepository _roleRepository;
    private readonly ILogger _logger;

    public RoleFunctions(IRoleRepository roleRepository, ILoggerFactory loggerFactory)
    {
        _roleRepository = roleRepository;
        _logger = loggerFactory.CreateLogger<RoleFunctions>();
    }

    [Function("GetAllRoles")]
    public async Task<HttpResponseData> GetAllRoles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "roles")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Getting roles for company {CompanyId}", companyId);

            var roles = await _roleRepository.GetForCompanyAsync(companyId);
            var response = ApiResponse<List<Role>>.SuccessResponse(roles);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<List<Role>>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("GetRoleById")]
    public async Task<HttpResponseData> GetRoleById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "roles/{id}")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Getting role with ID: {Id} for company {CompanyId}", id, companyId);

            var role = await _roleRepository.GetByIdAsync(id, companyId);
            if (role == null)
            {
                var errorResponse = ApiResponse<Role>.ErrorResponse($"Role with ID {id} not found");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.NotFound);
            }

            var response = ApiResponse<Role>.SuccessResponse(role);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<Role>.ErrorResponse(ex.Message);
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
