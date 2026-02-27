using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyStore.Functions.Middleware;
using MyStore.Repositories;

namespace MyStore.Functions.Middleware;

/// <summary>
/// Middleware that restricts API access when trial has expired without payment or account is suspended (EPIC-0-006-005).
/// Billing endpoints (trial-status, payment-methods) remain accessible when restricted so users can add payment.
/// </summary>
public class CompanyAccessMiddleware : IFunctionsWorkerMiddleware
{
    private static readonly HashSet<string> BillingExemptFunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "GetTrialStatus",
        "GetPaymentMethods",
        "StorePaymentMethod",
        "SetDefaultPaymentMethod",
        "DeletePaymentMethod"
    };

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CompanyAccessMiddleware> _logger;

    public CompanyAccessMiddleware(IServiceProvider serviceProvider, ILogger<CompanyAccessMiddleware> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var feature = context.Features.Get<JwtPrincipalFeature>();
        if (feature?.CompanyId == null)
        {
            await next(context);
            return;
        }

        var companyId = feature.CompanyId.Value;
        var functionName = context.FunctionDefinition.Name;

        if (BillingExemptFunctionNames.Contains(functionName))
        {
            await next(context);
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var companyRepository = scope.ServiceProvider.GetRequiredService<ICompanyRepository>();
        var paymentRepository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();

        var company = await companyRepository.GetByIdAsync(companyId);
        if (company == null)
        {
            await next(context);
            return;
        }

        var isSuspended = string.Equals(company.Status, "Suspended", StringComparison.OrdinalIgnoreCase);
        if (isSuspended)
        {
            _logger.LogWarning("Blocked API access for suspended company {CompanyId}", companyId);
            await ReturnForbidden(context, "Account is suspended. Please contact support.");
            return;
        }

        var paymentMethods = await paymentRepository.GetByCompanyIdAsync(companyId);
        var hasPaymentMethod = paymentMethods.Any();
        var trialExpired = company.TrialEndDate <= DateTime.UtcNow
            && string.Equals(company.SubscriptionTier, "Trial", StringComparison.OrdinalIgnoreCase);

        if (trialExpired && !hasPaymentMethod)
        {
            _logger.LogWarning("Blocked API access for company {CompanyId} - trial expired, no payment method", companyId);
            await ReturnForbidden(context, "Trial has expired. Please add a payment method to continue.");
            return;
        }

        await next(context);
    }

    private static async Task ReturnForbidden(FunctionContext context, string message)
    {
        var httpRequest = await context.GetHttpRequestDataAsync();
        if (httpRequest == null)
        {
            context.GetInvocationResult().Value = null;
            return;
        }

        var response = httpRequest.CreateResponse(HttpStatusCode.Forbidden);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");

        var body = JsonSerializer.Serialize(new { error = message, code = "ACCESS_RESTRICTED" });
        await response.WriteStringAsync(body);

        context.GetInvocationResult().Value = response;
    }
}
