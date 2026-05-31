using System.Net;
using System.Text;
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

    [Function("GetAvailablePermissions")]
    [RequirePermission("users.manage")]
    public async Task<HttpResponseData> GetAvailablePermissions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "permissions/available")] HttpRequestData req)
    {
        try
        {
            _ = CompanyHelper.GetCompanyIdRequired(req);
            var permissions = await _roleRepository.GetAllPermissionsAsync();
            var response = ApiResponse<List<PermissionInfo>>.SuccessResponse(permissions);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<List<PermissionInfo>>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
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

    [Function("CreateRole")]
    [RequirePermission("users.manage")]
    public async Task<HttpResponseData> CreateRole(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "roles")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Creating custom role for company {CompanyId}", companyId);

            string body;
            using (var reader = new StreamReader(req.Body, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync();
            }

            var request = JsonSerializer.Deserialize<CreateRoleRequest>(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
            {
                var err = ApiResponse<Role>.ErrorResponse("Name is required");
                return await CreateHttpResponse(req, err, HttpStatusCode.BadRequest);
            }

            var role = new Role
            {
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                CompanyId = companyId
            };

            var created = await _roleRepository.CreateAsync(role, companyId);
            await _roleRepository.AssignPermissionsAsync(created.Id, request.Permissions ?? new List<string>());
            var createdWithPerms = await _roleRepository.GetByIdAsync(created.Id, companyId);

            var response = ApiResponse<Role>.SuccessResponse(createdWithPerms!);
            return await CreateHttpResponse(req, response, HttpStatusCode.Created);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<Role>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("UpdateRole")]
    [RequirePermission("users.manage")]
    public async Task<HttpResponseData> UpdateRole(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "roles/{id}")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Updating role {Id} for company {CompanyId}", id, companyId);

            var existing = await _roleRepository.GetByIdAsync(id, companyId);
            if (existing == null)
            {
                var err = ApiResponse<Role>.ErrorResponse("Role not found");
                return await CreateHttpResponse(req, err, HttpStatusCode.NotFound);
            }
            if (existing.IsSystemRole)
            {
                var err = ApiResponse<Role>.ErrorResponse("System roles cannot be modified");
                return await CreateHttpResponse(req, err, HttpStatusCode.BadRequest);
            }

            string body;
            using (var reader = new StreamReader(req.Body, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync();
            }

            var request = JsonSerializer.Deserialize<UpdateRoleRequest>(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (request == null)
            {
                var err = ApiResponse<Role>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, err, HttpStatusCode.BadRequest);
            }

            var name = !string.IsNullOrWhiteSpace(request.Name) ? request.Name.Trim() : existing.Name;
            var description = request.Description != null ? request.Description.Trim() : existing.Description;

            var updated = await _roleRepository.UpdateAsync(id, name, description, companyId);
            if (updated != null && request.Permissions != null)
            {
                await _roleRepository.AssignPermissionsAsync(id, request.Permissions);
            }
            var final = updated != null ? await _roleRepository.GetByIdAsync(id, companyId) : null;

            var response = ApiResponse<Role>.SuccessResponse(final!);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<Role>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    [Function("DeleteRole")]
    [RequirePermission("users.manage")]
    public async Task<HttpResponseData> DeleteRole(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "roles/{id}")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Deleting role {Id} for company {CompanyId}", id, companyId);

            var existing = await _roleRepository.GetByIdAsync(id, companyId);
            if (existing == null)
            {
                var err = ApiResponse<object>.ErrorResponse("Role not found");
                return await CreateHttpResponse(req, err, HttpStatusCode.NotFound);
            }
            if (existing.IsSystemRole)
            {
                var err = ApiResponse<object>.ErrorResponse("System roles cannot be deleted");
                return await CreateHttpResponse(req, err, HttpStatusCode.BadRequest);
            }

            var deleted = await _roleRepository.DeleteAsync(id, companyId);
            if (!deleted)
            {
                var err = ApiResponse<object>.ErrorResponse("Cannot delete role: one or more users are assigned to it");
                return await CreateHttpResponse(req, err, HttpStatusCode.BadRequest);
            }

            var response = req.CreateResponse(HttpStatusCode.NoContent);
            return response;
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<object>.ErrorResponse(ex.Message);
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
