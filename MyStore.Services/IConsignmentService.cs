using MyStore.Models;

namespace MyStore.Services;

public interface IConsignmentService
{
    Task<ApiResponse<List<ConsignmentItem>>> GetAllAsync(int companyId, string? status = null);
    Task<ApiResponse<ConsignmentItem>> GetByIdAsync(int id, int companyId);
    Task<ApiResponse<ConsignmentItem>> CreateAsync(ConsignmentItem item, int companyId);
    Task<ApiResponse<ConsignmentItem>> UpdateAsync(ConsignmentItem item, int companyId);
    Task<ApiResponse<ConsignmentItem>> MarkSoldAsync(int id, decimal salePrice, int companyId);
    Task<ApiResponse<ConsignmentPayout>> ProcessPayoutAsync(int itemId, string? notes, int companyId);
    Task<ApiResponse<ConsignmentItem>> ReturnToCustomerAsync(int id, int companyId);
}
