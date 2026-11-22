using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyStore.Models;
using MyStore.Repositories;
using Stripe;

namespace MyStore.Services;

/// <summary>
/// Subscribes/cancels the Lot Scanner add-on: a second line item stacked on a company's
/// existing Stripe subscription (one invoice), billed against the customer's existing
/// default payment method. Mirrors SubscriptionChangeService's Stripe-call pattern, but
/// appends/removes an item instead of swapping the single tier item in place.
/// </summary>
public class LotScannerAddOnService : ILotScannerAddOnService
{
    private const string AddonType = "lot_scanner";

    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly ISubscriptionAddOnRepository _addOnRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly StripeOptions _stripeOptions;
    private readonly ILogger<LotScannerAddOnService> _logger;

    public LotScannerAddOnService(
        ISubscriptionRepository subscriptionRepository,
        ISubscriptionAddOnRepository addOnRepository,
        IPaymentRepository paymentRepository,
        IOptions<StripeOptions> stripeOptions,
        ILogger<LotScannerAddOnService> logger)
    {
        _subscriptionRepository = subscriptionRepository;
        _addOnRepository = addOnRepository;
        _paymentRepository = paymentRepository;
        _stripeOptions = stripeOptions.Value;
        _logger = logger;
    }

    public async Task<AddOnChangeResult> SubscribeAsync(int companyId)
    {
        if (!_stripeOptions.IsConfigured || string.IsNullOrWhiteSpace(_stripeOptions.AddOnPriceIdLotScanner))
        {
            return new AddOnChangeResult { Success = false, Message = "Lot Scanner add-on is not configured." };
        }

        var subscription = await _subscriptionRepository.GetByCompanyIdAsync(companyId);
        if (subscription == null || string.IsNullOrEmpty(subscription.StripeSubscriptionId))
        {
            return new AddOnChangeResult { Success = false, Message = "No active subscription found. Add a base plan first." };
        }

        var paymentMethods = await _paymentRepository.GetByCompanyIdAsync(companyId);
        if (!paymentMethods.Any(p => p.IsDefault))
        {
            return new AddOnChangeResult
            {
                Success = false,
                Message = "Add a payment method on the RetroStoreManager web app before enabling this add-on."
            };
        }

        var existing = await _addOnRepository.GetBySubscriptionIdAndTypeAsync(subscription.Id, AddonType);
        if (existing != null && existing.Status == "active")
        {
            return new AddOnChangeResult { Success = true, Message = "Lot Scanner add-on is already active." };
        }

        try
        {
            var itemService = new SubscriptionItemService();
            var item = await itemService.CreateAsync(new SubscriptionItemCreateOptions
            {
                Subscription = subscription.StripeSubscriptionId,
                Price = _stripeOptions.AddOnPriceIdLotScanner,
                ProrationBehavior = "create_prorations"
            });

            if (existing != null)
            {
                existing.StripeSubscriptionItemId = item.Id;
                existing.StripePriceId = _stripeOptions.AddOnPriceIdLotScanner!;
                existing.Status = "active";
                await _addOnRepository.UpdateAsync(existing);
            }
            else
            {
                await _addOnRepository.CreateAsync(new SubscriptionAddOn
                {
                    SubscriptionId = subscription.Id,
                    AddonType = AddonType,
                    StripeSubscriptionItemId = item.Id,
                    StripePriceId = _stripeOptions.AddOnPriceIdLotScanner!,
                    Status = "active",
                    CreatedDate = DateTime.UtcNow
                });
            }

            _logger.LogInformation("Subscribed company {CompanyId} to Lot Scanner add-on (item {ItemId})", companyId, item.Id);
            return new AddOnChangeResult { Success = true, Message = "Lot Scanner add-on enabled. Prorated charge applied." };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error subscribing company {CompanyId} to Lot Scanner add-on: {Message}", companyId, ex.Message);
            return new AddOnChangeResult { Success = false, Message = ex.Message ?? "Stripe error enabling the add-on." };
        }
    }

    public async Task<AddOnChangeResult> CancelAsync(int companyId)
    {
        var subscription = await _subscriptionRepository.GetByCompanyIdAsync(companyId);
        if (subscription == null)
        {
            return new AddOnChangeResult { Success = false, Message = "No subscription found." };
        }

        var existing = await _addOnRepository.GetBySubscriptionIdAndTypeAsync(subscription.Id, AddonType);
        if (existing == null || existing.Status != "active")
        {
            return new AddOnChangeResult { Success = false, Message = "No active Lot Scanner add-on to cancel." };
        }

        try
        {
            var itemService = new SubscriptionItemService();
            await itemService.DeleteAsync(existing.StripeSubscriptionItemId, new SubscriptionItemDeleteOptions
            {
                ProrationBehavior = "create_prorations"
            });

            existing.Status = "canceled";
            await _addOnRepository.UpdateAsync(existing);

            _logger.LogInformation("Canceled Lot Scanner add-on for company {CompanyId} (item {ItemId})", companyId, existing.StripeSubscriptionItemId);
            return new AddOnChangeResult { Success = true, Message = "Lot Scanner add-on canceled." };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error canceling Lot Scanner add-on for company {CompanyId}: {Message}", companyId, ex.Message);
            return new AddOnChangeResult { Success = false, Message = ex.Message ?? "Stripe error canceling the add-on." };
        }
    }
}
