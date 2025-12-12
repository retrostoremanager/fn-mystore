using MyStore.Models;

namespace MyStore.Repositories;

public interface IInventoryRepository
{
    Task<List<InventoryItem>> GetAllAsync();
    Task<InventoryItem?> GetByIdAsync(int id);
    Task<InventoryItem> CreateAsync(InventoryItem item);
    Task<InventoryItem?> UpdateAsync(int id, InventoryItem item);
    Task<bool> DeleteAsync(int id);
    Task<List<InventoryItem>> SearchAsync(string searchTerm);
    Task<bool> UpdateQuantityAsync(int id, int quantityChange);
}

