using MyStore.Models;

namespace MyStore.Services;

public interface IPaymentService
{
    Task<ApiResponse<StorePaymentMethodResponse>> StorePaymentMethodAsync(int companyId, StorePaymentMethodRequest request);
    Task<ApiResponse<IEnumerable<StorePaymentMethodResponse>>> GetPaymentMethodsAsync(int companyId);
}
