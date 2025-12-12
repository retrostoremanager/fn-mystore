using MyStore.Models;

namespace MyStore.Repositories;

public interface ISalesRepository
{
    Task<List<Sale>> GetAllAsync();
    Task<Sale?> GetByIdAsync(int id);
    Task<List<Sale>> GetByCustomerIdAsync(int customerId);
    Task<List<Sale>> GetByEmployeeIdAsync(int employeeId);
    Task<List<Sale>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<Sale> CreateAsync(Sale sale);
    Task<bool> DeleteAsync(int id);
}

