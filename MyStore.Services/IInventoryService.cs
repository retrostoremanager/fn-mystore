using MyStore.Models;

namespace MyStore.Services;

public interface IInventoryService
{
    Task<ApiResponse<List<InventoryItem>>> GetAllInventoryAsync();
    Task<ApiResponse<InventoryItem>> GetInventoryByIdAsync(int id);
    Task<ApiResponse<InventoryItem>> CreateInventoryItemAsync(CreateInventoryItemRequest request);
    Task<ApiResponse<InventoryItem>> UpdateInventoryItemAsync(int id, UpdateInventoryItemRequest request);
    Task<ApiResponse<bool>> DeleteInventoryItemAsync(int id);
    Task<ApiResponse<List<InventoryItem>>> SearchInventoryAsync(string searchTerm);
    Task<ApiResponse<bool>> UpdateInventoryQuantityAsync(int id, int quantityChange);
}

