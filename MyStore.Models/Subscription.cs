namespace MyStore.Models;

/// <summary>
/// Stripe subscription record for a company.
/// Status is kept in sync via Stripe webhooks.
/// </summary>
public class Subscription
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string StripeSubscriptionId { get; set; } = string.Empty;
    public string StripeCustomerId { get; set; } = string.Empty;
    public string? StripePriceId { get; set; }
    public string Status { get; set; } = string.Empty; // active, past_due, canceled, trialing, etc.
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? LastModifiedDate { get; set; }
}
