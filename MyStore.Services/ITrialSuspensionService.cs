namespace MyStore.Services;

/// <summary>
/// Service for suspending accounts when trial expires and no payment within 7 days (EPIC-0-006-005).
/// </summary>
public interface ITrialSuspensionService
{
    /// <summary>
    /// Suspends companies with trial expired 7+ days ago and no payment method on file.
    /// </summary>
    Task<int> SuspendExpiredTrialsAsync();
}
