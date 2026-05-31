namespace MyStore.Services;

/// <summary>
/// Configuration options for Stripe payment processing.
/// Keys are read from configuration (e.g., Stripe__SecretKey, Stripe__PublishableKey).
/// </summary>
public class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>
    /// Stripe secret API key (sk_test_... for test mode, sk_live_... for production).
    /// Required for server-side Stripe API calls.
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Stripe publishable API key (pk_test_... for test mode, pk_live_... for production).
    /// Used by the frontend for Stripe Elements; safe to expose in client code.
    /// </summary>
    public string PublishableKey { get; set; } = string.Empty;

    /// <summary>
    /// Webhook signing secret (whsec_...) for validating Stripe webhook requests.
    /// Required when processing webhooks (e.g., subscription events).
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Whether Stripe is configured and available.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);

    /// <summary>
    /// Stripe Price IDs for subscription tiers (price_xxx). Used for trial-to-paid conversion.
    /// </summary>
    public string? PriceIdBasic { get; set; }
    public string? PriceIdPro { get; set; }
    public string? PriceIdEnterprise { get; set; }

    /// <summary>
    /// Gets the Stripe Price ID for the given tier. Falls back to Basic if tier not configured.
    /// </summary>
    public string GetPriceIdForTier(string tier)
    {
        var normalized = (tier ?? "").Trim().ToLowerInvariant();
        return normalized switch
        {
            "pro" or "premium" => PriceIdPro ?? PriceIdBasic ?? "",
            "enterprise" => PriceIdEnterprise ?? PriceIdBasic ?? "",
            _ => PriceIdBasic ?? ""
        };
    }

    /// <summary>
    /// Reverse lookup: given a Stripe Price ID, return the plan tier name.
    /// Returns null if the price ID is not recognised.
    /// </summary>
    public string? GetTierNameForPriceId(string? priceId)
    {
        if (string.IsNullOrEmpty(priceId)) return null;
        if (!string.IsNullOrEmpty(PriceIdEnterprise) && priceId == PriceIdEnterprise) return "Enterprise";
        if (!string.IsNullOrEmpty(PriceIdPro)        && priceId == PriceIdPro)        return "Pro";
        if (!string.IsNullOrEmpty(PriceIdBasic)      && priceId == PriceIdBasic)      return "Basic";
        return null;
    }
}
