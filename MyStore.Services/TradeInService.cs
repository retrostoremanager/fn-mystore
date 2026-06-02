using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class TradeInService : ITradeInService
{
    private readonly ITradeInRepository _tradeInRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly ILoyaltyService? _loyaltyService;

    public TradeInService(
        ITradeInRepository tradeInRepository,
        IInventoryRepository inventoryRepository,
        ILoyaltyService? loyaltyService = null)
    {
        _tradeInRepository = tradeInRepository;
        _inventoryRepository = inventoryRepository;
        _loyaltyService = loyaltyService;
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
                var settingsResponse = await _loyaltyService.GetSettingsAsync(companyId);
                if (settingsResponse.Success && settingsResponse.Data is not null && settingsResponse.Data.IsEnabled)
                {
                    var totalAccepted = tradeIn.Items
                        .Where(i => i.AcceptedValue > 0)
                        .Sum(i => i.AcceptedValue ?? 0);

                    await _loyaltyService.EarnFromTradeInAsync(tradeIn.CustomerId.Value, companyId, totalAccepted);
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
