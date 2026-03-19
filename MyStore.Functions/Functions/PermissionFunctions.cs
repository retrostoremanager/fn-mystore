using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MyStore.Functions.Helpers;
using MyStore.Models;
using MyStore.Services;

namespace MyStore.Functions;

/// <summary>
/// Returns current user's permissions. No RequirePermission - any authenticated user can call.
/// Used by frontend for UI filtering (dashboard sections, nav, etc.).
/// </summary>
public class PermissionFunctions
{
    private readonly IPermissionService _permissionService;
    private readonly ILogger _logger;

    public PermissionFunctions(IPermissionService permissionService, ILoggerFactory loggerFactory)
    {
        _permissionService = permissionService;
        _logger = loggerFactory.CreateLogger<PermissionFunctions>();
    }

    [Function("GetMyPermissions")]
    public async Task<HttpResponseData> GetMyPermissions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "permissions")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            var email = CompanyHelper.GetEmailFromContext(context) ?? CompanyHelper.GetEmailFromJwt(req);
            if (string.IsNullOrEmpty(email))
            {
                var errorResponse = ApiResponse<List<string>>.ErrorResponse("Authentication required.");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
            }

            var permissions = await _permissionService.GetPermissionsAsync(companyId, email);
            var list = permissions.ToList();
            var response = ApiResponse<List<string>>.SuccessResponse(list);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<List<string>>.ErrorResponse(ex.Message);
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
