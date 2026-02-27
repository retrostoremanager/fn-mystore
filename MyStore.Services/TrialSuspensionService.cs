using Microsoft.Extensions.Logging;
using MyStore.Repositories;

namespace MyStore.Services;

/// <summary>
/// Suspends accounts when trial expires and no payment within 7 days (EPIC-0-006-005).
/// Data is retained for 30 days per business rules.
/// </summary>
public class TrialSuspensionService : ITrialSuspensionService
{
    private readonly ICompanyRepository _companyRepository;
    private readonly ILogger<TrialSuspensionService> _logger;

    public TrialSuspensionService(
        ICompanyRepository companyRepository,
        ILogger<TrialSuspensionService> logger)
    {
        _companyRepository = companyRepository;
        _logger = logger;
    }

    public async Task<int> SuspendExpiredTrialsAsync()
    {
        var candidates = (await _companyRepository.GetExpiredTrialsForSuspensionAsync()).ToList();
        _logger.LogInformation("Found {Count} companies with expired trial (7+ days) and no payment for suspension", candidates.Count);

        var suspended = 0;
        foreach (var company in candidates)
        {
            try
            {
                await _companyRepository.UpdateStatusAsync(company.Id, "Suspended");
                suspended++;
                _logger.LogInformation("Suspended company {CompanyId} ({Email}) - trial expired {Days} days ago",
                    company.Id, company.Email, (int)(DateTime.UtcNow - company.TrialEndDate).TotalDays);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error suspending company {CompanyId}", company.Id);
            }
        }

        return suspended;
    }
}
