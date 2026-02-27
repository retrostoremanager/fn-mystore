using MyStore.Models;

namespace MyStore.Repositories;

public interface ISubscriptionRepository
{
    Task<Subscription?> CreateAsync(Subscription subscription);
    Task<Subscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId);
    Task<Subscription?> GetByCompanyIdAsync(int companyId);
    Task<Subscription?> UpdateAsync(Subscription subscription);
}
