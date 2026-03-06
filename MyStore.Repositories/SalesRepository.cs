using MyStore.Models;

namespace MyStore.Repositories;

public class SalesRepository : ISalesRepository
{
    private static readonly List<Sale> _sales = new();
    private static int _nextId = 1;

    public Task<List<Sale>> GetAllAsync(int companyId)
    {
        var results = _sales.Where(s => s.CompanyId == companyId).ToList();
        return Task.FromResult(results);
    }

    public Task<Sale?> GetByIdAsync(int id, int companyId)
    {
        var sale = _sales.FirstOrDefault(s => s.Id == id && s.CompanyId == companyId);
        return Task.FromResult(sale);
    }

    public Task<List<Sale>> GetByCustomerIdAsync(int customerId, int companyId)
    {
        var results = _sales.Where(s => s.CustomerId == customerId && s.CompanyId == companyId).ToList();
        return Task.FromResult(results);
    }

    public Task<List<Sale>> GetByUserIdAsync(int userId, int companyId)
    {
        var results = _sales.Where(s => s.UserId == userId && s.CompanyId == companyId).ToList();
        return Task.FromResult(results);
    }

    public Task<List<Sale>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, int companyId)
    {
        var results = _sales.Where(s => s.SaleDate >= startDate && s.SaleDate <= endDate && s.CompanyId == companyId).ToList();
        return Task.FromResult(results);
    }

    public Task<Sale> CreateAsync(Sale sale)
    {
        sale.Id = _nextId++;
        sale.SaleDate = DateTime.UtcNow;
        
        // Assign IDs to sale items
        foreach (var item in sale.Items)
        {
            item.Id = _nextId++;
            item.SaleId = sale.Id;
        }

        _sales.Add(sale);
        return Task.FromResult(sale);
    }

    public Task<bool> DeleteAsync(int id, int companyId)
    {
        var sale = _sales.FirstOrDefault(s => s.Id == id && s.CompanyId == companyId);
        if (sale == null)
        {
            return Task.FromResult(false);
        }

        _sales.Remove(sale);
        return Task.FromResult(true);
    }
}

