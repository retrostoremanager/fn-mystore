namespace MyStore.Services;

/// <summary>
/// Service for converting expired trials to paid subscriptions (EPIC-0-006-004).
/// Creates Stripe subscriptions when trial expires and payment method is on file.
/// </summary>
public interface ITrialConversionService
{
    /// <summary>
    /// Processes all companies with expired trials who have payment methods.
    /// Creates Stripe subscriptions and updates company tier.
    /// </summary>
    Task<int> ProcessExpiredTrialsAsync();
}
