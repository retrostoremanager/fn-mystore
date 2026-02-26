using Microsoft.Extensions.Logging;
using MyStore.Models;
using MyStore.Repositories;
using Stripe;

namespace MyStore.Services;

/// <summary>
/// Service for managing payment methods for subscription billing.
/// Uses Stripe for payment method storage; we only store IDs and display info.
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly ICompanyRepository _companyRepository;
    private readonly string? _stripeSecretKey;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IPaymentRepository paymentRepository,
        ICompanyRepository companyRepository,
        ILogger<PaymentService> logger)
    {
        _paymentRepository = paymentRepository;
        _companyRepository = companyRepository;
        _logger = logger;
        _stripeSecretKey = Environment.GetEnvironmentVariable("Stripe__SecretKey");
    }

    public async Task<ApiResponse<StorePaymentMethodResponse>> StorePaymentMethodAsync(
        int companyId,
        StorePaymentMethodRequest request)
    {
        if (string.IsNullOrWhiteSpace(_stripeSecretKey))
        {
            return ApiResponse<StorePaymentMethodResponse>.ErrorResponse(
                "Payment processing is not configured.");
        }

        if (string.IsNullOrWhiteSpace(request.PaymentMethodId))
        {
            return ApiResponse<StorePaymentMethodResponse>.ErrorResponse(
                "Payment method ID is required.",
                new List<string> { "PaymentMethodId" });
        }

        try
        {
            var company = await _companyRepository.GetByIdAsync(companyId);
            if (company == null)
            {
                return ApiResponse<StorePaymentMethodResponse>.ErrorResponse("Company not found.");
            }

            string stripeCustomerId;
            var existingMethods = await _paymentRepository.GetByCompanyIdAsync(companyId);
            var firstExisting = existingMethods.FirstOrDefault();
            if (firstExisting != null && !string.IsNullOrWhiteSpace(firstExisting.StripeCustomerId))
            {
                stripeCustomerId = firstExisting.StripeCustomerId;
            }
            else
            {
                var customerService = new Stripe.CustomerService();
                var customer = await customerService.CreateAsync(new CustomerCreateOptions
                {
                    Email = company.Email,
                    Metadata = new Dictionary<string, string> { { "company_id", companyId.ToString() } }
                });
                stripeCustomerId = customer.Id;
                _logger.LogInformation("Created Stripe customer {CustomerId} for company {CompanyId}", stripeCustomerId, companyId);
            }

            var paymentMethodService = new Stripe.PaymentMethodService();
            var stripePaymentMethod = await paymentMethodService.GetAsync(request.PaymentMethodId);
            if (stripePaymentMethod == null)
            {
                return ApiResponse<StorePaymentMethodResponse>.ErrorResponse("Invalid payment method.");
            }

            await paymentMethodService.AttachAsync(request.PaymentMethodId, new PaymentMethodAttachOptions
            {
                Customer = stripeCustomerId
            });

            var isFirst = !existingMethods.Any();

            var dbPaymentMethod = new Models.PaymentMethod
            {
                CompanyId = companyId,
                StripeCustomerId = stripeCustomerId,
                StripePaymentMethodId = request.PaymentMethodId,
                Last4 = stripePaymentMethod.Card?.Last4 ?? "****",
                ExpirationMonth = (int)(stripePaymentMethod.Card?.ExpMonth ?? 0),
                ExpirationYear = (int)(stripePaymentMethod.Card?.ExpYear ?? 0),
                IsDefault = isFirst,
                CreatedDate = DateTime.UtcNow
            };

            var created = await _paymentRepository.CreateAsync(dbPaymentMethod);
            if (created == null)
            {
                return ApiResponse<StorePaymentMethodResponse>.ErrorResponse("Failed to store payment method.");
            }

            return ApiResponse<StorePaymentMethodResponse>.SuccessResponse(
                new StorePaymentMethodResponse
                {
                    Id = created.Id,
                    Last4 = created.Last4,
                    ExpirationMonth = created.ExpirationMonth,
                    ExpirationYear = created.ExpirationYear,
                    IsDefault = created.IsDefault
                },
                "Payment method added successfully.");
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error storing payment method for company {CompanyId}", companyId);
            return ApiResponse<StorePaymentMethodResponse>.ErrorResponse(
                ex.Message ?? "Payment method could not be processed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing payment method for company {CompanyId}", companyId);
            return ApiResponse<StorePaymentMethodResponse>.ErrorResponse(
                "An error occurred while storing the payment method.");
        }
    }

    public async Task<ApiResponse<IEnumerable<StorePaymentMethodResponse>>> GetPaymentMethodsAsync(int companyId)
    {
        try
        {
            var methods = await _paymentRepository.GetByCompanyIdAsync(companyId);
            var response = methods.Select(m => new StorePaymentMethodResponse
            {
                Id = m.Id,
                Last4 = m.Last4,
                ExpirationMonth = m.ExpirationMonth,
                ExpirationYear = m.ExpirationYear,
                IsDefault = m.IsDefault
            });
            return ApiResponse<IEnumerable<StorePaymentMethodResponse>>.SuccessResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving payment methods for company {CompanyId}", companyId);
            return ApiResponse<IEnumerable<StorePaymentMethodResponse>>.ErrorResponse(
                "An error occurred while retrieving payment methods.");
        }
    }
}
