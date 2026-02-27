using MyStore.Models;

namespace MyStore.Services;

/// <summary>
/// Service for changing subscription tier (upgrade/downgrade).
/// EPIC-0-009-002: Subscription Upgrade/Downgrade UI
/// </summary>
public interface ISubscriptionChangeService
{
    /// <summary>
    /// Changes the company's subscription tier.
    /// For trial users: updates Company.SubscriptionTier only.
    /// For paid users: updates Stripe subscription (upgrade = immediate prorated, downgrade = end of period).
    /// </summary>
    /// <param name="companyId">Company ID</param>
    /// <param name="newTier">Target tier: Basic, Premium, or Enterprise</param>
    /// <param name="locationCount">Current location count (for downgrade validation)</param>
    /// <returns>Success status and optional message (e.g., downgrade scheduled for date)</returns>
    Task<SubscriptionChangeResult> ChangeTierAsync(int companyId, string newTier, int locationCount);
}
