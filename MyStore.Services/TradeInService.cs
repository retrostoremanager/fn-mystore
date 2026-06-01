using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class TradeInService : ITradeInService
{
    private readonly ITradeInRepository _tradeInRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly ILoyaltyService? _loyaltyService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TradeInService> _logger;
    private readonly string? _openAiApiKey;

    private const string OpenAiUrl = "https://api.openai.com/v1/chat/completions";

    public TradeInService(
        ITradeInRepository tradeInRepository,
        IInventoryRepository inventoryRepository,
        HttpClient httpClient,
        ILogger<TradeInService> logger,
        ILoyaltyService? loyaltyService = null,
        IConfiguration? configuration = null)
    {
        _tradeInRepository = tradeInRepository ?? throw new ArgumentNullException(nameof(tradeInRepository));
        _inventoryRepository = inventoryRepository ?? throw new ArgumentNullException(nameof(inventoryRepository));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loyaltyService = loyaltyService;
        _openAiApiKey = configuration?["OpenAI:ApiKey"]
            ?? configuration?["OPENAI_API_KEY"]
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    }

    public async Task<ApiResponse<List<TradeIn>>> GetAllAsync(int companyId, string? status = null, DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        try
        {
            var items = await _tradeInRepository.GetAllAsync(companyId, status, dateFrom, dateTo);
            return ApiResponse<List<TradeIn>>.SuccessResponse(items);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<TradeIn>>.ErrorResponse(
                "Failed to retrieve trade-ins",
                new List<string> { ex.Message });
        }
    }

    public async Task<ApiResponse<TradeIn>> GetByIdAsync(int id, int companyId)
    {
        try
        {
            var tradeIn = await _tradeInRepository.GetByIdAsync(id, companyId);
            if (tradeIn is null)
                return ApiResponse<TradeIn>.ErrorResponse($"Trade-in with ID {id} not found");
            return ApiResponse<TradeIn>.SuccessResponse(tradeIn);
        }
        catch (Exception ex)
        {
            return ApiResponse<TradeIn>.ErrorResponse(
                "Failed to retrieve trade-in",
                new List<string> { ex.Message });
        }
    }

    public async Task<ApiResponse<TradeIn>> CreateDraftAsync(TradeIn tradeIn, int companyId)
    {
        try
        {
            tradeIn.CompanyId = companyId;
            tradeIn.Status = "draft";
            tradeIn.TotalOfferedValue = 0;
            tradeIn.CreatedAt = DateTime.UtcNow;

            var created = await _tradeInRepository.CreateAsync(tradeIn);
            return ApiResponse<TradeIn>.SuccessResponse(created, "Trade-in draft created successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<TradeIn>.ErrorResponse(
                "Failed to create trade-in draft",
                new List<string> { ex.Message });
        }
    }

    public async Task<ApiResponse<TradeInItem>> AddItemAsync(TradeInItem item, int tradeInId, int companyId)
    {
        try
        {
            var tradeIn = await _tradeInRepository.GetByIdAsync(tradeInId, companyId);
            if (tradeIn is null)
                return ApiResponse<TradeInItem>.ErrorResponse($"Trade-in with ID {tradeInId} not found");

            if (tradeIn.Status != "draft")
                return ApiResponse<TradeInItem>.ErrorResponse(
                    $"Cannot add items to a trade-in with status '{tradeIn.Status}'. Only draft trade-ins can be modified.");

            item.TradeInId = tradeInId;
            item.CreatedAt = DateTime.UtcNow;

            var created = await _tradeInRepository.AddItemAsync(item);
            return ApiResponse<TradeInItem>.SuccessResponse(created, "Item added to trade-in successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<TradeInItem>.ErrorResponse(
                "Failed to add item to trade-in",
                new List<string> { ex.Message });
        }
    }

    public async Task<ApiResponse<TradeInItem>> UpdateItemAsync(TradeInItem item, int companyId)
    {
        try
        {
            var tradeIn = await _tradeInRepository.GetByIdAsync(item.TradeInId, companyId);
            if (tradeIn is null)
                return ApiResponse<TradeInItem>.ErrorResponse($"Trade-in with ID {item.TradeInId} not found");

            if (tradeIn.Status != "draft")
                return ApiResponse<TradeInItem>.ErrorResponse(
                    $"Cannot update items on a trade-in with status '{tradeIn.Status}'. Only draft trade-ins can be modified.");

            var updated = await _tradeInRepository.UpdateItemAsync(item);
            if (updated is null)
                return ApiResponse<TradeInItem>.ErrorResponse($"Trade-in item with ID {item.Id} not found");

            return ApiResponse<TradeInItem>.SuccessResponse(updated, "Trade-in item updated successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<TradeInItem>.ErrorResponse(
                "Failed to update trade-in item",
                new List<string> { ex.Message });
        }
    }

    public async Task<ApiResponse<TradeIn>> CompleteAsync(int id, int companyId, string paymentType)
    {
        try
        {
            var tradeIn = await _tradeInRepository.GetByIdAsync(id, companyId);
            if (tradeIn is null)
                return ApiResponse<TradeIn>.ErrorResponse($"Trade-in with ID {id} not found");

            if (tradeIn.Status != "draft")
                return ApiResponse<TradeIn>.ErrorResponse(
                    $"Cannot complete a trade-in with status '{tradeIn.Status}'. Only draft trade-ins can be completed.");

            var completed = await _tradeInRepository.CompleteAsync(id, paymentType, DateTime.UtcNow);
            if (completed is null)
                return ApiResponse<TradeIn>.ErrorResponse($"Failed to complete trade-in with ID {id}");

            foreach (var item in tradeIn.Items.Where(i => i.AcceptedValue > 0))
            {
                var inventoryItem = new InventoryItem
                {
                    CompanyId = companyId,
                    Name = $"{item.GameTitle} ({item.Platform})",
                    Category = "Game",
                    Quantity = 1,
                    Condition = item.Condition,
                    BuyPrice = item.AcceptedValue,
                    SellPrice = 0,
                    AddedDate = DateTime.UtcNow,
                };

                var created = await _inventoryRepository.CreateAsync(inventoryItem);

                item.InventoryItemId = created.Id;
                await _tradeInRepository.UpdateItemAsync(item);
            }

            if (paymentType == "store_credit" && tradeIn.CustomerId.HasValue && _loyaltyService is not null)
            {
                var totalAccepted = tradeIn.Items
                    .Where(i => i.AcceptedValue > 0)
                    .Sum(i => i.AcceptedValue ?? 0);

                await _loyaltyService.EarnFromTradeInAsync(tradeIn.CustomerId.Value, companyId, totalAccepted);
            }

            var result = await _tradeInRepository.GetByIdAsync(id, companyId);
            return ApiResponse<TradeIn>.SuccessResponse(result!, "Trade-in completed successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<TradeIn>.ErrorResponse(
                "Failed to complete trade-in",
                new List<string> { ex.Message });
        }
    }

    public async Task<ApiResponse<TradeIn>> UpdateTradeInAsync(int id, int companyId, string? notes, int? customerId, List<TradeInItem> items)
    {
        try
        {
            var tradeIn = await _tradeInRepository.GetByIdAsync(id, companyId);
            if (tradeIn is null)
                return ApiResponse<TradeIn>.ErrorResponse($"Trade-in with ID {id} not found");

            if (tradeIn.Status != "draft")
                return ApiResponse<TradeIn>.ErrorResponse(
                    $"Cannot update a trade-in with status '{tradeIn.Status}'. Only draft trade-ins can be modified.");

            tradeIn.Notes = notes;
            tradeIn.CustomerId = customerId;

            await _tradeInRepository.UpdateAsync(tradeIn);

            var existingIds = tradeIn.Items.Select(i => i.Id).ToHashSet();
            foreach (var item in items)
            {
                item.TradeInId = id;
                if (item.Id > 0 && existingIds.Contains(item.Id))
                {
                    await _tradeInRepository.UpdateItemAsync(item);
                }
                else
                {
                    item.CreatedAt = DateTime.UtcNow;
                    await _tradeInRepository.AddItemAsync(item);
                }
            }

            var updated = await _tradeInRepository.GetByIdAsync(id, companyId);
            return ApiResponse<TradeIn>.SuccessResponse(updated!, "Trade-in updated successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<TradeIn>.ErrorResponse(
                "Failed to update trade-in",
                new List<string> { ex.Message });
        }
    }

    public async Task<ApiResponse<TradeIn>> RejectAsync(int id, int companyId)
    {
        try
        {
            var tradeIn = await _tradeInRepository.GetByIdAsync(id, companyId);
            if (tradeIn is null)
                return ApiResponse<TradeIn>.ErrorResponse($"Trade-in with ID {id} not found");

            if (tradeIn.Status != "draft")
                return ApiResponse<TradeIn>.ErrorResponse(
                    $"Cannot reject a trade-in with status '{tradeIn.Status}'. Only draft trade-ins can be rejected.");

            tradeIn.Status = "rejected";
            var updated = await _tradeInRepository.UpdateAsync(tradeIn);
            if (updated is null)
                return ApiResponse<TradeIn>.ErrorResponse($"Failed to reject trade-in with ID {id}");

            return ApiResponse<TradeIn>.SuccessResponse(updated, "Trade-in rejected successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<TradeIn>.ErrorResponse(
                "Failed to reject trade-in",
                new List<string> { ex.Message });
        }
    }

    public async Task<ApiResponse<ParseImageResult>> ParseImageAsync(string imageBase64, string mimeType, int companyId)
    {
        if (string.IsNullOrWhiteSpace(_openAiApiKey))
        {
            _logger.LogWarning("OpenAI API key not configured for company {CompanyId}. Returning empty parse result.", companyId);
            return ApiResponse<ParseImageResult>.SuccessResponse(new ParseImageResult());
        }

        try
        {
            var prompt = "You are a retro game store assistant. Analyze this image of video games and identify each game. " +
                         "For each game found, return a JSON array with objects containing: gameTitle (string), platform (string), " +
                         "condition (string: poor/fair/good/excellent), offeredValue (null). " +
                         "Return ONLY the JSON array, no other text. Example: [{\"gameTitle\":\"Super Mario Bros\",\"platform\":\"NES\",\"condition\":\"good\",\"offeredValue\":null}]";

            var requestBody = new
            {
                model = "gpt-4o",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new
                            {
                                type = "image_url",
                                image_url = new { url = $"data:{mimeType};base64,{imageBase64}" }
                            }
                        }
                    }
                },
                max_tokens = 1000
            };

            var json = JsonSerializer.Serialize(requestBody);

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, OpenAiUrl);
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _openAiApiKey);
            requestMessage.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(requestMessage);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenAI API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
                return ApiResponse<ParseImageResult>.ErrorResponse("Image parsing service unavailable");
            }

            var jsonNode = JsonNode.Parse(responseBody);
            var messageContent = jsonNode?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(messageContent))
                return ApiResponse<ParseImageResult>.SuccessResponse(new ParseImageResult());

            var cleanedContent = messageContent.Trim();
            if (cleanedContent.StartsWith("```"))
            {
                cleanedContent = cleanedContent.Split('\n', 2).Last();
                cleanedContent = cleanedContent.TrimEnd('`').Trim();
            }

            var items = JsonSerializer.Deserialize<List<ParsedTradeInItem>>(cleanedContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<ParsedTradeInItem>();

            return ApiResponse<ParseImageResult>.SuccessResponse(new ParseImageResult { Items = items });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse image via OpenAI");
            return ApiResponse<ParseImageResult>.ErrorResponse(
                "Failed to parse image",
                new List<string> { ex.Message });
        }
    }
}
