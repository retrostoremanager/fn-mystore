using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyStore.Models;
using MyStore.Repositories;
using Stripe;

namespace MyStore.Services;

/// <summary>
/// Processes Stripe subscription webhook events and updates subscription status in the database.
/// Implements idempotency for duplicate webhook deliveries.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private const string CustomerSubscriptionCreated = "customer.subscription.created";
    private const string CustomerSubscriptionUpdated = "customer.subscription.updated";
    private const string InvoicePaymentSucceeded = "invoice.payment_succeeded";
    private const string InvoicePaymentFailed = "invoice.payment_failed";
    private const string LotScannerAddonType = "lot_scanner";

    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly ISubscriptionAddOnRepository _addOnRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly StripeOptions _stripeOptions;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        ISubscriptionRepository subscriptionRepository,
        ISubscriptionAddOnRepository addOnRepository,
        IPaymentRepository paymentRepository,
        IOptions<StripeOptions> stripeOptions,
        ILogger<SubscriptionService> logger)
    {
        _subscriptionRepository = subscriptionRepository;
        _addOnRepository = addOnRepository;
        _paymentRepository = paymentRepository;
        _stripeOptions = stripeOptions.Value;
        _logger = logger;
    }

    public async Task ProcessStripeEventAsync(Event stripeEvent)
    {
        switch (stripeEvent.Type)
        {
            case CustomerSubscriptionCreated:
            case CustomerSubscriptionUpdated:
                await HandleSubscriptionEventAsync(stripeEvent);
                break;
            case InvoicePaymentSucceeded:
                await HandleInvoicePaymentSucceededAsync(stripeEvent);
                break;
            case InvoicePaymentFailed:
                await HandleInvoicePaymentFailedAsync(stripeEvent);
                break;
            default:
                _logger.LogDebug("Unhandled Stripe event type: {EventType}", stripeEvent.Type);
                break;
        }
    }

    private async Task HandleSubscriptionEventAsync(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Stripe.Subscription;
        if (subscription == null)
        {
            _logger.LogWarning("Subscription event {EventId} has no subscription object", stripeEvent.Id);
            return;
        }

        var existing = await _subscriptionRepository.GetByStripeSubscriptionIdAsync(subscription.Id);
        var companyId = await ResolveCompanyIdFromSubscriptionAsync(subscription);
        if (companyId == null)
        {
            _logger.LogWarning("Could not resolve company for Stripe subscription {SubscriptionId}", subscription.Id);
            return;
        }

        DateTime? periodStart = null;
        DateTime? periodEnd = null;
        var rawJson = GetRawObjectJson(stripeEvent.Data.RawObject);
        if (!string.IsNullOrEmpty(rawJson))
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("current_period_start", out JsonElement startEl) && startEl.ValueKind == JsonValueKind.Number)
                periodStart = DateTimeOffset.FromUnixTimeSeconds(startEl.GetInt64()).UtcDateTime;
            if (root.TryGetProperty("current_period_end", out JsonElement endEl) && endEl.ValueKind == JsonValueKind.Number)
                periodEnd = DateTimeOffset.FromUnixTimeSeconds(endEl.GetInt64()).UtcDateTime;
        }

        // A subscription can have 2+ items once an add-on is stacked on it (e.g. base tier +
        // Lot Scanner) -- FirstOrDefault() here used to silently drop every item after the
        // first. Split items into "the base tier item" (stored on subscription itself) vs.
        // known add-on items (stored as subscription_addon child rows) instead.
        var items = subscription.Items?.Data ?? new List<Stripe.SubscriptionItem>();
        var addOnPriceId = _stripeOptions.AddOnPriceIdLotScanner;
        var baseItem = items.FirstOrDefault(i => string.IsNullOrEmpty(addOnPriceId) || i.Price?.Id != addOnPriceId);

        var sub = new Models.Subscription
        {
            CompanyId = companyId.Value,
            StripeSubscriptionId = subscription.Id,
            StripeCustomerId = subscription.CustomerId ?? string.Empty,
            StripePriceId = baseItem?.Price?.Id,
            Status = subscription.Status ?? string.Empty,
            CurrentPeriodStart = periodStart,
            CurrentPeriodEnd = periodEnd,
            CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
            CreatedDate = DateTime.UtcNow
        };

        if (existing != null)
        {
            sub.Id = existing.Id;
            sub.CompanyId = existing.CompanyId;
            sub.CreatedDate = existing.CreatedDate;
            await _subscriptionRepository.UpdateAsync(sub);
            _logger.LogInformation("Updated subscription {SubscriptionId} status to {Status}", subscription.Id, sub.Status);
        }
        else
        {
            await _subscriptionRepository.CreateAsync(sub);
            _logger.LogInformation("Created subscription record for {SubscriptionId} status {Status}", subscription.Id, sub.Status);
        }

        await SyncLotScannerAddOnAsync(sub.Id, items, subscription.Status ?? string.Empty);
    }

    /// <summary>
    /// Keeps subscription_addon in sync with whether the Lot Scanner price is currently
    /// present among the Stripe subscription's items. Covers both directions: the add-on was
    /// just added (item present, no row or row canceled -> upsert active) and it was removed
    /// directly in the Stripe dashboard rather than through CancelAsync (row active, item gone
    /// -> mark canceled).
    /// </summary>
    private async Task SyncLotScannerAddOnAsync(int subscriptionDbId, List<Stripe.SubscriptionItem> items, string subscriptionStatus)
    {
        var addOnPriceId = _stripeOptions.AddOnPriceIdLotScanner;
        if (string.IsNullOrEmpty(addOnPriceId))
            return;

        var addOnItem = items.FirstOrDefault(i => i.Price?.Id == addOnPriceId);
        var existingAddOn = await _addOnRepository.GetBySubscriptionIdAndTypeAsync(subscriptionDbId, LotScannerAddonType);

        if (addOnItem != null)
        {
            if (existingAddOn != null)
            {
                existingAddOn.StripeSubscriptionItemId = addOnItem.Id;
                existingAddOn.StripePriceId = addOnPriceId;
                existingAddOn.Status = subscriptionStatus;
                await _addOnRepository.UpdateAsync(existingAddOn);
            }
            else
            {
                await _addOnRepository.CreateAsync(new Models.SubscriptionAddOn
                {
                    SubscriptionId = subscriptionDbId,
                    AddonType = LotScannerAddonType,
                    StripeSubscriptionItemId = addOnItem.Id,
                    StripePriceId = addOnPriceId,
                    Status = subscriptionStatus,
                    CreatedDate = DateTime.UtcNow
                });
            }
            _logger.LogInformation("Synced Lot Scanner add-on for subscription {SubscriptionDbId} to status {Status}", subscriptionDbId, subscriptionStatus);
        }
        else if (existingAddOn != null && existingAddOn.Status != "canceled")
        {
            existingAddOn.Status = "canceled";
            await _addOnRepository.UpdateAsync(existingAddOn);
            _logger.LogInformation("Lot Scanner add-on item no longer present on subscription {SubscriptionDbId}; marked canceled", subscriptionDbId);
        }
    }

    private async Task HandleInvoicePaymentSucceededAsync(Event stripeEvent)
    {
        var invoice = stripeEvent.Data.Object as Stripe.Invoice;
        var invoiceId = (invoice?.Id as string) ?? "unknown";
        var subscriptionId = GetSubscriptionIdFromRawObject(GetRawObjectJson(stripeEvent.Data.RawObject));
        if (string.IsNullOrEmpty(subscriptionId))
        {
            _logger.LogDebug("Invoice payment succeeded without subscription: {InvoiceId}", invoiceId);
            return;
        }

        var existing = await _subscriptionRepository.GetByStripeSubscriptionIdAsync(subscriptionId);
        if (existing == null)
        {
            _logger.LogWarning("Invoice payment succeeded for unknown subscription {SubscriptionId}", (object)subscriptionId);
            return;
        }

        _logger.LogInformation("Invoice {InvoiceId} paid for subscription {SubscriptionId}", (object)invoiceId, (object)subscriptionId);
    }

    private async Task HandleInvoicePaymentFailedAsync(Event stripeEvent)
    {
        var invoice = stripeEvent.Data.Object as Stripe.Invoice;
        var invoiceId = (invoice?.Id as string) ?? "unknown";
        var subscriptionId = GetSubscriptionIdFromRawObject(GetRawObjectJson(stripeEvent.Data.RawObject));
        if (string.IsNullOrEmpty(subscriptionId))
        {
            _logger.LogDebug("Invoice payment failed without subscription: {InvoiceId}", invoiceId);
            return;
        }

        var existing = await _subscriptionRepository.GetByStripeSubscriptionIdAsync(subscriptionId);
        if (existing == null)
        {
            _logger.LogWarning("Invoice payment failed for unknown subscription {SubscriptionId}", (object)subscriptionId);
            return;
        }

        existing.Status = "past_due";
        await _subscriptionRepository.UpdateAsync(existing);
        _logger.LogWarning("Marked subscription {SubscriptionId} as past_due after invoice {InvoiceId} payment failed", (object)subscriptionId, (object)invoiceId);
    }

    /// <summary>
    /// Converts Stripe Data.RawObject (dynamic, may be JToken or string) to JSON string.
    /// </summary>
    private static string? GetRawObjectJson(dynamic? rawObject)
    {
        if (rawObject == null) return null;
        return rawObject.ToString();
    }

    private static string? GetSubscriptionIdFromRawObject(string? rawObject)
    {
        if (string.IsNullOrEmpty(rawObject)) return null;
        try
        {
            using var doc = JsonDocument.Parse(rawObject);
            if (doc.RootElement.TryGetProperty("subscription", out var subEl))
            {
                if (subEl.ValueKind == JsonValueKind.String)
                    return subEl.GetString();
                if (subEl.ValueKind == JsonValueKind.Object && subEl.TryGetProperty("id", out var idEl))
                    return idEl.GetString();
            }
        }
        catch { /* ignore */ }
        return null;
    }

    private async Task<int?> ResolveCompanyIdFromSubscriptionAsync(Stripe.Subscription stripeSubscription)
    {
        var customerId = stripeSubscription.CustomerId;
        if (string.IsNullOrEmpty(customerId))
            return null;

        return await _paymentRepository.GetCompanyIdByStripeCustomerIdAsync(customerId);
    }
}
