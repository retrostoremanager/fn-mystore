using Microsoft.Extensions.Logging;
using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class TradeInService : ITradeInService
{
    private readonly ITradeInRepository _tradeInRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IGameRepository _gameRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly ILoyaltyService? _loyaltyService;
    private readonly ILogger<TradeInService>? _logger;

    public TradeInService(
        ITradeInRepository tradeInRepository,
        IInventoryRepository inventoryRepository,
        IGameRepository gameRepository,
        ILocationRepository locationRepository,
        ILoyaltyService? loyaltyService = null,
        ILogger<TradeInService>? logger = null)
    {
        _tradeInRepository = tradeInRepository;
        _inventoryRepository = inventoryRepository;
        _gameRepository = gameRepository;
        _locationRepository = locationRepository;
        _loyaltyService = loyaltyService;
        _logger = logger;
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

            var acceptedItems = tradeIn.Items.Where(i => i.AcceptedValue > 0).ToList();
            int locationId = 0;
            if (acceptedItems.Count > 0)
            {
                var locations = await _locationRepository.GetByCompanyIdAsync(companyId);
                var resolved = locations
                    .OrderByDescending(l => l.IsPrimary)
                    .ThenBy(l => l.Id)
                    .FirstOrDefault();
                if (resolved is null)
                    return ApiResponse<TradeIn>.ErrorResponse(
                        "Cannot complete trade-in: no location configured for this company. Create a location before completing trade-ins.");
                locationId = resolved.Id;
            }

            foreach (var item in acceptedItems)
            {
                var game = await EnsureGameForTradeInItemAsync(item);

                var existing = await _inventoryRepository.FindByCompanyGameConditionAsync(
                    companyId, game.Id, item.Condition ?? string.Empty);

                int inventoryId;
                if (existing is not null)
                {
                    await _inventoryRepository.UpdateQuantityAsync(existing.Id, 1, companyId);
                    inventoryId = existing.Id;
                }
                else
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
                        Game = game,
                        LocationId = locationId,
                    };

                    var created = await _inventoryRepository.CreateAsync(inventoryItem);
                    inventoryId = created.Id;
                }

                item.InventoryItemId = inventoryId;
                await _tradeInRepository.UpdateItemAsync(item);
            }

            var completed = await _tradeInRepository.CompleteAsync(id, companyId, paymentType, DateTime.UtcNow);
            if (completed is null)
                return ApiResponse<TradeIn>.ErrorResponse($"Failed to complete trade-in with ID {id}");

            if (paymentType == "store_credit" && tradeIn.CustomerId.HasValue && _loyaltyService is not null)
            {
                var totalAccepted = tradeIn.Items
                    .Where(i => i.AcceptedValue > 0)
                    .Sum(i => i.AcceptedValue ?? 0);

                try
                {
                    await _loyaltyService.EarnFromTradeInAsync(tradeIn.CustomerId.Value, companyId, totalAccepted, id);
                }
                catch (Exception loyaltyEx)
                {
                    _logger?.LogError(
                        loyaltyEx,
                        "Failed to award loyalty points for trade-in {TradeInId} (customer {CustomerId}, company {CompanyId}); trade-in completion will not be rolled back",
                        id,
                        tradeIn.CustomerId.Value,
                        companyId);
                }
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

    private async Task<Game> EnsureGameForTradeInItemAsync(TradeInItem item)
    {
        var title = (item.GameTitle ?? string.Empty).Trim();
        var platform = (item.Platform ?? string.Empty).Trim();
        var key = $"{title.ToLowerInvariant()}|{platform.ToLowerInvariant()}";
        var gameId = $"tradein:{ComputeShortHash(key)}";

        var game = new Game
        {
            Id = gameId,
            Title = string.IsNullOrWhiteSpace(title) ? "Unknown" : title,
            Console = string.IsNullOrWhiteSpace(platform) ? "Unknown" : platform
        };

        await _gameRepository.UpsertAsync(game);
        return game;
    }

    private static string ComputeShortHash(string input)
    {
        using var sha = System.Security.Cryptography.SHA1.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant().Substring(0, 32);
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
}
