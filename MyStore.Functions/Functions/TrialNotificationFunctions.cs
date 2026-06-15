using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using MyStore.Repositories;
using MyStore.Services;

namespace MyStore.Functions;

/// <summary>
/// Timer-triggered functions for trial expiration notifications (EPIC-0-006-003).
/// Sends email reminders at 7, 3, and 1 days before trial ends.
/// </summary>
public class TrialNotificationFunctions
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmailService _emailService;
    private readonly ILogger _logger;

    public TrialNotificationFunctions(
        ICompanyRepository companyRepository,
        IEmailService emailService,
        ILoggerFactory loggerFactory)
    {
        _companyRepository = companyRepository;
        _emailService = emailService;
        _logger = loggerFactory.CreateLogger<TrialNotificationFunctions>();
    }

    /// <summary>
    /// Runs daily at 9:00 AM UTC. Sends trial expiration reminder emails to companies
    /// whose trials expire in 7, 3, or 1 days and haven't received that notification yet.
    /// </summary>
    [Function("TrialExpirationNotifications")]
    public async Task Run(
        [TimerTrigger("0 0 9 * * *")] TimerInfo timerInfo)
    {
        _logger.LogInformation("Trial expiration notification job started at {Time}", DateTime.UtcNow);

        foreach (var daysRemaining in new[] { 7, 3, 1 })
        {
            await ProcessExpiringTrialsAsync(daysRemaining);
        }

        _logger.LogInformation("Trial expiration notification job completed");
    }

    private async Task ProcessExpiringTrialsAsync(int daysRemaining)
    {
        var companies = (await _companyRepository.GetExpiringTrialsAsync(daysRemaining)).ToList();
        _logger.LogInformation("Found {Count} companies with trial expiring in {Days} days", companies.Count, daysRemaining);

        foreach (var company in companies)
        {
            try
            {
                var result = await _emailService.SendTrialExpirationEmailAsync(company.Email, daysRemaining);
                if (result.Success)
                {
                    await _companyRepository.MarkTrialNotificationSentAsync(company.Id, daysRemaining);
                    _logger.LogInformation("Sent {Days}d trial expiration email to company {CompanyId} ({Email})",
                        daysRemaining, company.Id, company.Email);
                }
                else
                {
                    _logger.LogWarning("Failed to send trial expiration email to company {CompanyId}: {Error}",
                        company.Id, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending trial expiration email to company {CompanyId} ({Email})",
                    company.Id, company.Email);
            }
        }
    }
}
