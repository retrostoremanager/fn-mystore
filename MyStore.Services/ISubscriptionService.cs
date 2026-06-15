using Stripe;

namespace MyStore.Services;

/// <summary>
/// Service for processing Stripe subscription webhook events.
/// Updates subscription status in the database.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Process a Stripe event (subscription.created, subscription.updated, invoice.payment_succeeded, invoice.payment_failed).
    /// Handles idempotency for duplicate webhook deliveries.
    /// </summary>
    Task ProcessStripeEventAsync(Event stripeEvent);
}
