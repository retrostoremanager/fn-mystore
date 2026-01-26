using System.Linq;
using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MyStore.Functions.Helpers;
using MyStore.Models;
using MyStore.Services;

namespace MyStore.Functions;

public class AccountFunctions
{
    private readonly ICompanyService _companyService;
    private readonly ILogger _logger;

    public AccountFunctions(ICompanyService companyService, ILoggerFactory loggerFactory)
    {
        _companyService = companyService;
        _logger = loggerFactory.CreateLogger<AccountFunctions>();
    }

    [Function("RegisterAccount")]
    public async Task<HttpResponseData> RegisterAccount(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "accounts/register")] HttpRequestData req)
    {
        try
        {
            _logger.LogInformation("Registering new account");

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            RegisterAccountRequest? request;
            
            try
            {
                request = JsonSerializer.Deserialize<RegisterAccountRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException)
            {
                var errorResponse = ApiResponse<RegisterAccountResponse>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            if (request == null)
            {
                var errorResponse = ApiResponse<RegisterAccountResponse>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var response = await _companyService.RegisterAccountAsync(request);

            // Handle duplicate email case - return 409 Conflict
            // Check both message and field errors for duplicate email
            if (!response.Success && 
                (response.Message?.Contains("already exists", StringComparison.OrdinalIgnoreCase) == true ||
                 response.FieldErrors?.ContainsKey("email") == true && 
                 response.FieldErrors["email"].Any(e => e.Contains("already registered", StringComparison.OrdinalIgnoreCase))))
            {
                return await CreateHttpResponse(req, response, HttpStatusCode.Conflict);
            }

            // Return 201 Created for successful registration
            // Return 400 Bad Request for validation errors (field-level errors)
            var statusCode = response.Success 
                ? HttpStatusCode.Created 
                : (response.FieldErrors != null && response.FieldErrors.Count > 0 
                    ? HttpStatusCode.BadRequest 
                    : HttpStatusCode.BadRequest);
            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering account");
            var errorResponse = ApiResponse<RegisterAccountResponse>.ErrorResponse(
                "An error occurred while registering the account",
                new List<string> { ex.Message }
            );
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    [Function("VerifyEmail")]
    public async Task<HttpResponseData> VerifyEmail(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "accounts/verify-email")] HttpRequestData req)
    {
        try
        {
            // Extract token from query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var token = query["token"];

            _logger.LogInformation("Verifying email with token");

            if (string.IsNullOrWhiteSpace(token))
            {
                var errorResponse = ApiResponse<VerifyEmailResponse>.ErrorResponse(
                    "Invalid verification link. The token is missing.",
                    new List<string> { "Token parameter is required" }
                );
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var response = await _companyService.VerifyEmailAsync(token);

            // Determine appropriate HTTP status code based on response
            HttpStatusCode statusCode;
            if (response.Success)
            {
                statusCode = HttpStatusCode.OK;
            }
            else if (response.Errors?.Any(e => e.Contains("expired", StringComparison.OrdinalIgnoreCase)) == true)
            {
                statusCode = HttpStatusCode.Gone; // 410 Gone for expired tokens
            }
            else if (response.Errors?.Any(e => e.Contains("Invalid token", StringComparison.OrdinalIgnoreCase)) == true)
            {
                statusCode = HttpStatusCode.NotFound; // 404 Not Found for invalid tokens
            }
            else
            {
                statusCode = HttpStatusCode.BadRequest; // 400 Bad Request for other errors
            }

            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying email");
            var errorResponse = ApiResponse<VerifyEmailResponse>.ErrorResponse(
                "An unexpected error occurred while verifying your email. Please try again later.",
                new List<string> { ex.Message }
            );
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
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
