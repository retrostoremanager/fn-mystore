using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyStore.Models;
using MyStore.Repositories;
using Stripe;

namespace MyStore.Services;

/// <summary>
/// Service for changing subscription tier (upgrade/downgrade).
/// EPIC-0-009-002: Subscription Upgrade/Downgrade UI
/// </summary>
public class SubscriptionChangeService : ISubscriptionChangeService
{
    private static readonly Dictionary<string, int> TierOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Basic"] = 1,
        ["Premium"] = 2,
        ["Enterprise"] = 3
    };

    private static readonly Dictionary<string, int?> TierLocationLimits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Basic"] = 1,
        ["Premium"] = 3,
        ["Enterprise"] = null
    };

    private readonly ICompanyRepository _companyRepository;
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly StripeOptions _stripeOptions;
    private readonly ILogger<SubscriptionChangeService> _logger;

    public SubscriptionChangeService(
        ICompanyRepository companyRepository,
        ISubscriptionRepository subscriptionRepository,
        IOptions<StripeOptions> stripeOptions,
        ILogger<SubscriptionChangeService> logger)
    {
        _companyRepository = companyRepository;
        _subscriptionRepository = subscriptionRepository;
        _stripeOptions = stripeOptions.Value;
        _logger = logger;
    }

    public async Task<SubscriptionChangeResult> ChangeTierAsync(int companyId, string newTier, int locationCount)
    {
        var normalizedTier = NormalizeTier(newTier);
        if (string.IsNullOrEmpty(normalizedTier) || normalizedTier == "Trial")
        {
            return new SubscriptionChangeResult
            {
                Success = false,
                Message = "Invalid tier. Must be Basic, Premium, or Enterprise."
            };
        }

        var company = await _companyRepository.GetByIdAsync(companyId);
        if (company == null)
        {
            return new SubscriptionChangeResult { Success = false, Message = "Company not found." };
        }

        var currentTier = NormalizeTier(company.SubscriptionTier);
        if (currentTier == normalizedTier)
        {
            return new SubscriptionChangeResult
            {
                Success = true,
                Message = "Already on this tier."
            };
        }

        var limit = TierLocationLimits.GetValueOrDefault(normalizedTier);
        if (limit.HasValue && locationCount > limit.Value)
        {
            return new SubscriptionChangeResult
            {
                Success = false,
                Message = $"Cannot downgrade to {normalizedTier}: you have {locationCount} location(s) but {normalizedTier} allows {limit.Value}. Remove locations first."
            };
        }

        var isUpgrade = IsUpgrade(currentTier, normalizedTier);
        var now = DateTime.UtcNow;
        var isInTrial = company.TrialEndDate > now;

        if (isInTrial)
        {
            await _companyRepository.UpdateSubscriptionTierAsync(companyId, normalizedTier);
            _logger.LogInformation("Updated trial company {CompanyId} tier to {Tier}", companyId, normalizedTier);
            return new SubscriptionChangeResult
            {
                Success = true,
                Message = $"Tier updated to {normalizedTier}. Change takes effect when your trial ends."
            };
        }

        var subscription = await _subscriptionRepository.GetByCompanyIdAsync(companyId);
        if (subscription == null || string.IsNullOrEmpty(subscription.StripeSubscriptionId))
        {
            return new SubscriptionChangeResult
            {
                Success = false,
                Message = "No active subscription found. Please add a payment method and wait for trial conversion."
            };
        }

        if (!_stripeOptions.IsConfigured || string.IsNullOrWhiteSpace(_stripeOptions.PriceIdBasic))
        {
            return new SubscriptionChangeResult
            {
                Success = false,
                Message = "Subscription management is not configured."
            };
        }

        var priceId = _stripeOptions.GetPriceIdForTier(normalizedTier);
        if (string.IsNullOrEmpty(priceId))
        {
            return new SubscriptionChangeResult
            {
                Success = false,
                Message = $"Price for tier {normalizedTier} is not configured."
            };
        }

        try
        {
            var subscriptionService = new Stripe.SubscriptionService();
            var stripeSub = await subscriptionService.GetAsync(subscription.StripeSubscriptionId);
            var itemId = stripeSub.Items?.Data?.FirstOrDefault()?.Id;
            if (string.IsNullOrEmpty(itemId))
            {
                return new SubscriptionChangeResult
                {
                    Success = false,
                    Message = "Could not find subscription item to update."
                };
            }

            if (isUpgrade)
            {
                await subscriptionService.UpdateAsync(subscription.StripeSubscriptionId, new SubscriptionUpdateOptions
                {
                    Items = new List<SubscriptionItemOptions>
                    {
                        new()
                        {
                            Id = itemId,
                            Price = priceId
                        }
                    },
                    ProrationBehavior = "create_prorations"
                });
                await _companyRepository.UpdateSubscriptionTierAsync(companyId, normalizedTier);
                _logger.LogInformation("Upgraded company {CompanyId} to {Tier}", companyId, normalizedTier);
                return new SubscriptionChangeResult
                {
                    Success = true,
                    Message = $"Upgraded to {normalizedTier}. Prorated charge applied."
                };
            }
            else
            {
                await subscriptionService.UpdateAsync(subscription.StripeSubscriptionId, new SubscriptionUpdateOptions
                {
                    Items = new List<SubscriptionItemOptions>
                    {
                        new()
                        {
                            Id = itemId,
                            Price = priceId
                        }
                    },
                    ProrationBehavior = "none"
                });
                var periodEnd = subscription.CurrentPeriodEnd ?? DateTime.UtcNow.AddMonths(1);
                await _companyRepository.UpdateSubscriptionTierAsync(companyId, normalizedTier);
                _logger.LogInformation("Scheduled downgrade for company {CompanyId} to {Tier} at {Date}", companyId, normalizedTier, periodEnd);
                return new SubscriptionChangeResult
                {
                    Success = true,
                    Message = $"Downgrade to {normalizedTier} scheduled for end of billing period.",
                    EffectiveDate = periodEnd
                };
            }
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error changing tier for company {CompanyId}: {Message}", companyId, ex.Message);
            return new SubscriptionChangeResult
            {
                Success = false,
                Message = ex.Message ?? "Stripe error updating subscription."
            };
        }
    }

    private static string NormalizeTier(string? tier)
    {
        var t = (tier ?? "").Trim();
        if (string.IsNullOrEmpty(t)) return "";
        return t.ToLowerInvariant() switch
        {
            "basic" => "Basic",
            "premium" or "pro" => "Premium",
            "enterprise" => "Enterprise",
            _ => t
        };
    }

    private static bool IsUpgrade(string current, string target)
    {
        var currentOrder = TierOrder.GetValueOrDefault(current, 0);
        var targetOrder = TierOrder.GetValueOrDefault(target, 0);
        return targetOrder > currentOrder;
    }
}
