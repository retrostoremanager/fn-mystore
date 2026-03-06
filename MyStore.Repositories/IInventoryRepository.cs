using MyStore.Models;

namespace MyStore.Repositories;

public interface IInventoryRepository
{
    Task<List<InventoryItem>> GetAllAsync(int companyId, int? locationId = null);
    Task<InventoryItem?> GetByIdAsync(int id, int companyId);
    Task<InventoryItem> CreateAsync(InventoryItem item);
    Task<InventoryItem?> UpdateAsync(int id, InventoryItem item, int companyId);
    Task<bool> DeleteAsync(int id, int companyId);
    Task<List<InventoryItem>> SearchAsync(string searchTerm, int companyId, int? locationId = null);
    Task<List<ItemLocationInfo>> GetLocationsForItemAsync(int id, int companyId);
    Task<bool> UpdateQuantityAsync(int id, int quantityChange, int companyId);
    Task<int> GetCountByLocationIdAsync(int locationId, int companyId);
    Task<int> DeleteByLocationIdAsync(int locationId, int companyId);
    Task<int> ReassignToLocationAsync(int fromLocationId, int toLocationId, int companyId);
}

