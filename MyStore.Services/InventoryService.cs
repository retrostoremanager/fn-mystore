using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class InventoryService : IInventoryService
{
    private readonly IInventoryRepository _repository;

    public InventoryService(IInventoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApiResponse<List<InventoryItem>>> GetAllInventoryAsync(int companyId)
    {
        try
        {
            var items = await _repository.GetAllAsync(companyId);
            return ApiResponse<List<InventoryItem>>.SuccessResponse(items);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<InventoryItem>>.ErrorResponse(
                "Failed to retrieve inventory items",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<InventoryItem>> GetInventoryByIdAsync(int id, int companyId)
    {
        try
        {
            var item = await _repository.GetByIdAsync(id, companyId);
            if (item == null)
            {
                return ApiResponse<InventoryItem>.ErrorResponse($"Inventory item with ID {id} not found");
            }

            return ApiResponse<InventoryItem>.SuccessResponse(item);
        }
        catch (Exception ex)
        {
            return ApiResponse<InventoryItem>.ErrorResponse(
                "Failed to retrieve inventory item",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<InventoryItem>> CreateInventoryItemAsync(CreateInventoryItemRequest request, int companyId)
    {
        try
        {
            // Validation
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return ApiResponse<InventoryItem>.ErrorResponse("Name is required");
            }

            if (request.Quantity < 0)
            {
                return ApiResponse<InventoryItem>.ErrorResponse("Quantity cannot be negative");
            }

            if (request.SellPrice < 0)
            {
                return ApiResponse<InventoryItem>.ErrorResponse("Sell price cannot be negative");
            }

            if (request.LocationId <= 0)
            {
                return ApiResponse<InventoryItem>.ErrorResponse("Location is required");
            }

            var item = new InventoryItem
            {
                CompanyId = companyId,
                LocationId = request.LocationId,
                Name = request.Name,
                Category = request.Category,
                Quantity = request.Quantity,
                SellPrice = request.SellPrice,
                BuyPrice = request.BuyPrice,
                Condition = request.Condition,
                Completeness = request.Completeness,
                Notes = request.Notes
            };

            // TODO: If GameId is provided, fetch game details from game API
            // For now, we'll leave Game as null

            var created = await _repository.CreateAsync(item);
            return ApiResponse<InventoryItem>.SuccessResponse(created, "Inventory item created successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<InventoryItem>.ErrorResponse(
                "Failed to create inventory item",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<InventoryItem>> UpdateInventoryItemAsync(int id, UpdateInventoryItemRequest request, int companyId)
    {
        try
        {
            var existing = await _repository.GetByIdAsync(id, companyId);
            if (existing == null)
            {
                return ApiResponse<InventoryItem>.ErrorResponse($"Inventory item with ID {id} not found");
            }

            // Update only provided fields
            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                existing.Name = request.Name;
            }

            if (!string.IsNullOrWhiteSpace(request.Category))
            {
                existing.Category = request.Category;
            }

            if (request.Quantity.HasValue)
            {
                if (request.Quantity.Value < 0)
                {
                    return ApiResponse<InventoryItem>.ErrorResponse("Quantity cannot be negative");
                }
                existing.Quantity = request.Quantity.Value;
            }

            if (request.SellPrice.HasValue)
            {
                if (request.SellPrice.Value < 0)
                {
                    return ApiResponse<InventoryItem>.ErrorResponse("Sell price cannot be negative");
                }
                existing.SellPrice = request.SellPrice.Value;
            }

            if (request.BuyPrice.HasValue)
            {
                existing.BuyPrice = request.BuyPrice.Value;
            }

            if (!string.IsNullOrWhiteSpace(request.Condition))
            {
                existing.Condition = request.Condition;
            }

            if (request.Completeness != null)
            {
                existing.Completeness = request.Completeness;
            }

            if (request.Notes != null)
            {
                existing.Notes = request.Notes;
            }

            if (request.LocationId.HasValue && request.LocationId.Value > 0)
            {
                existing.LocationId = request.LocationId.Value;
            }

            var updated = await _repository.UpdateAsync(id, existing, companyId);
            if (updated == null)
            {
                return ApiResponse<InventoryItem>.ErrorResponse($"Failed to update inventory item with ID {id}");
            }

            return ApiResponse<InventoryItem>.SuccessResponse(updated, "Inventory item updated successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<InventoryItem>.ErrorResponse(
                "Failed to update inventory item",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<bool>> DeleteInventoryItemAsync(int id, int companyId)
    {
        try
        {
            var result = await _repository.DeleteAsync(id, companyId);
            if (!result)
            {
                return ApiResponse<bool>.ErrorResponse($"Inventory item with ID {id} not found");
            }

            return ApiResponse<bool>.SuccessResponse(true, "Inventory item deleted successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<bool>.ErrorResponse(
                "Failed to delete inventory item",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<List<InventoryItem>>> SearchInventoryAsync(string searchTerm, int companyId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return await GetAllInventoryAsync(companyId);
            }

            var results = await _repository.SearchAsync(searchTerm, companyId);
            return ApiResponse<List<InventoryItem>>.SuccessResponse(results);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<InventoryItem>>.ErrorResponse(
                "Failed to search inventory",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<bool>> UpdateInventoryQuantityAsync(int id, int quantityChange, int companyId)
    {
        try
        {
            var result = await _repository.UpdateQuantityAsync(id, quantityChange, companyId);
            if (!result)
            {
                return ApiResponse<bool>.ErrorResponse($"Inventory item with ID {id} not found");
            }

            return ApiResponse<bool>.SuccessResponse(true, "Inventory quantity updated successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<bool>.ErrorResponse(
                "Failed to update inventory quantity",
                new List<string> { ex.Message }
            );
        }
    }
}

