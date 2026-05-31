using MyStore.Models;

namespace MyStore.Services;

public interface ISalesService
{
    Task<ApiResponse<List<Sale>>> GetAllSalesAsync(int companyId);
    Task<ApiResponse<Sale>> GetSaleByIdAsync(int id, int companyId);
    Task<ApiResponse<List<Sale>>> GetSalesByCustomerIdAsync(int customerId, int companyId);
    Task<ApiResponse<List<Sale>>> GetSalesByDateRangeAsync(DateTime startDate, DateTime endDate, int companyId);
    Task<ApiResponse<Sale>> CreateSaleAsync(CreateSaleRequest request, int companyId);
    Task<ApiResponse<bool>> DeleteSaleAsync(int id, int companyId);
    Task<ApiResponse<ReceiptResponse>> GetReceiptAsync(int id, int companyId);
}

