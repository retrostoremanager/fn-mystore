using MyStore.Models;

namespace MyStore.Repositories;

public interface IInventoryRepository
{
    Task<List<InventoryItem>> GetAllAsync(int companyId);
    Task<InventoryItem?> GetByIdAsync(int id, int companyId);
    Task<InventoryItem> CreateAsync(InventoryItem item);
    Task<InventoryItem?> UpdateAsync(int id, InventoryItem item, int companyId);
    Task<bool> DeleteAsync(int id, int companyId);
    Task<List<InventoryItem>> SearchAsync(string searchTerm, int companyId);
    Task<bool> UpdateQuantityAsync(int id, int quantityChange, int companyId);
}

