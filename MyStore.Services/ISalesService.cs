using MyStore.Models;

namespace MyStore.Services;

public interface ISalesService
{
    Task<ApiResponse<List<Sale>>> GetAllSalesAsync();
    Task<ApiResponse<Sale>> GetSaleByIdAsync(int id);
    Task<ApiResponse<List<Sale>>> GetSalesByCustomerIdAsync(int customerId);
    Task<ApiResponse<List<Sale>>> GetSalesByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<ApiResponse<Sale>> CreateSaleAsync(CreateSaleRequest request);
    Task<ApiResponse<bool>> DeleteSaleAsync(int id);
}

