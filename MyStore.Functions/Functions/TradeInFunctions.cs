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

[RequirePermission("trade_in.view")]
public class TradeInFunctions
{
    private readonly ITradeInService _tradeInService;
    private readonly ILogger _logger;

    public TradeInFunctions(ITradeInService tradeInService, ILoggerFactory loggerFactory)
    {
        _tradeInService = tradeInService;
        _logger = loggerFactory.CreateLogger<TradeInFunctions>();
    }

    [Function("GetAllTradeIns")]
    public async Task<HttpResponseData> GetAllTradeIns(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "trade-ins")] HttpRequestData req)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            return await CreateHttpResponse(req, ApiResponse<List<TradeIn>>.ErrorResponse(ex.Message), HttpStatusCode.Unauthorized);
        }

        var status = req.Query["status"];
        DateTime? dateFrom = null;
        DateTime? dateTo = null;

        var dateFromStr = req.Query["dateFrom"];
        var dateToStr = req.Query["dateTo"];

        if (!string.IsNullOrWhiteSpace(dateFromStr) && DateTime.TryParse(dateFromStr, out var parsedFrom))
            dateFrom = parsedFrom;

        if (!string.IsNullOrWhiteSpace(dateToStr) && DateTime.TryParse(dateToStr, out var parsedTo))
            dateTo = parsedTo;

        _logger.LogInformation("Getting all trade-ins for company {CompanyId}", companyId);
        var response = await _tradeInService.GetAllAsync(companyId, status, dateFrom, dateTo);
        return await CreateHttpResponse(req, response);
    }

    [Function("CreateTradeIn")]
    [RequirePermission("trade_in.create")]
    public async Task<HttpResponseData> CreateTradeIn(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "trade-ins")] HttpRequestData req)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            return await CreateHttpResponse(req, ApiResponse<TradeIn>.ErrorResponse(ex.Message), HttpStatusCode.Unauthorized);
        }

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        TradeIn? tradeIn;
        try
        {
            tradeIn = JsonSerializer.Deserialize<TradeIn>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return await CreateHttpResponse(req, ApiResponse<TradeIn>.ErrorResponse("Invalid request body"), HttpStatusCode.BadRequest);
        }

        if (tradeIn == null)
            return await CreateHttpResponse(req, ApiResponse<TradeIn>.ErrorResponse("Invalid request body"), HttpStatusCode.BadRequest);

        _logger.LogInformation("Creating trade-in draft for company {CompanyId}", companyId);
        var response = await _tradeInService.CreateDraftAsync(tradeIn, companyId);
        var statusCode = response.Success ? HttpStatusCode.Created : HttpStatusCode.BadRequest;
        return await CreateHttpResponse(req, response, statusCode);
    }

    [Function("GetTradeInById")]
    public async Task<HttpResponseData> GetTradeInById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "trade-ins/{id:int}")] HttpRequestData req,
        int id)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            return await CreateHttpResponse(req, ApiResponse<TradeIn>.ErrorResponse(ex.Message), HttpStatusCode.Unauthorized);
        }

        _logger.LogInformation("Getting trade-in {Id} for company {CompanyId}", id, companyId);
        var response = await _tradeInService.GetByIdAsync(id, companyId);
        if (!response.Success)
            return await CreateHttpResponse(req, response, HttpStatusCode.NotFound);
        return await CreateHttpResponse(req, response);
    }

    [Function("UpdateTradeIn")]
    [RequirePermission("trade_in.create")]
    public async Task<HttpResponseData> UpdateTradeIn(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "trade-ins/{id:int}")] HttpRequestData req,
        int id)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            return await CreateHttpResponse(req, ApiResponse<TradeIn>.ErrorResponse(ex.Message), HttpStatusCode.Unauthorized);
        }

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        UpdateTradeInRequest? updateRequest;
        try
        {
            updateRequest = JsonSerializer.Deserialize<UpdateTradeInRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return await CreateHttpResponse(req, ApiResponse<TradeIn>.ErrorResponse("Invalid request body"), HttpStatusCode.BadRequest);
        }

        if (updateRequest == null)
            return await CreateHttpResponse(req, ApiResponse<TradeIn>.ErrorResponse("Invalid request body"), HttpStatusCode.BadRequest);

        _logger.LogInformation("Updating trade-in {Id} for company {CompanyId}", id, companyId);
        var response = await _tradeInService.UpdateTradeInAsync(id, companyId, updateRequest.Notes, updateRequest.CustomerId, updateRequest.Items ?? new List<TradeInItem>());
        if (!response.Success)
        {
            var statusCode = IsNotFound(response.Message) ? HttpStatusCode.NotFound : HttpStatusCode.BadRequest;
            return await CreateHttpResponse(req, response, statusCode);
        }
        return await CreateHttpResponse(req, response);
    }

    [Function("CompleteTradeIn")]
    [RequirePermission("trade_in.complete")]
    public async Task<HttpResponseData> CompleteTradeIn(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "trade-ins/{id:int}/complete")] HttpRequestData req,
        int id)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            return await CreateHttpResponse(req, ApiResponse<TradeIn>.ErrorResponse(ex.Message), HttpStatusCode.Unauthorized);
        }

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        CompleteTradeInRequest? completeRequest;
        try
        {
            completeRequest = JsonSerializer.Deserialize<CompleteTradeInRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return await CreateHttpResponse(req, ApiResponse<TradeIn>.ErrorResponse("Invalid request body"), HttpStatusCode.BadRequest);
        }

        if (completeRequest == null || string.IsNullOrWhiteSpace(completeRequest.PaymentType))
            return await CreateHttpResponse(req, ApiResponse<TradeIn>.ErrorResponse("paymentType is required"), HttpStatusCode.BadRequest);

        var allowedPaymentTypes = new[] { "cash", "store_credit" };
        if (!allowedPaymentTypes.Contains(completeRequest.PaymentType, StringComparer.OrdinalIgnoreCase))
            return await CreateHttpResponse(req, ApiResponse<TradeIn>.ErrorResponse("paymentType must be 'cash' or 'store_credit'"), HttpStatusCode.BadRequest);

        _logger.LogInformation("Completing trade-in {Id} for company {CompanyId}", id, companyId);
        var response = await _tradeInService.CompleteAsync(id, companyId, completeRequest.PaymentType);
        if (!response.Success)
        {
            var statusCode = IsNotFound(response.Message) ? HttpStatusCode.NotFound : HttpStatusCode.BadRequest;
            return await CreateHttpResponse(req, response, statusCode);
        }
        return await CreateHttpResponse(req, response);
    }

    [Function("ParseTradeInImage")]
    [RequirePermission("trade_in.create")]
    public async Task<HttpResponseData> ParseTradeInImage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "trade-ins/parse-image")] HttpRequestData req)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            return await CreateHttpResponse(req, ApiResponse<ParseImageResult>.ErrorResponse(ex.Message), HttpStatusCode.Unauthorized);
        }

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        ParseImageRequest? parseRequest;
        try
        {
            parseRequest = JsonSerializer.Deserialize<ParseImageRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return await CreateHttpResponse(req, ApiResponse<ParseImageResult>.ErrorResponse("Invalid request body"), HttpStatusCode.BadRequest);
        }

        if (parseRequest == null || string.IsNullOrWhiteSpace(parseRequest.ImageBase64))
            return await CreateHttpResponse(req, ApiResponse<ParseImageResult>.ErrorResponse("imageBase64 is required"), HttpStatusCode.BadRequest);

        _logger.LogInformation("Parsing trade-in image for company {CompanyId}", companyId);
        var response = await _tradeInService.ParseImageAsync(parseRequest.ImageBase64, parseRequest.MimeType, companyId);
        var statusCode = response.Success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError;
        return await CreateHttpResponse(req, response, statusCode);
    }

    [Function("RejectTradeIn")]
    [RequirePermission("trade_in.complete")]
    public async Task<HttpResponseData> RejectTradeIn(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "trade-ins/{id:int}/reject")] HttpRequestData req,
        int id)
    {
        int companyId;
        try
        {
            companyId = CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            return await CreateHttpResponse(req, ApiResponse<TradeIn>.ErrorResponse(ex.Message), HttpStatusCode.Unauthorized);
        }

        _logger.LogInformation("Rejecting trade-in {Id} for company {CompanyId}", id, companyId);
        var response = await _tradeInService.RejectAsync(id, companyId);
        if (!response.Success)
        {
            var statusCode = IsNotFound(response.Message) ? HttpStatusCode.NotFound : HttpStatusCode.BadRequest;
            return await CreateHttpResponse(req, response, statusCode);
        }
        return await CreateHttpResponse(req, response);
    }

    private static bool IsNotFound(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        return message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpResponseData> CreateHttpResponse<T>(
        HttpRequestData req,
        ApiResponse<T> apiResponse,
        HttpStatusCode? statusCode = null)
    {
        var resolvedStatus = statusCode ?? (apiResponse.Success ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        var response = req.CreateResponse(resolvedStatus);

        if (resolvedStatus == HttpStatusCode.NoContent)
            return response;

        response.Headers.Add("Content-Type", "application/json; charset=utf-8");

        var json = JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await response.WriteStringAsync(json);
        return response;
    }
}

internal class UpdateTradeInRequest
{
    public string? Notes { get; set; }
    public int? CustomerId { get; set; }
    public List<TradeInItem>? Items { get; set; }
}

internal class CompleteTradeInRequest
{
    public string? PaymentType { get; set; }
}
