using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyStore.Models;
using MyStore.Repositories;
using Stripe;

namespace MyStore.Services;

/// <summary>
/// Converts expired trials to paid Stripe subscriptions (EPIC-0-006-004).
/// </summary>
public class TrialConversionService : ITrialConversionService
{
    private readonly ICompanyRepository _companyRepository;
    private readonly StripeOptions _stripeOptions;
    private readonly ILogger<TrialConversionService> _logger;

    public TrialConversionService(
        ICompanyRepository companyRepository,
        IOptions<StripeOptions> stripeOptions,
        ILogger<TrialConversionService> logger)
    {
        _companyRepository = companyRepository;
        _stripeOptions = stripeOptions.Value;
        _logger = logger;
    }

    public async Task<int> ProcessExpiredTrialsAsync()
    {
        if (!_stripeOptions.IsConfigured)
        {
            _logger.LogWarning("Stripe not configured. Skipping trial conversion.");
            return 0;
        }

        var priceIdBasic = _stripeOptions.PriceIdBasic;
        if (string.IsNullOrWhiteSpace(priceIdBasic))
        {
            _logger.LogWarning("Stripe PriceIdBasic not configured. Skipping trial conversion.");
            return 0;
        }

        var candidates = (await _companyRepository.GetExpiredTrialsForConversionAsync()).ToList();
        _logger.LogInformation("Found {Count} companies with expired trial ready for conversion", candidates.Count);

        var converted = 0;
        foreach (var company in candidates)
        {
            try
            {
                var targetTier = ResolveTargetTier(company.SubscriptionTier);
                var priceId = _stripeOptions.GetPriceIdForTier(targetTier);
                if (string.IsNullOrWhiteSpace(priceId))
                {
                    priceId = priceIdBasic;
                }

                var stripeSubscriptionService = new Stripe.SubscriptionService();
                var subscription = await stripeSubscriptionService.CreateAsync(new SubscriptionCreateOptions
                {
                    Customer = company.StripeCustomerId,
                    Items = new List<SubscriptionItemOptions>
                    {
                        new() { Price = priceId }
                    },
                    DefaultPaymentMethod = company.DefaultPaymentMethodId,
                    Metadata = new Dictionary<string, string> { { "company_id", company.Id.ToString() } }
                });

                if (subscription != null && !string.IsNullOrEmpty(subscription.Id))
                {
                    var paidTier = NormalizeTierForStorage(targetTier);
                    await _companyRepository.UpdateSubscriptionTierAsync(company.Id, paidTier);
                    converted++;
                    _logger.LogInformation(
                        "Converted company {CompanyId} to paid subscription {SubscriptionId} (tier: {Tier})",
                        company.Id, subscription.Id, paidTier);
                }
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe error converting company {CompanyId} to paid: {Message}",
                    company.Id, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting company {CompanyId} to paid subscription",
                    company.Id);
            }
        }

        return converted;
    }

    private static string ResolveTargetTier(string currentTier)
    {
        var t = (currentTier ?? "").Trim().ToLowerInvariant();
        if (t is "trial" or "" or "basic") return "basic";
        if (t == "pro") return "pro";
        if (t == "enterprise") return "enterprise";
        return "basic";
    }

    private static string NormalizeTierForStorage(string tier)
    {
        return tier?.ToLowerInvariant() switch
        {
            "pro" => "Pro",
            "enterprise" => "Enterprise",
            _ => "Basic"
        };
    }
}
