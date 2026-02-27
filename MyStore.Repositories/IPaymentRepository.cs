using MyStore.Models;

namespace MyStore.Repositories;

public interface IPaymentRepository
{
    Task<PaymentMethod?> CreateAsync(PaymentMethod paymentMethod);
    Task<IEnumerable<PaymentMethod>> GetByCompanyIdAsync(int companyId);
    Task<PaymentMethod?> GetByIdAsync(int id, int companyId);
    Task SetDefaultAsync(int companyId, int paymentMethodId);
    Task<int?> GetCompanyIdByStripeCustomerIdAsync(string stripeCustomerId);
}
