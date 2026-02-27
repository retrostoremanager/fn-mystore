using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyStore.Functions.Helpers;
using MyStore.Models;
using MyStore.Services;
using Stripe;

namespace MyStore.Functions;

public class BillingFunctions
{
    private readonly IPaymentService _paymentService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly StripeOptions _stripeOptions;
    private readonly ILogger _logger;

    public BillingFunctions(
        IPaymentService paymentService,
        ISubscriptionService subscriptionService,
        IOptions<StripeOptions> stripeOptions,
        ILoggerFactory loggerFactory)
    {
        _paymentService = paymentService;
        _subscriptionService = subscriptionService;
        _stripeOptions = stripeOptions.Value;
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

    [Function("GetPaymentMethods")]
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
