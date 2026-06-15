using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using MyStore.Services;

namespace MyStore.Functions;

/// <summary>
/// Timer-triggered function for trial-to-paid conversion (EPIC-0-006-004).
/// When trial expires and payment method is on file, creates Stripe subscription and updates company tier.
/// </summary>
public class TrialConversionFunctions
{
    private readonly ITrialConversionService _trialConversionService;
    private readonly ILogger _logger;

    public TrialConversionFunctions(
        ITrialConversionService trialConversionService,
        ILoggerFactory loggerFactory)
    {
        _trialConversionService = trialConversionService;
        _logger = loggerFactory.CreateLogger<TrialConversionFunctions>();
    }

    /// <summary>
    /// Runs daily at 9:30 AM UTC (after trial expiration notifications at 9:00).
    /// Converts expired trials with payment methods to paid subscriptions.
    /// </summary>
    [Function("TrialToPaidConversion")]
    public async Task Run(
        [TimerTrigger("0 30 9 * * *")] TimerInfo timerInfo)
    {
        _logger.LogInformation("Trial-to-paid conversion job started at {Time}", DateTime.UtcNow);

        var converted = await _trialConversionService.ProcessExpiredTrialsAsync();

        _logger.LogInformation("Trial-to-paid conversion job completed. Converted {Count} companies.", converted);
    }
}
