namespace MyStore.Models;

/// <summary>
/// Request to change subscription tier. EPIC-0-009-002.
/// </summary>
public class ChangeSubscriptionTierRequest
{
    public string Tier { get; set; } = string.Empty; // Basic, Premium, or Enterprise
    public int LocationCount { get; set; }
}

/// <summary>
/// Result of a subscription tier change. EPIC-0-009-002.
/// </summary>
public class SubscriptionChangeResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    /// <summary>
    /// When downgrade is scheduled for end of period.
    /// </summary>
    public DateTime? EffectiveDate { get; set; }
}
