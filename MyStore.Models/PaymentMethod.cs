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
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? LastModifiedDate { get; set; }
}

/// <summary>
/// Invoice summary returned by GET /billing/invoices.
/// </summary>
public class InvoiceSummary
{
    public string Id { get; set; } = string.Empty;
    public string? Number { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime Created { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string? HostedInvoiceUrl { get; set; }
    public string? InvoicePdf { get; set; }
}

/// <summary>
/// Paginated invoice list response for GET /billing/invoices.
/// </summary>
public class InvoiceListResponse
{
    public List<InvoiceSummary> Invoices { get; set; } = new();
    public bool HasMore { get; set; }
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
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public bool IsDefault { get; set; }
}
