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
    private readonly IUserService _userService;
    private readonly ILogger _logger;

    public AccountFunctions(ICompanyService companyService, IUserService userService, ILoggerFactory loggerFactory)
    {
        _companyService = companyService;
        _userService = userService;
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

    /// <summary>
    /// GET /api/accounts/company-by-slug/{slug} - Get company info by slug for login page display.
    /// Returns 404 if company not found.
    /// </summary>
    [Function("GetCompanyBySlug")]
    public async Task<HttpResponseData> GetCompanyBySlug(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "accounts/company-by-slug/{slug}")] HttpRequestData req,
        string slug)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                var errorResponse = ApiResponse<CompanyBySlugResponse?>.ErrorResponse("Company slug is required.");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var response = await _companyService.GetCompanyBySlugAsync(slug);

            var statusCode = response.Success
                ? (response.Data != null ? HttpStatusCode.OK : HttpStatusCode.NotFound)
                : HttpStatusCode.NotFound;

            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting company by slug {Slug}", slug);
            var errorResponse = ApiResponse<CompanyBySlugResponse?>.ErrorResponse(
                "An error occurred while looking up the company.");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    [Function("Login")]
    public async Task<HttpResponseData> Login(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "accounts/login")] HttpRequestData req)
    {
        try
        {
            _logger.LogInformation("Processing login request");

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            LoginRequest? request;

            try
            {
                request = JsonSerializer.Deserialize<LoginRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException)
            {
                var errorResponse = ApiResponse<LoginResponse>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            if (request == null)
            {
                var errorResponse = ApiResponse<LoginResponse>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var response = await _companyService.LoginAsync(request);

            var statusCode = response.Success ? HttpStatusCode.OK : HttpStatusCode.Unauthorized;
            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing login");
            var errorResponse = ApiResponse<LoginResponse>.ErrorResponse(
                "An error occurred during sign-in. Please try again.",
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

    /// <summary>
    /// HTTP function to resend verification email for unverified accounts.
    /// POST /api/accounts/resend-verification
    /// </summary>
    /// <param name="req">The HTTP request containing the email address in the request body.</param>
    /// <returns>
    /// HTTP response with status codes:
    /// - 200 OK: Verification email sent successfully
    /// - 400 Bad Request: Invalid request body or account already verified
    /// - 429 Too Many Requests: Rate limit exceeded (max 3 per hour)
    /// - 500 Internal Server Error: Unexpected error occurred
    /// </returns>
    /// <remarks>
    /// Request body should be JSON: { "email": "user@example.com" }
    /// Implements rate limiting to prevent abuse (max 3 requests per hour per email).
    /// </remarks>
    [Function("ResendVerification")]
    public async Task<HttpResponseData> ResendVerification(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "accounts/resend-verification")] HttpRequestData req)
    {
        try
        {
            _logger.LogInformation("Resending verification email");

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            ResendVerificationEmailRequest? request;
            
            try
            {
                request = JsonSerializer.Deserialize<ResendVerificationEmailRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException)
            {
                var errorResponse = ApiResponse<ResendVerificationEmailResponse>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            if (request == null)
            {
                var errorResponse = ApiResponse<ResendVerificationEmailResponse>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var response = await _companyService.ResendVerificationEmailAsync(request);

            // Determine appropriate HTTP status code based on response
            HttpStatusCode statusCode;
            if (response.Success)
            {
                statusCode = HttpStatusCode.OK;
            }
            else if (response.Errors?.Any(e => e.Contains("Rate limit", StringComparison.OrdinalIgnoreCase)) == true)
            {
                statusCode = HttpStatusCode.TooManyRequests; // 429 Too Many Requests for rate limiting
            }
            else if (response.Errors?.Any(e => e.Contains("already verified", StringComparison.OrdinalIgnoreCase)) == true)
            {
                statusCode = HttpStatusCode.BadRequest; // 400 Bad Request for already verified
            }
            else
            {
                statusCode = HttpStatusCode.BadRequest; // 400 Bad Request for other errors
            }

            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resending verification email");
            var errorResponse = ApiResponse<ResendVerificationEmailResponse>.ErrorResponse(
                "An unexpected error occurred while processing your request. Please try again later.",
                new List<string> { ex.Message }
            );
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// POST /api/accounts/forgot-password - Request password reset email.
    /// Always returns generic success (do not reveal if email exists).
    /// </summary>
    [Function("ForgotPassword")]
    public async Task<HttpResponseData> ForgotPassword(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "accounts/forgot-password")] HttpRequestData req)
    {
        try
        {
            _logger.LogInformation("Processing forgot password request");

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            ForgotPasswordRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<ForgotPasswordRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                var errorResponse = ApiResponse<ForgotPasswordResponse>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            if (request == null)
            {
                var errorResponse = ApiResponse<ForgotPasswordResponse>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var response = await _companyService.ForgotPasswordAsync(request);

            HttpStatusCode statusCode = response.Success
                ? HttpStatusCode.OK
                : (response.Errors?.Any(e => e.Contains("Rate limit", StringComparison.OrdinalIgnoreCase)) == true
                    ? HttpStatusCode.TooManyRequests
                    : HttpStatusCode.BadRequest);

            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing forgot password");
            var errorResponse = ApiResponse<ForgotPasswordResponse>.ErrorResponse(
                "An unexpected error occurred. Please try again later.",
                new List<string> { ex.Message }
            );
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// POST /api/accounts/reset-password - Complete password reset with token.
    /// </summary>
    [Function("ResetPassword")]
    public async Task<HttpResponseData> ResetPassword(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "accounts/reset-password")] HttpRequestData req)
    {
        try
        {
            _logger.LogInformation("Processing reset password request");

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            ResetPasswordRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<ResetPasswordRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                var errorResponse = ApiResponse<ResetPasswordResponse>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            if (request == null)
            {
                var errorResponse = ApiResponse<ResetPasswordResponse>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var response = await _companyService.ResetPasswordAsync(request);

            HttpStatusCode statusCode;
            if (response.Success)
            {
                statusCode = HttpStatusCode.OK;
            }
            else if (response.Errors?.Any(e => e.Contains("expired", StringComparison.OrdinalIgnoreCase)) == true)
            {
                statusCode = HttpStatusCode.Gone;
            }
            else if (response.Errors?.Any(e => e.Contains("Invalid", StringComparison.OrdinalIgnoreCase)) == true)
            {
                statusCode = HttpStatusCode.BadRequest;
            }
            else if (response.FieldErrors != null && response.FieldErrors.Count > 0)
            {
                statusCode = HttpStatusCode.BadRequest;
            }
            else
            {
                statusCode = HttpStatusCode.BadRequest;
            }

            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing reset password");
            var errorResponse = ApiResponse<ResetPasswordResponse>.ErrorResponse(
                "An unexpected error occurred. Please try again later.",
                new List<string> { ex.Message }
            );
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// POST /api/accounts/set-password-invite - Set password from user invite (employee onboarding).
    /// Returns company slug for redirect to /c/{slug}/login.
    /// </summary>
    [Function("SetPasswordForInvite")]
    public async Task<HttpResponseData> SetPasswordForInvite(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "accounts/set-password-invite")] HttpRequestData req)
    {
        try
        {
            _logger.LogInformation("Processing set password from invite request");

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            SetPasswordFromInviteRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<SetPasswordFromInviteRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                var errorResponse = ApiResponse<SetPasswordFromInviteResponse>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            if (request == null)
            {
                var errorResponse = ApiResponse<SetPasswordFromInviteResponse>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var response = await _userService.SetPasswordFromInviteAsync(request);

            var statusCode = response.Success ? HttpStatusCode.OK : HttpStatusCode.BadRequest;
            if (!response.Success && response.Errors?.Any(e => e.Contains("expired", StringComparison.OrdinalIgnoreCase) || e.Contains("Invalid", StringComparison.OrdinalIgnoreCase)) == true)
            {
                statusCode = HttpStatusCode.Gone; // 410 for expired/invalid token
            }

            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing set password from invite");
            var errorResponse = ApiResponse<SetPasswordFromInviteResponse>.ErrorResponse(
                "An unexpected error occurred. Please try again later.",
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
