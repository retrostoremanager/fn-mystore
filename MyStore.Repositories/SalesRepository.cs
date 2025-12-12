using MyStore.Models;

namespace MyStore.Repositories;

public class SalesRepository : ISalesRepository
{
    private static readonly List<Sale> _sales = new();
    private static int _nextId = 1;

    public Task<List<Sale>> GetAllAsync()
    {
        return Task.FromResult(_sales.ToList());
    }

    public Task<Sale?> GetByIdAsync(int id)
    {
        var sale = _sales.FirstOrDefault(s => s.Id == id);
        return Task.FromResult(sale);
    }

    public Task<List<Sale>> GetByCustomerIdAsync(int customerId)
    {
        var results = _sales.Where(s => s.CustomerId == customerId).ToList();
        return Task.FromResult(results);
    }

    public Task<List<Sale>> GetByEmployeeIdAsync(int employeeId)
    {
        var results = _sales.Where(s => s.EmployeeId == employeeId).ToList();
        return Task.FromResult(results);
    }

    public Task<List<Sale>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        var results = _sales.Where(s => s.SaleDate >= startDate && s.SaleDate <= endDate).ToList();
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

    public Task<bool> DeleteAsync(int id)
    {
        var sale = _sales.FirstOrDefault(s => s.Id == id);
        if (sale == null)
        {
            return Task.FromResult(false);
        }

        _sales.Remove(sale);
        return Task.FromResult(true);
    }
}

