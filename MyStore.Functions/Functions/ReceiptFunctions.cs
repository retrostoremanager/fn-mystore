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
public class ReceiptFunctions
{
    private readonly IReceiptService _receiptService;
    private readonly ILogger _logger;

    public ReceiptFunctions(IReceiptService receiptService, ILoggerFactory loggerFactory)
    {
        _receiptService = receiptService;
        _logger = loggerFactory.CreateLogger<ReceiptFunctions>();
    }

    [Function("GetSaleReceipt")]
    public async Task<HttpResponseData> GetSaleReceipt(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sales/{id:int}/receipt")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Getting receipt for sale {SaleId} for company {CompanyId}", id, companyId);

            var response = await _receiptService.GetReceiptAsync(id, companyId);
            var statusCode = response.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound;
            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<ReceiptResponse>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in GetSaleReceipt");
            var errorResponse = ApiResponse<ReceiptResponse>.ErrorResponse("An unexpected error occurred");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    [Function("EmailSaleReceipt")]
    public async Task<HttpResponseData> EmailSaleReceipt(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sales/{id:int}/receipt/email")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            _logger.LogInformation("Sending receipt email for sale {SaleId} for company {CompanyId}", id, companyId);

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var emailRequest = JsonSerializer.Deserialize<SendReceiptEmailRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (emailRequest == null || string.IsNullOrWhiteSpace(emailRequest.Email))
            {
                var errorResponse = ApiResponse<bool>.ErrorResponse("email is required");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            if (!IsValidEmail(emailRequest.Email))
            {
                var errorResponse = ApiResponse<bool>.ErrorResponse("email is not a valid email address");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var response = await _receiptService.SendReceiptEmailAsync(id, companyId, emailRequest.Email);
            if (!response.Success)
            {
                var statusCode = response.Message?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true
                    ? HttpStatusCode.NotFound
                    : HttpStatusCode.BadRequest;
                return await CreateHttpResponse(req, response, statusCode);
            }

            return await CreateHttpResponse(req, response, HttpStatusCode.OK);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<bool>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
        catch (JsonException)
        {
            var errorResponse = ApiResponse<bool>.ErrorResponse("Invalid request body");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in EmailSaleReceipt");
            var errorResponse = ApiResponse<bool>.ErrorResponse("An unexpected error occurred");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        if (email != email.Trim()) return false;
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            if (addr.Address != email) return false;
            var atIndex = email.LastIndexOf('@');
            if (atIndex <= 0 || atIndex >= email.Length - 1) return false;
            var domain = email.Substring(atIndex + 1);
            return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.');
        }
        catch
        {
            return false;
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
