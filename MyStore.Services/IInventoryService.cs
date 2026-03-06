using MyStore.Models;

namespace MyStore.Services;

public interface IInventoryService
{
    Task<ApiResponse<List<InventoryItem>>> GetAllInventoryAsync(int companyId, int? locationId = null);
    Task<ApiResponse<InventoryItem>> GetInventoryByIdAsync(int id, int companyId);
    Task<ApiResponse<List<ItemLocationInfo>>> GetLocationsForItemAsync(int id, int companyId);
    Task<ApiResponse<InventoryItem>> CreateInventoryItemAsync(CreateInventoryItemRequest request, int companyId);
    Task<ApiResponse<InventoryItem>> UpdateInventoryItemAsync(int id, UpdateInventoryItemRequest request, int companyId);
    Task<ApiResponse<bool>> DeleteInventoryItemAsync(int id, int companyId);
    Task<ApiResponse<List<InventoryItem>>> SearchInventoryAsync(string searchTerm, int companyId, int? locationId = null);
    Task<ApiResponse<bool>> UpdateInventoryQuantityAsync(int id, int quantityChange, int companyId);
}

