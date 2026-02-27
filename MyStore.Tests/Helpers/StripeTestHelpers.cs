using System.Security.Cryptography;
using System.Text;
using Stripe;

namespace MyStore.Tests.Helpers;

/// <summary>
/// Helpers for creating Stripe webhook events in unit tests.
/// </summary>
public static class StripeTestHelpers
{
    /// <summary>
    /// Creates a valid Stripe Event by generating a signed payload.
    /// Payload should be the full event JSON (as sent by Stripe webhook).
    /// Use webhookSecret "whsec_test" for tests.
    /// </summary>
    public static Event CreateSignedEvent(string fullEventPayload, string webhookSecret = "whsec_test")
    {
        var (_, header) = CreateSignedPayloadAndHeader(fullEventPayload, webhookSecret);
        return EventUtility.ConstructEvent(fullEventPayload, header, webhookSecret, throwOnApiVersionMismatch: false);
    }

    /// <summary>
    /// Creates signed payload and Stripe-Signature header for webhook request testing.
    /// Returns (payload, "t=timestamp,v1=signature").
    /// </summary>
    public static (string Payload, string SignatureHeader) CreateSignedPayloadAndHeader(string fullEventPayload, string webhookSecret = "whsec_test")
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{timestamp}.{fullEventPayload}";
        var signature = ComputeHmacSha256(webhookSecret, signedPayload);
        var header = $"t={timestamp},v1={signature}";
        return (fullEventPayload, header);
    }

    /// <summary>
    /// Creates a customer.subscription.created/updated event payload (object only).
    /// </summary>
    public static string CreateSubscriptionObjectPayload(
        string subscriptionId = "sub_test123",
        string customerId = "cus_test123",
        string status = "active",
        long? currentPeriodStart = null,
        long? currentPeriodEnd = null)
    {
        var start = currentPeriodStart ?? DateTimeOffset.UtcNow.AddMonths(-1).ToUnixTimeSeconds();
        var end = currentPeriodEnd ?? DateTimeOffset.UtcNow.AddMonths(1).ToUnixTimeSeconds();
        return $$"""
            {
                "id": "{{subscriptionId}}",
                "object": "subscription",
                "customer": "{{customerId}}",
                "status": "{{status}}",
                "current_period_start": {{start}},
                "current_period_end": {{end}},
                "cancel_at_period_end": false,
                "items": {
                    "object": "list",
                    "data": [{
                        "id": "si_test",
                        "object": "subscription_item",
                        "price": {
                            "id": "price_test123",
                            "object": "price"
                        }
                    }]
                }
            }
            """;
    }

    /// <summary>
    /// Creates an invoice.payment_succeeded/failed event payload (object only).
    /// </summary>
    public static string CreateInvoiceObjectPayload(
        string invoiceId = "in_test123",
        string subscriptionId = "sub_test123")
    {
        return $$"""
            {
                "id": "{{invoiceId}}",
                "object": "invoice",
                "subscription": "{{subscriptionId}}"
            }
            """;
    }

    /// <summary>
    /// Wraps an object payload in the full Stripe event structure.
    /// Includes required fields (created, request) for Stripe Event deserialization.
    /// </summary>
    public static string WrapInEventPayload(string objectJson, string eventId = "evt_test123", string eventType = "customer.subscription.created")
    {
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"{{\"id\":\"{eventId}\",\"object\":\"event\",\"type\":\"{eventType}\",\"created\":{created},\"data\":{{\"object\":{objectJson}}},\"request\":{{\"id\":null,\"idempotency_key\":null}}}}";
    }

    private static string ComputeHmacSha256(string secret, string payload)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
