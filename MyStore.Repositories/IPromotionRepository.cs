using MyStore.Models;

namespace MyStore.Repositories;

public interface IPromotionRepository
{
    Task<List<Promotion>> GetAllAsync(int companyId);
    Task<Promotion?> GetByIdAsync(int id, int companyId);
    Task<Promotion> CreateAsync(Promotion promotion);
    Task<Promotion?> UpdateAsync(Promotion promotion);
    Task<bool> DeleteAsync(int id, int companyId);
    Task<List<Promotion>> GetActiveAsync(int companyId, DateTime date);
}
