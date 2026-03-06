using MyStore.Models;

namespace MyStore.Repositories;

public class InventoryRepository : IInventoryRepository
{
    // In-memory storage for now. Replace with actual database (e.g., Cosmos DB, SQL Server) later
    private static readonly List<InventoryItem> _inventory = new();
    private static int _nextId = 1;

    public Task<List<InventoryItem>> GetAllAsync(int companyId)
    {
        var results = _inventory.Where(i => i.CompanyId == companyId).ToList();
        return Task.FromResult(results);
    }

    public Task<InventoryItem?> GetByIdAsync(int id, int companyId)
    {
        var item = _inventory.FirstOrDefault(i => i.Id == id && i.CompanyId == companyId);
        return Task.FromResult(item);
    }

    public Task<InventoryItem> CreateAsync(InventoryItem item)
    {
        item.Id = _nextId++;
        item.AddedDate = DateTime.UtcNow;
        item.LastModifiedDate = DateTime.UtcNow;
        _inventory.Add(item);
        return Task.FromResult(item);
    }

    public Task<InventoryItem?> UpdateAsync(int id, InventoryItem item, int companyId)
    {
        var existing = _inventory.FirstOrDefault(i => i.Id == id && i.CompanyId == companyId);
        if (existing == null)
        {
            return Task.FromResult<InventoryItem?>(null);
        }

        existing.Name = item.Name;
        existing.LocationId = item.LocationId;
        existing.Category = item.Category;
        existing.Quantity = item.Quantity;
        existing.SellPrice = item.SellPrice;
        existing.BuyPrice = item.BuyPrice;
        existing.Condition = item.Condition;
        existing.Game = item.Game;
        existing.Completeness = item.Completeness;
        existing.Notes = item.Notes;
        existing.LastModifiedDate = DateTime.UtcNow;

        return Task.FromResult<InventoryItem?>(existing);
    }

    public Task<bool> DeleteAsync(int id, int companyId)
    {
        var item = _inventory.FirstOrDefault(i => i.Id == id && i.CompanyId == companyId);
        if (item == null)
        {
            return Task.FromResult(false);
        }

        _inventory.Remove(item);
        return Task.FromResult(true);
    }

    public Task<List<InventoryItem>> SearchAsync(string searchTerm, int companyId)
    {
        var term = searchTerm.ToLowerInvariant();
        var results = _inventory.Where(i =>
            i.CompanyId == companyId &&
            (i.Name.ToLowerInvariant().Contains(term) ||
            i.Category.ToLowerInvariant().Contains(term) ||
            (i.Game != null && i.Game.Title.ToLowerInvariant().Contains(term)) ||
            (i.Game != null && i.Game.Console.ToLowerInvariant().Contains(term)))
        ).ToList();

        return Task.FromResult(results);
    }

    public Task<bool> UpdateQuantityAsync(int id, int quantityChange, int companyId)
    {
        var item = _inventory.FirstOrDefault(i => i.Id == id && i.CompanyId == companyId);
        if (item == null)
        {
            return Task.FromResult(false);
        }

        item.Quantity += quantityChange;
        if (item.Quantity < 0)
        {
            item.Quantity = 0;
        }

        item.LastModifiedDate = DateTime.UtcNow;
        return Task.FromResult(true);
    }
}

