using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyStore.Functions.Attributes;
using MyStore.Functions.Helpers;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Services;
using Newtonsoft.Json.Linq;
using Stripe;

namespace MyStore.Functions;

public class BillingFunctions
{
    private readonly IPaymentService _paymentService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ISubscriptionChangeService _subscriptionChangeService;
    private readonly ICompanyRepository _companyRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly StripeOptions _stripeOptions;
    private readonly InvoiceService _invoiceService;
    private readonly Stripe.SubscriptionService _stripeSubscriptionService;
    private readonly ILogger _logger;

    public BillingFunctions(
        IPaymentService paymentService,
        ISubscriptionService subscriptionService,
        ISubscriptionChangeService subscriptionChangeService,
        ICompanyRepository companyRepository,
        IPaymentRepository paymentRepository,
        ISubscriptionRepository subscriptionRepository,
        IOptions<StripeOptions> stripeOptions,
        InvoiceService invoiceService,
        Stripe.SubscriptionService stripeSubscriptionService,
        ILoggerFactory loggerFactory)
    {
        _paymentService = paymentService;
        _subscriptionService = subscriptionService;
        _subscriptionChangeService = subscriptionChangeService;
        _companyRepository = companyRepository;
        _paymentRepository = paymentRepository;
        _subscriptionRepository = subscriptionRepository;
        _stripeOptions = stripeOptions.Value;
        _invoiceService = invoiceService;
        _stripeSubscriptionService = stripeSubscriptionService;
        _logger = loggerFactory.CreateLogger<BillingFunctions>();
    }

