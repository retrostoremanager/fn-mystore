using MyStore.Models;

namespace MyStore.Repositories;

public interface IConsignmentRepository
{
    Task<List<ConsignmentItem>> GetAllAsync(int companyId, string? status = null);
    Task<ConsignmentItem?> GetByIdAsync(int id, int companyId);
    Task<ConsignmentItem> CreateAsync(ConsignmentItem item);
    Task<ConsignmentItem?> UpdateAsync(ConsignmentItem item);
    Task<ConsignmentItem?> MarkSoldAsync(int id, decimal salePrice, int companyId);
    Task<List<ConsignmentPayout>> GetPayoutsAsync(int itemId, int companyId);
    Task<ConsignmentPayout> CreatePayoutAsync(ConsignmentPayout payout);
}
