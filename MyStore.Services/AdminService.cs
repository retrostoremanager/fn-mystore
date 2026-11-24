using Microsoft.Extensions.Logging;
using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

/// <summary>
/// Creates companies outside the public sign-up flow, with no Stripe involvement at all --
/// for internal test/demo accounts (e.g. trying out the Lot Scanner add-on) where requiring
/// a real card makes no sense. Only reachable via AdminFunctions, which gates every call on
/// a shared admin key -- this never touches RegisterAccountAsync or the public endpoint.
/// </summary>
public class AdminService : IAdminService
{
    private const string LotScannerAddonType = "lot_scanner";

    private readonly ICompanyRepository _companyRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IUserRepository _userRepository;
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly ISubscriptionAddOnRepository _addOnRepository;
    private readonly ILogger<AdminService> _logger;

    public AdminService(
        ICompanyRepository companyRepository,
        IRoleRepository roleRepository,
        IUserRepository userRepository,
        ISubscriptionRepository subscriptionRepository,
        ISubscriptionAddOnRepository addOnRepository,
        ILogger<AdminService> logger)
    {
        _companyRepository = companyRepository;
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _subscriptionRepository = subscriptionRepository;
        _addOnRepository = addOnRepository;
        _logger = logger;
    }

    public async Task<CreateTestCompanyResponse> CreateTestCompanyAsync(CreateTestCompanyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.CompanyName) || string.IsNullOrWhiteSpace(request.Password))
            throw new ArgumentException("Email, company name, and password are all required.");

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, BCrypt.Net.BCrypt.GenerateSalt(12));

        // No email to verify and no card on file to ever expire/suspend against -- active
        // immediately, trial window pushed out 10 years so the trial-conversion and
        // trial-suspension timers (which only look at Company.TrialEndDate, not the
        // subscription row below) never try to charge or suspend a company with no
        // payment method on file.
        var now = DateTime.UtcNow;
        var company = new Company
        {
            Email = request.Email.Trim().ToLowerInvariant(),
            CompanyName = request.CompanyName.Trim(),
            PasswordHash = passwordHash,
            Status = "Active",
            TrialStartDate = now,
            TrialEndDate = now.AddDays(3650),
            SubscriptionTier = "Trial",
            CreatedDate = now,
            LastModifiedDate = now
        };
        var created = await _companyRepository.CreateAsync(company);

        // Same Owner RBAC bootstrap as RegisterAccountAsync -- without this, every
        // [RequirePermission]-gated endpoint (including billing/subscription, which the Lot
        // Scanner entitlement check calls) 403s even for the account's own owner.
        var ownerRole = (await _roleRepository.GetForCompanyAsync(created.Id))
            .FirstOrDefault(r => string.Equals(r.Name, "Owner", StringComparison.OrdinalIgnoreCase));
        if (ownerRole == null)
        {
            _logger.LogError("No global 'Owner' role found; test company {CompanyId} owner has no permissions.", created.Id);
        }
        else
        {
            var ownerUser = await _userRepository.CreateAsync(new User
            {
                CompanyId = created.Id,
                Email = created.Email,
                FirstName = "Owner",
                LastName = string.Empty,
                UserType = "owner",
                Status = "active"
            });
            await _userRepository.AssignRolesAsync(ownerUser.Id, [ownerRole.Id]);
        }

        // Real signups only get a `subscription` row once an actual Stripe subscription
        // webhook fires (see SubscriptionService.HandleSubscriptionEventAsync) -- there's no
        // Stripe object here to generate one, so it's created directly. The fake
        // StripeSubscriptionId is shaped like a real one so GetSubscriptionStatus's Stripe
        // lookup fails with "resource_missing" (handled gracefully) instead of a malformed-id
        // error; either way the AddOns list it returns comes from a purely local query, so
        // the fake ID never has to resolve against real Stripe for the add-on check to work.
        var subscription = new Subscription
        {
            CompanyId = created.Id,
            StripeSubscriptionId = $"sub_test_{Guid.NewGuid():N}",
            StripeCustomerId = $"cus_test_{Guid.NewGuid():N}",
            StripePriceId = null,
            Status = "active",
            CurrentPeriodStart = now,
            CurrentPeriodEnd = now.AddDays(3650),
            CancelAtPeriodEnd = false,
            CreatedDate = now
        };
        var createdSubscription = await _subscriptionRepository.CreateAsync(subscription)
            ?? throw new InvalidOperationException("Failed to create subscription record for test company.");

        if (request.GrantLotScannerAddOn)
        {
            await _addOnRepository.CreateAsync(new SubscriptionAddOn
            {
                SubscriptionId = createdSubscription.Id,
                AddonType = LotScannerAddonType,
                StripeSubscriptionItemId = $"si_test_{Guid.NewGuid():N}",
                StripePriceId = string.Empty,
                Status = "active",
                CreatedDate = now
            });
        }

        _logger.LogInformation("Created test company {CompanyId} ({Email}), Lot Scanner add-on granted: {Granted}",
            created.Id, created.Email, request.GrantLotScannerAddOn);

        return new CreateTestCompanyResponse
        {
            CompanyId = created.Id,
            Email = created.Email,
            CompanyName = created.CompanyName ?? string.Empty,
            Status = created.Status
        };
    }
}
