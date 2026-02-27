using MyStore.Models;

namespace MyStore.Repositories;

public interface ICompanyRepository
{
    Task<Company?> GetByIdAsync(int id);
    Task<Company?> GetByEmailAsync(string email);
    /// <summary>
    /// Gets companies in trial with expiration in exactly the specified number of days
    /// who have not yet received that notification.
    /// </summary>
    Task<IEnumerable<Company>> GetExpiringTrialsAsync(int daysRemaining);
    /// <summary>
    /// Marks that a trial expiration notification was sent for the company.
    /// </summary>
    Task MarkTrialNotificationSentAsync(int companyId, int daysRemaining);
    /// <summary>
    /// Gets companies with expired trial who have payment method and no active subscription (EPIC-0-006-004).
    /// </summary>
    Task<IEnumerable<TrialConversionCandidate>> GetExpiredTrialsForConversionAsync();
    /// <summary>
    /// Updates company subscription tier (e.g., Trial to Basic after conversion).
    /// </summary>
    Task UpdateSubscriptionTierAsync(int companyId, string subscriptionTier);
    Task<Company?> GetByVerificationTokenAsync(string token);
    Task<Company?> GetByPasswordResetTokenAsync(string token);
    Task<Company> CreateAsync(Company company);
    Task<Company?> UpdateAsync(int id, Company company);
    Task<bool> DeleteAsync(int id);
}
