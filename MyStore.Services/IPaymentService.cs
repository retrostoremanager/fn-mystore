using MyStore.Models;

namespace MyStore.Services;

public interface IPaymentService
{
    Task<ApiResponse<StorePaymentMethodResponse>> StorePaymentMethodAsync(int companyId, StorePaymentMethodRequest request);
    Task<ApiResponse<IEnumerable<StorePaymentMethodResponse>>> GetPaymentMethodsAsync(int companyId);
    Task<ApiResponse<StorePaymentMethodResponse>> SetDefaultPaymentMethodAsync(int companyId, int paymentMethodId);
    Task<ApiResponse<object>> DeletePaymentMethodAsync(int companyId, int paymentMethodId);
}
