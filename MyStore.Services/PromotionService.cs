using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class PromotionService : IPromotionService
{
    private readonly IPromotionRepository _repository;

    public PromotionService(IPromotionRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApiResponse<List<Promotion>>> GetAllAsync(int companyId)
    {
        try
        {
            var promotions = await _repository.GetAllAsync(companyId);
            return ApiResponse<List<Promotion>>.SuccessResponse(promotions);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<Promotion>>.ErrorResponse(
                "Failed to retrieve promotions",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<Promotion>> GetByIdAsync(int id, int companyId)
    {
        try
        {
            var promotion = await _repository.GetByIdAsync(id, companyId);
            if (promotion == null)
            {
                return ApiResponse<Promotion>.ErrorResponse($"Promotion with ID {id} not found");
            }

            return ApiResponse<Promotion>.SuccessResponse(promotion);
        }
        catch (Exception ex)
        {
            return ApiResponse<Promotion>.ErrorResponse(
                "Failed to retrieve promotion",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<Promotion>> CreateAsync(CreatePromotionRequest request, int companyId)
    {
        try
        {
            var promotion = new Promotion
            {
                CompanyId = companyId,
                Name = request.Name,
                Type = request.Type,
                DiscountPercent = request.DiscountPercent,
                BuyQuantity = request.BuyQuantity,
                GetQuantity = request.GetQuantity,
                Scope = request.Scope,
                ScopeValue = request.ScopeValue,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                IsActive = request.IsActive,
                CreatedBy = request.CreatedBy,
            };

            var created = await _repository.CreateAsync(promotion);
            return ApiResponse<Promotion>.SuccessResponse(created, "Promotion created successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<Promotion>.ErrorResponse(
                "Failed to create promotion",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<Promotion>> UpdateAsync(int id, UpdatePromotionRequest request, int companyId)
    {
        try
        {
            var existing = await _repository.GetByIdAsync(id, companyId);
            if (existing == null)
            {
                return ApiResponse<Promotion>.ErrorResponse($"Promotion with ID {id} not found");
            }

            if (request.Name != null) existing.Name = request.Name;
            if (request.Type != null) existing.Type = request.Type;
            if (request.DiscountPercent.HasValue) existing.DiscountPercent = request.DiscountPercent;
            if (request.BuyQuantity.HasValue) existing.BuyQuantity = request.BuyQuantity;
            if (request.GetQuantity.HasValue) existing.GetQuantity = request.GetQuantity;
            if (request.Scope != null) existing.Scope = request.Scope;
            if (request.ScopeValue != null) existing.ScopeValue = request.ScopeValue;
            if (request.StartDate.HasValue) existing.StartDate = request.StartDate.Value;
            if (request.EndDate.HasValue) existing.EndDate = request.EndDate;
            if (request.IsActive.HasValue) existing.IsActive = request.IsActive.Value;

            var updated = await _repository.UpdateAsync(existing);
            if (updated == null)
            {
                return ApiResponse<Promotion>.ErrorResponse($"Failed to update promotion with ID {id}");
            }

            return ApiResponse<Promotion>.SuccessResponse(updated, "Promotion updated successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<Promotion>.ErrorResponse(
                "Failed to update promotion",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<bool>> DeleteAsync(int id, int companyId)
    {
        try
        {
            var result = await _repository.DeleteAsync(id, companyId);
            if (!result)
            {
                return ApiResponse<bool>.ErrorResponse($"Promotion with ID {id} not found");
            }

            return ApiResponse<bool>.SuccessResponse(true, "Promotion deleted successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<bool>.ErrorResponse(
                "Failed to delete promotion",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<List<Promotion>>> GetActivePromotionsAsync(int companyId)
    {
        try
        {
            var promotions = await _repository.GetActiveAsync(companyId, DateTime.UtcNow);
            return ApiResponse<List<Promotion>>.SuccessResponse(promotions);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<Promotion>>.ErrorResponse(
                "Failed to retrieve active promotions",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<IEnumerable<LineDiscount>> ApplyPromotionsAsync(IEnumerable<CartItem> cartItems, int companyId)
    {
        var activePromotions = await _repository.GetActiveAsync(companyId, DateTime.UtcNow);
        var items = cartItems.ToList();
        var discounts = new Dictionary<int, decimal>();

        foreach (var item in items)
        {
            discounts[item.InventoryItemId] = 0m;
        }

        foreach (var promotion in activePromotions)
        {
            var matchingItems = GetMatchingItems(items, promotion);

            if (promotion.Type == "percentage" && promotion.DiscountPercent.HasValue)
            {
                foreach (var item in matchingItems)
                {
                    var lineTotal = item.Quantity * item.UnitPrice;
                    var discount = Math.Round(lineTotal * promotion.DiscountPercent.Value / 100m, 2, MidpointRounding.AwayFromZero);
                    discounts[item.InventoryItemId] += discount;
                }
            }
            else if (promotion.Type == "bxgy" && promotion.BuyQuantity.HasValue && promotion.GetQuantity.HasValue)
            {
                foreach (var item in matchingItems)
                {
                    var freeUnits = (item.Quantity / promotion.BuyQuantity.Value) * promotion.GetQuantity.Value;
                    var discount = freeUnits * item.UnitPrice;
                    discounts[item.InventoryItemId] += discount;
                }
            }
        }

        return discounts
            .Where(kv => kv.Value > 0)
            .Select(kv => new LineDiscount { ItemId = kv.Key, DiscountAmount = kv.Value });
    }

    private static List<CartItem> GetMatchingItems(List<CartItem> items, Promotion promotion)
    {
        return promotion.Scope switch
        {
            "store_wide" => items,
            "category" => items
                .Where(i => string.Equals(i.Category, promotion.ScopeValue, StringComparison.OrdinalIgnoreCase))
                .ToList(),
            "item" => items
                .Where(i => i.InventoryItemId.ToString() == promotion.ScopeValue)
                .ToList(),
            _ => new List<CartItem>()
        };
    }
}
