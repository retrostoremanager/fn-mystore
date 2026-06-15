using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using MyStore.Services;

namespace MyStore.Functions;

/// <summary>
/// Timer-triggered function for trial suspension (EPIC-0-006-005).
/// When trial expires and no payment within 7 days, suspends the account.
/// Runs daily at 10:00 AM UTC (after trial conversion at 9:30).
/// </summary>
public class TrialSuspensionFunctions
{
    private readonly ITrialSuspensionService _trialSuspensionService;
    private readonly ILogger _logger;

    public TrialSuspensionFunctions(
        ITrialSuspensionService trialSuspensionService,
        ILoggerFactory loggerFactory)
    {
        _trialSuspensionService = trialSuspensionService;
        _logger = loggerFactory.CreateLogger<TrialSuspensionFunctions>();
    }

    /// <summary>
    /// Runs daily at 10:00 AM UTC.
    /// Suspends companies with trial expired 7+ days ago and no payment method.
    /// </summary>
    [Function("TrialSuspension")]
    public async Task Run(
        [TimerTrigger("0 0 10 * * *")] TimerInfo timerInfo)
    {
        _logger.LogInformation("Trial suspension job started at {Time}", DateTime.UtcNow);

        var suspended = await _trialSuspensionService.SuspendExpiredTrialsAsync();

        _logger.LogInformation("Trial suspension job completed. Suspended {Count} companies.", suspended);
    }
}
