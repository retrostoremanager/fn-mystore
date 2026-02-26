namespace MyStore.Models;

/// <summary>
/// Payment method stored for subscription billing.
/// Only stores Stripe IDs and display info—never full card numbers.
/// </summary>
public class PaymentMethod
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string StripeCustomerId { get; set; } = string.Empty;
    public string StripePaymentMethodId { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? LastModifiedDate { get; set; }
}

/// <summary>
/// Request to store a payment method from the frontend.
/// Frontend creates PaymentMethod via Stripe.js and sends only the ID.
/// </summary>
public class StorePaymentMethodRequest
{
    public string PaymentMethodId { get; set; } = string.Empty;
}

/// <summary>
/// Response when storing a payment method.
/// </summary>
public class StorePaymentMethodResponse
{
    public int Id { get; set; }
    public string Last4 { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public bool IsDefault { get; set; }
}
