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
