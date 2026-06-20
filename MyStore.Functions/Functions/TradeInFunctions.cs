using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger _logger;

    public TradeInFunctions(
        ITradeInService tradeInService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        _tradeInService = tradeInService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
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
            var statusCode = ResolveErrorStatus(response.Message, HttpStatusCode.BadRequest);
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
            var statusCode = ResolveErrorStatus(response.Message, HttpStatusCode.BadRequest);
            return await CreateHttpResponse(req, response, statusCode);
        }
        return await CreateHttpResponse(req, response);
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
            var statusCode = ResolveErrorStatus(response.Message, HttpStatusCode.BadRequest);
            return await CreateHttpResponse(req, response, statusCode);
        }
        return await CreateHttpResponse(req, response);
    }

    [Function("ParseTradeInImage")]
    [RequirePermission("trade_in.create")]
    public async Task<HttpResponseData> ParseTradeInImage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "trade-ins/parse-image")] HttpRequestData req)
    {
        try
        {
            CompanyHelper.GetCompanyIdRequired(req);
        }
        catch (UnauthorizedAccessException ex)
        {
            return await CreateHttpResponse(req, ApiResponse<ParseTradeInImageResponse>.ErrorResponse(ex.Message), HttpStatusCode.Unauthorized);
        }

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        ParseTradeInImageRequest? parseRequest;
        try
        {
            parseRequest = JsonSerializer.Deserialize<ParseTradeInImageRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return await CreateHttpResponse(req, ApiResponse<ParseTradeInImageResponse>.ErrorResponse("Invalid request body"), HttpStatusCode.BadRequest);
        }

        if (parseRequest == null
            || string.IsNullOrWhiteSpace(parseRequest.ImageBase64)
            || string.IsNullOrWhiteSpace(parseRequest.MimeType))
        {
            return await CreateHttpResponse(req, ApiResponse<ParseTradeInImageResponse>.ErrorResponse("imageBase64 and mimeType are required"), HttpStatusCode.BadRequest);
        }

        var apiKey = Environment.GetEnvironmentVariable("Anthropic__ApiKey")
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? _configuration["Anthropic__ApiKey"]
            ?? _configuration["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogError("Anthropic API key is not configured (set Anthropic__ApiKey or ANTHROPIC_API_KEY on the Function App)");
            return await CreateHttpResponse(req, ApiResponse<ParseTradeInImageResponse>.ErrorResponse("Image parsing service is not configured"), HttpStatusCode.BadGateway);
        }

        const string prompt = "Identify all video games visible in this image. " +
            "Return ONLY a JSON array (no prose, no markdown, no code fences) of objects, " +
            "each with exactly these fields: \"gameTitle\" (string), \"platform\" (string, e.g. " +
            "\"PlayStation 2\", \"Nintendo Switch\", \"Xbox 360\"), and \"estimatedCondition\" " +
            "(string, one of: \"Mint\", \"Good\", \"Fair\", \"Poor\"). " +
            "If you cannot identify any games, return an empty array []. " +
            "Do not include any text outside the JSON array.";

        var anthropicPayload = new
        {
            model = "claude-haiku-4-5",
            max_tokens = 1024,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "image",
                            source = new
                            {
                                type = "base64",
                                media_type = parseRequest.MimeType,
                                data = parseRequest.ImageBase64
                            }
                        },
                        new
                        {
                            type = "text",
                            text = prompt
                        }
                    }
                }
            }
        };

        string anthropicJson;
        try
        {
            anthropicJson = await CallAnthropicAsync(apiKey, anthropicPayload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Anthropic API call failed");
            return await CreateHttpResponse(req, ApiResponse<ParseTradeInImageResponse>.ErrorResponse("Failed to parse image"), HttpStatusCode.BadGateway);
        }

        List<ParsedTradeInItem>? items;
        try
        {
            items = ExtractItemsFromAnthropicResponse(anthropicJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Anthropic response");
            return await CreateHttpResponse(req, ApiResponse<ParseTradeInImageResponse>.ErrorResponse("Failed to parse image"), HttpStatusCode.BadGateway);
        }

        if (items == null)
        {
            return await CreateHttpResponse(req, ApiResponse<ParseTradeInImageResponse>.ErrorResponse("Failed to parse image"), HttpStatusCode.BadGateway);
        }

        var result = new ParseTradeInImageResponse { Items = items };
        return await CreateHttpResponse(req, ApiResponse<ParseTradeInImageResponse>.SuccessResponse(result));
    }

    private async Task<string> CallAnthropicAsync(string apiKey, object payload)
    {
        var client = _httpClientFactory.CreateClient();
        var json = JsonSerializer.Serialize(payload);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await client.SendAsync(httpRequest);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Anthropic API returned status {(int)response.StatusCode}");
        }
        return body;
    }

    private static List<ParsedTradeInItem>? ExtractItemsFromAnthropicResponse(string anthropicJson)
    {
        using var doc = JsonDocument.Parse(anthropicJson);
        if (!doc.RootElement.TryGetProperty("content", out var contentArray)
            || contentArray.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? text = null;
        foreach (var block in contentArray.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var typeEl)
                && typeEl.GetString() == "text"
                && block.TryGetProperty("text", out var textEl))
            {
                text = textEl.GetString();
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0) trimmed = trimmed.Substring(firstNewline + 1);
            if (trimmed.EndsWith("```"))
                trimmed = trimmed.Substring(0, trimmed.Length - 3);
            trimmed = trimmed.Trim();
        }

        var startIdx = trimmed.IndexOf('[');
        var endIdx = trimmed.LastIndexOf(']');
        if (startIdx < 0 || endIdx <= startIdx)
            return null;
        var jsonArray = trimmed.Substring(startIdx, endIdx - startIdx + 1);

        var items = JsonSerializer.Deserialize<List<ParsedTradeInItem>>(jsonArray, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        return items ?? new List<ParsedTradeInItem>();
    }

    private static bool IsNotFound(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        return message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStatusConflict(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        return message.Contains("Cannot complete", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Cannot reject", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Cannot update", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Cannot add items", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Only draft trade-ins", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpStatusCode ResolveErrorStatus(string? message, HttpStatusCode fallback)
    {
        if (IsNotFound(message)) return HttpStatusCode.NotFound;
        if (IsStatusConflict(message)) return HttpStatusCode.Conflict;
        return fallback;
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

internal class ParseTradeInImageRequest
{
    public string? ImageBase64 { get; set; }
    public string? MimeType { get; set; }
}

public class ParseTradeInImageResponse
{
    public List<ParsedTradeInItem> Items { get; set; } = new();
}

public class ParsedTradeInItem
{
    public string? GameTitle { get; set; }
    public string? Platform { get; set; }
    public string? EstimatedCondition { get; set; }
}
