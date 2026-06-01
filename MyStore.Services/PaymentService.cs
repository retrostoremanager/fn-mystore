using Microsoft.Extensions.Configuration;
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
        IConfiguration configuration,
        ILogger<PaymentService> logger)
    {
        _paymentRepository = paymentRepository;
        _companyRepository = companyRepository;
        _logger = logger;
        _stripeSecretKey = configuration["Stripe:SecretKey"];
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
                Brand = stripePaymentMethod.Card?.Brand ?? string.Empty,
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
                    Brand = created.Brand,
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
                StripeErrorMapper.ToUserFriendlyMessage(ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing payment method for company {CompanyId}", companyId);
            return ApiResponse<StorePaymentMethodResponse>.ErrorResponse(
                "An error occurred while storing the payment method. Please try again.");
        }
    }

    public async Task<ApiResponse<IEnumerable<StorePaymentMethodResponse>>> GetPaymentMethodsAsync(int companyId)
    {
        try
        {
            var methods = (await _paymentRepository.GetByCompanyIdAsync(companyId)).ToList();

            var methodsNeedingBackfill = methods
                .Where(m => string.IsNullOrEmpty(m.Brand) && !string.IsNullOrEmpty(m.StripePaymentMethodId))
                .ToList();

            if (methodsNeedingBackfill.Count > 0 && !string.IsNullOrWhiteSpace(_stripeSecretKey))
            {
                var paymentMethodService = new Stripe.PaymentMethodService(new StripeClient(_stripeSecretKey));

                foreach (var method in methodsNeedingBackfill)
                {
                    try
                    {
                        var stripePaymentMethod = await paymentMethodService.GetAsync(method.StripePaymentMethodId);
                        var brand = stripePaymentMethod.Card?.Brand;
                        if (!string.IsNullOrEmpty(brand))
                        {
                            method.Brand = brand;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not retrieve brand from Stripe for payment method {PaymentMethodId}; falling back to 'unknown'.", method.Id);
                    }
                }
            }

            var response = methods.Select(m => new StorePaymentMethodResponse
            {
                Id = m.Id,
                Brand = string.IsNullOrEmpty(m.Brand) ? "unknown" : m.Brand,
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

    public async Task<ApiResponse<StorePaymentMethodResponse>> SetDefaultPaymentMethodAsync(int companyId, int paymentMethodId)
    {
        if (string.IsNullOrWhiteSpace(_stripeSecretKey))
        {
            return ApiResponse<StorePaymentMethodResponse>.ErrorResponse(
                "Payment processing is not configured.");
        }

        try
        {
            var paymentMethod = await _paymentRepository.GetByIdAsync(paymentMethodId, companyId);
            if (paymentMethod == null)
            {
                return ApiResponse<StorePaymentMethodResponse>.ErrorResponse("Payment method not found.");
            }

            await _paymentRepository.SetDefaultAsync(companyId, paymentMethodId);

            var customerService = new Stripe.CustomerService();
            await customerService.UpdateAsync(paymentMethod.StripeCustomerId, new CustomerUpdateOptions
            {
                InvoiceSettings = new CustomerInvoiceSettingsOptions
                {
                    DefaultPaymentMethod = paymentMethod.StripePaymentMethodId
                }
            });

            _logger.LogInformation("Set default payment method {PaymentMethodId} for company {CompanyId}",
                paymentMethodId, companyId);

            var updated = await _paymentRepository.GetByIdAsync(paymentMethodId, companyId);
            return ApiResponse<StorePaymentMethodResponse>.SuccessResponse(
                new StorePaymentMethodResponse
                {
                    Id = updated!.Id,
                    Brand = updated.Brand,
                    Last4 = updated.Last4,
                    ExpirationMonth = updated.ExpirationMonth,
                    ExpirationYear = updated.ExpirationYear,
                    IsDefault = updated.IsDefault
                },
                "Default payment method updated successfully.");
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error setting default payment method for company {CompanyId}", companyId);
            return ApiResponse<StorePaymentMethodResponse>.ErrorResponse(
                StripeErrorMapper.ToUserFriendlyMessage(ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting default payment method for company {CompanyId}", companyId);
            return ApiResponse<StorePaymentMethodResponse>.ErrorResponse(
                "An error occurred while updating the default payment method. Please try again.");
        }
    }

    public async Task<ApiResponse<object>> DeletePaymentMethodAsync(int companyId, int paymentMethodId)
    {
        if (string.IsNullOrWhiteSpace(_stripeSecretKey))
        {
            return ApiResponse<object>.ErrorResponse(
                "Payment processing is not configured.");
        }

        try
        {
            var paymentMethod = await _paymentRepository.GetByIdAsync(paymentMethodId, companyId);
            if (paymentMethod == null)
            {
                return ApiResponse<object>.ErrorResponse("Payment method not found.");
            }

            var allMethods = await _paymentRepository.GetByCompanyIdAsync(companyId);
            if (allMethods.Count() <= 1)
            {
                return ApiResponse<object>.ErrorResponse(
                    "Cannot delete your last payment method. Add another payment method first.");
            }

            var deleted = await _paymentRepository.DeleteAsync(paymentMethodId, companyId);
            if (!deleted)
            {
                return ApiResponse<object>.ErrorResponse("Failed to delete payment method.");
            }

            var paymentMethodService = new Stripe.PaymentMethodService();
            await paymentMethodService.DetachAsync(paymentMethod.StripePaymentMethodId);

            if (paymentMethod.IsDefault)
            {
                var newDefault = allMethods.FirstOrDefault(m => m.Id != paymentMethodId);
                if (newDefault != null)
                {
                    await _paymentRepository.SetDefaultAsync(companyId, newDefault.Id);
                    var customerService = new Stripe.CustomerService();
                    await customerService.UpdateAsync(paymentMethod.StripeCustomerId, new CustomerUpdateOptions
                    {
                        InvoiceSettings = new CustomerInvoiceSettingsOptions
                        {
                            DefaultPaymentMethod = newDefault.StripePaymentMethodId
                        }
                    });
                }
            }

            _logger.LogInformation("Deleted payment method {PaymentMethodId} for company {CompanyId}",
                paymentMethodId, companyId);

            return ApiResponse<object>.SuccessResponse(new { }, "Payment method removed successfully.");
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error deleting payment method for company {CompanyId}", companyId);
            return ApiResponse<object>.ErrorResponse(
                StripeErrorMapper.ToUserFriendlyMessage(ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting payment method for company {CompanyId}", companyId);
            return ApiResponse<object>.ErrorResponse(
                "An error occurred while removing the payment method. Please try again.");
        }
    }
}
