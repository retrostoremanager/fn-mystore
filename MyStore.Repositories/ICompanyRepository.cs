using MyStore.Models;

namespace MyStore.Repositories;

public interface ICompanyRepository
{
    Task<Company?> GetByIdAsync(int id);
    Task<Company?> GetByEmailAsync(string email);
    /// <summary>
    /// Gets company by slug for path-based login (/{slug}/login).
    /// </summary>
    Task<Company?> GetBySlugAsync(string slug);
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
    /// <summary>
    /// Gets companies with trial expired 7+ days ago, no payment method, not suspended (EPIC-0-006-005).
    /// </summary>
    Task<IEnumerable<Company>> GetExpiredTrialsForSuspensionAsync();
    /// <summary>
    /// Updates company status (e.g., Active to Suspended).
    /// </summary>
    Task UpdateStatusAsync(int companyId, string status);
    /// <summary>
    /// Gets company profile (store info) for display/edit (EPIC-0-007).
    /// </summary>
    Task<CompanyProfile?> GetProfileAsync(int companyId);
    /// <summary>
    /// Updates company profile fields (EPIC-0-007).
    /// </summary>
    Task UpdateProfileAsync(int companyId, CompanyProfileUpdateRequest request);
    /// <summary>
    /// Gets tax settings for a company (Issues #163, #288).
    /// </summary>
    Task<TaxSettingsResponse?> GetTaxSettingsAsync(int companyId);
    /// <summary>
    /// Updates tax settings for a company (Issues #163, #288).
    /// </summary>
    Task UpdateTaxSettingsAsync(int companyId, TaxSettingsRequest request);
    Task<Company?> GetByVerificationTokenAsync(string token);
    Task<Company?> GetByPasswordResetTokenAsync(string token);
    Task<Company> CreateAsync(Company company);
    Task<Company?> UpdateAsync(int id, Company company);
    Task<bool> DeleteAsync(int id);
}
