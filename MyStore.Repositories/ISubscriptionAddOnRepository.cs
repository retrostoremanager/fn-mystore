using MyStore.Models;

namespace MyStore.Repositories;

public interface ISubscriptionAddOnRepository
{
    Task<SubscriptionAddOn?> CreateAsync(SubscriptionAddOn addOn);
    Task<SubscriptionAddOn?> GetBySubscriptionIdAndTypeAsync(int subscriptionId, string addonType);
    Task<IEnumerable<SubscriptionAddOn>> GetBySubscriptionIdAsync(int subscriptionId);
    Task<SubscriptionAddOn?> UpdateAsync(SubscriptionAddOn addOn);
}