    [Function("StripeWebhook")]
    public async Task<HttpResponseData> StripeWebhook(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "webhooks/stripe")] HttpRequestData req)
    {
        var webhookSecret = _stripeOptions.WebhookSecret ?? Environment.GetEnvironmentVariable("Stripe__WebhookSecret");
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            _logger.LogWarning("Stripe webhook secret not configured");
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync("{\"error\":\"Webhook not configured\"}");
            err.Headers.Add("Content-Type", "application/json");
            return err;
        }

        string payload;
        using (var reader = new StreamReader(req.Body, Encoding.UTF8))
        {
            payload = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrEmpty(payload))
        {
            var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
            await badReq.WriteStringAsync("{\"error\":\"Empty payload\"}");
            badReq.Headers.Add("Content-Type", "application/json");
            return badReq;
        }

        string? sigHeader = null;
        if (req.Headers.TryGetValues("Stripe-Signature", out var values))
            sigHeader = values.FirstOrDefault();

        if (string.IsNullOrEmpty(sigHeader))
        {
            _logger.LogWarning("Stripe webhook received without Stripe-Signature header");
            var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
            await badReq.WriteStringAsync("{\"error\":\"Missing Stripe-Signature\"}");
            badReq.Headers.Add("Content-Type", "application/json");
            return badReq;
        }

        Event? stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, sigHeader, webhookSecret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature verification failed");
            var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
            await badReq.WriteStringAsync($"{{\"error\":\"{ex.Message}\"}}");
            badReq.Headers.Add("Content-Type", "application/json");
            return badReq;
        }

        try
        {
            await _subscriptionService.ProcessStripeEventAsync(stripeEvent);
            var ok = req.CreateResponse(HttpStatusCode.OK);
            await ok.WriteStringAsync("{\"received\":true}");
            ok.Headers.Add("Content-Type", "application/json");
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Stripe webhook event {EventId}", stripeEvent.Id);
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync("{\"error\":\"Processing failed\"}");
            err.Headers.Add("Content-Type", "application/json");
            return err;
        }
    }

    [Function("StorePaymentMethod")]
    [RequirePermission("billing.manage")]
    public async Task<HttpResponseData> StorePaymentMethod(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "billing/payment-methods")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            StorePaymentMethodRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<StorePaymentMethodRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException)
            {
                var errorResponse = ApiResponse<StorePaymentMethodResponse>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            if (request == null)
            {
                var errorResponse = ApiResponse<StorePaymentMethodResponse>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var response = await _paymentService.StorePaymentMethodAsync(companyId, request);

            var statusCode = response.Success ? HttpStatusCode.Created : HttpStatusCode.BadRequest;
            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized payment method storage attempt");
            var errorResponse = ApiResponse<StorePaymentMethodResponse>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing payment method");
            var errorResponse = ApiResponse<StorePaymentMethodResponse>.ErrorResponse(
                "An error occurred while storing the payment method.");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    [Function("ChangeSubscriptionTier")]
    [RequirePermission("billing.manage")]
    public async Task<HttpResponseData> ChangeSubscriptionTier(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "billing/subscription/tier")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            ChangeSubscriptionTierRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<ChangeSubscriptionTierRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException)
            {
                var errorResponse = ApiResponse<SubscriptionChangeResult>.ErrorResponse("Invalid request body");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Tier))
            {
                var errorResponse = ApiResponse<SubscriptionChangeResult>.ErrorResponse("Tier is required");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var result = await _subscriptionChangeService.ChangeTierAsync(companyId, request.Tier, request.LocationCount);

            var statusCode = result.Success ? HttpStatusCode.OK : HttpStatusCode.BadRequest;
            var apiResponse = result.Success
                ? ApiResponse<SubscriptionChangeResult>.SuccessResponse(result, result.Message)
                : ApiResponse<SubscriptionChangeResult>.ErrorResponse(result.Message ?? "Failed to change tier");
            return await CreateHttpResponse(req, apiResponse, statusCode);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized subscription tier change attempt");
            var errorResponse = ApiResponse<SubscriptionChangeResult>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing subscription tier");
            var errorResponse = ApiResponse<SubscriptionChangeResult>.ErrorResponse(
                "An error occurred while changing the subscription tier.");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    [Function("GetTrialStatus")]
    [RequirePermission("billing.view")]
    public async Task<HttpResponseData> GetTrialStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "billing/trial-status")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);

            var company = await _companyRepository.GetByIdAsync(companyId);
            if (company == null)
            {
                var errorResponse = ApiResponse<TrialStatusResponse>.ErrorResponse("Company not found.");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.NotFound);
            }

            var paymentMethods = await _paymentRepository.GetByCompanyIdAsync(companyId);
            var hasPaymentMethod = paymentMethods.Any();

            var now = DateTime.UtcNow;
            // Trial status based on TrialEndDate only - user may have selected Basic/Premium at signup but is still in 30-day trial
            var isInTrial = company.TrialEndDate > now;
            var daysRemaining = isInTrial
                ? Math.Max(0, (int)(company.TrialEndDate - now).TotalDays)
                : 0;

            var isSuspended = string.Equals(company.Status, "Suspended", StringComparison.OrdinalIgnoreCase);
            var trialExpired = company.TrialEndDate <= now && !hasPaymentMethod;
            var accessRestricted = !isSuspended && trialExpired && !hasPaymentMethod;
            var accessSuspended = isSuspended;

            var response = ApiResponse<TrialStatusResponse>.SuccessResponse(new TrialStatusResponse
            {
                IsInTrial = isInTrial,
                TrialStartDate = company.TrialStartDate,
                TrialEndDate = company.TrialEndDate,
                DaysRemaining = daysRemaining,
                HasPaymentMethod = hasPaymentMethod,
                SubscriptionTier = company.SubscriptionTier ?? "Trial",
                AccessRestricted = accessRestricted,
                AccessSuspended = accessSuspended
            });

            return await CreateHttpResponse(req, response, HttpStatusCode.OK);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized trial status retrieval attempt");
            var errorResponse = ApiResponse<TrialStatusResponse>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving trial status");
            var errorResponse = ApiResponse<TrialStatusResponse>.ErrorResponse(
                "An error occurred while retrieving trial status.");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    [Function("GetPaymentMethods")]
    [RequirePermission("billing.view")]
    public async Task<HttpResponseData> GetPaymentMethods(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "billing/payment-methods")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);

            var response = await _paymentService.GetPaymentMethodsAsync(companyId);

            return await CreateHttpResponse(req, response, response.Success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized payment methods retrieval attempt");
            var errorResponse = ApiResponse<IEnumerable<StorePaymentMethodResponse>>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving payment methods");
            var errorResponse = ApiResponse<IEnumerable<StorePaymentMethodResponse>>.ErrorResponse(
                "An error occurred while retrieving payment methods.");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    [Function("SetDefaultPaymentMethod")]
    [RequirePermission("billing.manage")]
    public async Task<HttpResponseData> SetDefaultPaymentMethod(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "billing/payment-methods/{id}/default")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);

            var response = await _paymentService.SetDefaultPaymentMethodAsync(companyId, id);

            var statusCode = response.Success ? HttpStatusCode.OK : HttpStatusCode.BadRequest;
            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized set default payment method attempt");
            var errorResponse = ApiResponse<StorePaymentMethodResponse>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting default payment method");
            var errorResponse = ApiResponse<StorePaymentMethodResponse>.ErrorResponse(
                "An error occurred while updating the default payment method.");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    [Function("DeletePaymentMethod")]
    [RequirePermission("billing.manage")]
    public async Task<HttpResponseData> DeletePaymentMethod(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "billing/payment-methods/{id}")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);

            var response = await _paymentService.DeletePaymentMethodAsync(companyId, id);

            var statusCode = response.Success ? HttpStatusCode.OK : HttpStatusCode.BadRequest;
            return await CreateHttpResponse(req, response, statusCode);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized delete payment method attempt");
            var errorResponse = ApiResponse<object>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting payment method");
            var errorResponse = ApiResponse<object>.ErrorResponse(
                "An error occurred while removing the payment method.");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    [Function("GetInvoices")]
    [RequirePermission("billing.view")]
    public async Task<HttpResponseData> GetInvoices(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "billing/invoices")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);

            var limitParam = req.Url.Query
                .TrimStart('?')
                .Split('&')
                .Select(p => p.Split('='))
                .Where(p => p.Length == 2 && p[0].Equals("limit", StringComparison.OrdinalIgnoreCase))
                .Select(p => p[1])
                .FirstOrDefault();

            var limit = 10;
            if (!string.IsNullOrEmpty(limitParam) && int.TryParse(limitParam, out var parsedLimit))
                limit = Math.Clamp(parsedLimit, 1, 100);

            var paymentMethods = await _paymentRepository.GetByCompanyIdAsync(companyId);
            var stripeCustomerId = paymentMethods.FirstOrDefault()?.StripeCustomerId;

            if (string.IsNullOrEmpty(stripeCustomerId))
            {
                var emptyResponse = ApiResponse<List<InvoiceSummary>>.SuccessResponse(new List<InvoiceSummary>());
                return await CreateHttpResponse(req, emptyResponse, HttpStatusCode.OK);
            }

            var invoices = await _invoiceService.ListAsync(new InvoiceListOptions
            {
                Customer = stripeCustomerId,
                Limit = limit,
            });

            var summaries = invoices.Data.Select(inv => new InvoiceSummary
            {
                Id = inv.Id,
                Number = inv.Number,
                Amount = inv.Total,
                Currency = inv.Currency,
                Status = inv.Status,
                Created = inv.Created,
                PeriodStart = inv.PeriodStart,
                PeriodEnd = inv.PeriodEnd,
                HostedInvoiceUrl = inv.HostedInvoiceUrl,
                InvoicePdf = inv.InvoicePdf,
            }).ToList();

            var response = ApiResponse<List<InvoiceSummary>>.SuccessResponse(summaries);
            return await CreateHttpResponse(req, response, HttpStatusCode.OK);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized invoice retrieval attempt");
            var errorResponse = ApiResponse<List<InvoiceSummary>>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving invoices");
            var errorResponse = ApiResponse<List<InvoiceSummary>>.ErrorResponse(
                "An error occurred while retrieving invoices.");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    [Function("GetSubscriptionStatus")]
    [RequirePermission("billing.view")]
    public async Task<HttpResponseData> GetSubscriptionStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "billing/subscription")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);

            var company = await _companyRepository.GetByIdAsync(companyId);
            if (company == null)
            {
                var errorResponse = ApiResponse<SubscriptionStatusResponse>.ErrorResponse("Company not found.");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.NotFound);
            }

            var paymentMethods = await _paymentRepository.GetByCompanyIdAsync(companyId);
            var hasPaymentMethod = paymentMethods.Any();

            var now = DateTime.UtcNow;
            var isInTrial = company.TrialEndDate > now;
            var daysRemainingInTrial = isInTrial
                ? Math.Max(0, (int)(company.TrialEndDate - now).TotalDays)
                : 0;

            var isSuspended = string.Equals(company.Status, "Suspended", StringComparison.OrdinalIgnoreCase);

            var localSub = await _subscriptionRepository.GetByCompanyIdAsync(companyId);

            string status;
            string? billingCycle = null;
            DateTime? currentPeriodStart = null;
            DateTime? currentPeriodEnd = null;

            if (isSuspended)
            {
                status = "suspended";
            }
            else if (isInTrial && localSub == null)
            {
                status = "trial";
            }
            else if (localSub != null)
            {
                Stripe.Subscription? stripeSub = null;
                try
                {
                    stripeSub = await _stripeSubscriptionService.GetAsync(localSub.StripeSubscriptionId);
                }
                catch (StripeException ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch Stripe subscription {StripeSubscriptionId}", localSub.StripeSubscriptionId);
                }

                currentPeriodStart = localSub.CurrentPeriodStart;
                currentPeriodEnd = localSub.CurrentPeriodEnd;

                if (stripeSub is not null)
                {
                    status = stripeSub.Status switch
                    {
                        "active" or "trialing" => "active",
                        "past_due" => "past_due",
                        "canceled" => "canceled",
                        _ => stripeSub.Status
                    };

                    var firstItem = stripeSub.Items?.Data?.FirstOrDefault();
                    if (firstItem?.Plan is not null)
                    {
                        billingCycle = firstItem.Plan.Interval == "year" ? "annual" : "monthly";
                    }
                }
                else
                {
                    status = localSub.Status;
                }
            }
            else
            {
                var stripeCustomerId = paymentMethods.FirstOrDefault()?.StripeCustomerId;
                if (!string.IsNullOrEmpty(stripeCustomerId))
                {
                    Stripe.Subscription? stripeSub = null;
                    try
                    {
                        var stripeList = await _stripeSubscriptionService.ListAsync(new SubscriptionListOptions
                        {
                            Customer = stripeCustomerId,
                            Status = "active",
                            Limit = 1,
                        });
                        stripeSub = stripeList.Data?.FirstOrDefault();
                    }
                    catch (StripeException ex)
                    {
                        _logger.LogWarning(ex, "Failed to fetch Stripe subscriptions for customer {StripeCustomerId}", stripeCustomerId);
                    }

                    if (stripeSub is not null)
                    {
                        status = "active";
                        if (stripeSub.RawJObject?["current_period_start"] is not null)
                            currentPeriodStart = DateTimeOffset.FromUnixTimeSeconds(stripeSub.RawJObject["current_period_start"]!.Value<long>()).UtcDateTime;
                        if (stripeSub.RawJObject?["current_period_end"] is not null)
                            currentPeriodEnd = DateTimeOffset.FromUnixTimeSeconds(stripeSub.RawJObject["current_period_end"]!.Value<long>()).UtcDateTime;
                        var firstItem = stripeSub.Items?.Data?.FirstOrDefault();
                        if (firstItem?.Plan is not null)
                        {
                            billingCycle = firstItem.Plan.Interval == "year" ? "annual" : "monthly";
                        }
                    }
                    else
                    {
                        status = "active";
                    }
                }
                else
                {
                    status = "active";
                }
            }

            var responseData = new SubscriptionStatusResponse
            {
                Tier = company.SubscriptionTier ?? "Trial",
                Status = status,
                IsInTrial = isInTrial,
                TrialEndDate = company.TrialEndDate,
                DaysRemainingInTrial = daysRemainingInTrial,
                BillingCycle = billingCycle,
                CurrentPeriodStart = currentPeriodStart,
                CurrentPeriodEnd = currentPeriodEnd,
                HasPaymentMethod = hasPaymentMethod,
            };

            var response = ApiResponse<SubscriptionStatusResponse>.SuccessResponse(responseData);
            return await CreateHttpResponse(req, response, HttpStatusCode.OK);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized subscription status retrieval attempt");
            var errorResponse = ApiResponse<SubscriptionStatusResponse>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving subscription status");
            var errorResponse = ApiResponse<SubscriptionStatusResponse>.ErrorResponse(
                "An error occurred while retrieving subscription status.");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    private static async Task<HttpResponseData> CreateHttpResponse<T>(HttpRequestData req, ApiResponse<T> apiResponse, HttpStatusCode statusCode)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
        return response;
    }
}
