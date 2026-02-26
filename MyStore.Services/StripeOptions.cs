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
}
