using MyStore.Models;

namespace MyStore.Services;

public interface IReceiptService
{
    Task<ApiResponse<ReceiptResponse>> GetReceiptAsync(int saleId, int companyId);
    Task<ApiResponse<bool>> SendReceiptEmailAsync(int saleId, int companyId, string toEmail);
}
