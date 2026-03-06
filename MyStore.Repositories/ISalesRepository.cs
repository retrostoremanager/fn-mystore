using MyStore.Models;

namespace MyStore.Repositories;

public interface ISalesRepository
{
    Task<List<Sale>> GetAllAsync(int companyId);
    Task<Sale?> GetByIdAsync(int id, int companyId);
    Task<List<Sale>> GetByCustomerIdAsync(int customerId, int companyId);
    Task<List<Sale>> GetByUserIdAsync(int userId, int companyId);
    Task<List<Sale>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, int companyId);
    Task<Sale> CreateAsync(Sale sale);
    Task<bool> DeleteAsync(int id, int companyId);
}

