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

/// <summary>
/// An add-on line item stacked on a company's existing Stripe subscription (e.g. Lot Scanner,
/// $49/mo). subscription_addon is a child table keyed by subscription_id since `subscription`
/// enforces exactly one row per Stripe subscription ID.
/// </summary>
public class SubscriptionAddOn
{
    public int Id { get; set; }
    public int SubscriptionId { get; set; }
    public string AddonType { get; set; } = string.Empty; // "lot_scanner" for now
    public string StripeSubscriptionItemId { get; set; } = string.Empty;
    public string StripePriceId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // active, past_due, canceled
    public DateTime CreatedDate { get; set; }
    public DateTime? LastModifiedDate { get; set; }
}

/// <summary>
/// Result of subscribing to / canceling an add-on. Mirrors SubscriptionChangeResult's shape.
/// </summary>
public class AddOnChangeResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Add-on status surfaced in GET billing/subscription (SubscriptionDetailResponse.AddOns).
/// </summary>
public class AddOnStatusResponse
{
    public string AddonType { get; set; } = string.Empty;
    public bool Active { get; set; }
    public string? Status { get; set; }
}
