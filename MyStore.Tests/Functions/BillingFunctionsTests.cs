using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MyStore.Functions;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Services;
using MyStore.Tests.Helpers;
using Stripe;
using Xunit;

namespace MyStore.Tests.Functions;

public class BillingFunctionsTests
{
    private readonly Mock<IPaymentService> _paymentServiceMock;
    private readonly Mock<ISubscriptionService> _subscriptionServiceMock;
    private readonly Mock<ISubscriptionChangeService> _subscriptionChangeServiceMock;
    private readonly Mock<ICompanyRepository> _companyRepositoryMock;
    private readonly Mock<IPaymentRepository> _paymentRepositoryMock;
    private readonly Mock<ISubscriptionRepository> _subscriptionRepositoryMock;
    private readonly Mock<InvoiceService> _invoiceServiceMock;
    private readonly Mock<Stripe.SubscriptionService> _stripeSubscriptionServiceMock;
    private readonly Mock<Stripe.ProductService> _stripeProductServiceMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger<BillingFunctions>> _loggerMock;
    private readonly BillingFunctions _functions;

    public BillingFunctionsTests()
    {
        _paymentServiceMock = new Mock<IPaymentService>();
        _subscriptionServiceMock = new Mock<ISubscriptionService>();
        _subscriptionChangeServiceMock = new Mock<ISubscriptionChangeService>();
        _companyRepositoryMock = new Mock<ICompanyRepository>();
        _paymentRepositoryMock = new Mock<IPaymentRepository>();
        _subscriptionRepositoryMock = new Mock<ISubscriptionRepository>();
        _invoiceServiceMock = new Mock<InvoiceService>();
        _stripeSubscriptionServiceMock = new Mock<Stripe.SubscriptionService>();
        _stripeProductServiceMock = new Mock<Stripe.ProductService>();
        _loggerMock = new Mock<ILogger<BillingFunctions>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        var stripeOptions = Options.Create(new StripeOptions
        {
            WebhookSecret = "whsec_test"
        });
        _functions = new BillingFunctions(
            _paymentServiceMock.Object,
            _subscriptionServiceMock.Object,
            _subscriptionChangeServiceMock.Object,
            _companyRepositoryMock.Object,
            _paymentRepositoryMock.Object,
            _subscriptionRepositoryMock.Object,
            stripeOptions,
            _invoiceServiceMock.Object,
            _stripeSubscriptionServiceMock.Object,
            _stripeProductServiceMock.Object,
            _loggerFactoryMock.Object);
    }

    [Fact]
    public async Task StripeWebhook_EmptyPayload_Returns400BadRequest()
    {
        // Arrange
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", new Dictionary<string, string>());

        // Act
        var result = await _functions.StripeWebhook(request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await TestHelpers.ReadResponseBody(result);
        body.Should().Contain("Empty payload");
        _subscriptionServiceMock.Verify(s => s.ProcessStripeEventAsync(It.IsAny<Stripe.Event>()), Times.Never);
    }

    [Fact]
    public async Task StripeWebhook_MissingStripeSignature_Returns400BadRequest()
    {
        // Arrange
        var payload = """{"id":"evt_1","object":"event","type":"customer.subscription.created"}""";
        var request = TestHelpers.CreateHttpRequestDataWithRawBody(payload);

        // Act
        var result = await _functions.StripeWebhook(request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await TestHelpers.ReadResponseBody(result);
        body.Should().Contain("Missing Stripe-Signature");
        _subscriptionServiceMock.Verify(s => s.ProcessStripeEventAsync(It.IsAny<Stripe.Event>()), Times.Never);
    }

    [Fact]
    public async Task StripeWebhook_InvalidSignature_Returns400BadRequest()
    {
        // Arrange
        var payload = """{"id":"evt_1","object":"event","type":"customer.subscription.created"}""";
        var headers = new Dictionary<string, string> { { "Stripe-Signature", "t=123,v1=invalid" } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody(payload, headers);

        // Act
        var result = await _functions.StripeWebhook(request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await TestHelpers.ReadResponseBody(result);
        body.Should().Contain("error");
        _subscriptionServiceMock.Verify(s => s.ProcessStripeEventAsync(It.IsAny<Stripe.Event>()), Times.Never);
    }

    [Fact]
    public async Task StripeWebhook_WebhookSecretNotConfigured_Returns500()
    {
        // Arrange - create functions with no webhook secret
        var stripeOptions = Options.Create(new StripeOptions { WebhookSecret = "" });
        var functionsNoSecret = new BillingFunctions(
            _paymentServiceMock.Object,
            _subscriptionServiceMock.Object,
            _subscriptionChangeServiceMock.Object,
            _companyRepositoryMock.Object,
            _paymentRepositoryMock.Object,
            _subscriptionRepositoryMock.Object,
            stripeOptions,
            _invoiceServiceMock.Object,
            _stripeSubscriptionServiceMock.Object,
            _stripeProductServiceMock.Object,
            _loggerFactoryMock.Object);
        var payload = """{"id":"evt_1","object":"event","type":"test"}""";
        var request = TestHelpers.CreateHttpRequestDataWithRawBody(payload);

        // Act
        var result = await functionsNoSecret.StripeWebhook(request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await TestHelpers.ReadResponseBody(result);
        body.Should().Contain("Webhook not configured");
        _subscriptionServiceMock.Verify(s => s.ProcessStripeEventAsync(It.IsAny<Event>()), Times.Never);
    }

    [Fact]
    public async Task StripeWebhook_ValidEvent_ProcessesAndReturns200()
    {
        // Arrange
        var subPayload = StripeTestHelpers.CreateSubscriptionObjectPayload(
            subscriptionId: "sub_valid",
            customerId: "cus_company1",
            status: "active");
        var fullPayload = StripeTestHelpers.WrapInEventPayload(subPayload, "evt_valid", "customer.subscription.created");
        var (payload, sigHeader) = StripeTestHelpers.CreateSignedPayloadAndHeader(fullPayload, "whsec_test");
        var headers = new Dictionary<string, string> { { "Stripe-Signature", sigHeader } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody(payload, headers);

        _subscriptionServiceMock
            .Setup(s => s.ProcessStripeEventAsync(It.IsAny<Stripe.Event>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _functions.StripeWebhook(request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        body.Should().Contain("received");
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("received").GetBoolean().Should().BeTrue();
        _subscriptionServiceMock.Verify(s => s.ProcessStripeEventAsync(It.IsAny<Stripe.Event>()), Times.Once);
    }

    [Fact]
    public async Task GetTrialStatus_MissingCompanyId_Returns401Unauthorized()
    {
        // Arrange - no X-Company-Id header
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("");

        // Act
        var result = await _functions.GetTrialStatus(request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _companyRepositoryMock.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetTrialStatus_CompanyNotFound_Returns404()
    {
        // Arrange
        var headers = new Dictionary<string, string> { { "X-Company-Id", "999" } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);
        _companyRepositoryMock.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((Company?)null);

        // Act
        var result = await _functions.GetTrialStatus(request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await TestHelpers.ReadResponseBody(result);
        body.Should().Contain("Company not found");
    }

    [Fact]
    public async Task GetTrialStatus_CompanyInTrialWithPaymentMethod_Returns200WithCorrectData()
    {
        // Arrange
        var companyId = 1;
        var trialEnd = DateTime.UtcNow.AddDays(15);
        var trialStart = trialEnd.AddDays(-15);
        var company = new Company
        {
            Id = companyId,
            TrialStartDate = trialStart,
            TrialEndDate = trialEnd,
            SubscriptionTier = "Trial"
        };
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        _companyRepositoryMock.Setup(r => r.GetByIdAsync(companyId)).ReturnsAsync(company);
        _paymentRepositoryMock.Setup(p => p.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(new List<Models.PaymentMethod> { new Models.PaymentMethod { Id = 1, CompanyId = companyId } });

        // Act
        var result = await _functions.GetTrialStatus(request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("isInTrial").GetBoolean().Should().BeTrue();
        data.GetProperty("hasPaymentMethod").GetBoolean().Should().BeTrue();
        data.GetProperty("daysRemaining").GetInt32().Should().BeInRange(14, 16);
        data.GetProperty("subscriptionTier").GetString().Should().Be("Trial");
    }

    [Fact]
    public async Task GetTrialStatus_CompanyInTrialWithoutPaymentMethod_Returns200WithHasPaymentMethodFalse()
    {
        // Arrange
        var companyId = 1;
        var trialEnd = DateTime.UtcNow.AddDays(5);
        var company = new Company
        {
            Id = companyId,
            TrialStartDate = trialEnd.AddDays(-30),
            TrialEndDate = trialEnd,
            SubscriptionTier = "Trial"
        };
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        _companyRepositoryMock.Setup(r => r.GetByIdAsync(companyId)).ReturnsAsync(company);
        _paymentRepositoryMock.Setup(p => p.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(Array.Empty<Models.PaymentMethod>());

        // Act
        var result = await _functions.GetTrialStatus(request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("isInTrial").GetBoolean().Should().BeTrue();
        data.GetProperty("hasPaymentMethod").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetTrialStatus_TrialExpired_Returns200WithIsInTrialFalse()
    {
        // Arrange - trial ended yesterday
        var companyId = 1;
        var trialEnd = DateTime.UtcNow.AddDays(-1);
        var company = new Company
        {
            Id = companyId,
            TrialStartDate = trialEnd.AddDays(-30),
            TrialEndDate = trialEnd,
            SubscriptionTier = "Trial"
        };
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        _companyRepositoryMock.Setup(r => r.GetByIdAsync(companyId)).ReturnsAsync(company);
        _paymentRepositoryMock.Setup(p => p.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(Array.Empty<Models.PaymentMethod>());

        // Act
        var result = await _functions.GetTrialStatus(request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("isInTrial").GetBoolean().Should().BeFalse();
        data.GetProperty("daysRemaining").GetInt32().Should().Be(0);
        data.GetProperty("accessRestricted").GetBoolean().Should().BeTrue();
        data.GetProperty("accessSuspended").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetTrialStatus_Suspended_ReturnsAccessSuspendedTrue()
    {
        var companyId = 1;
        var company = new Company
        {
            Id = companyId,
            Status = "Suspended",
            TrialStartDate = DateTime.UtcNow.AddDays(-40),
            TrialEndDate = DateTime.UtcNow.AddDays(-10),
            SubscriptionTier = "Trial"
        };
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        _companyRepositoryMock.Setup(r => r.GetByIdAsync(companyId)).ReturnsAsync(company);
        _paymentRepositoryMock.Setup(p => p.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(Array.Empty<Models.PaymentMethod>());

        var result = await _functions.GetTrialStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("accessRestricted").GetBoolean().Should().BeFalse();
        data.GetProperty("accessSuspended").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetPaymentMethods_Returns200WithBrandField()
    {
        var companyId = 1;
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        var methods = new List<StorePaymentMethodResponse>
        {
            new StorePaymentMethodResponse
            {
                Id = 1,
                Brand = "visa",
                Last4 = "4242",
                ExpirationMonth = 12,
                ExpirationYear = 2035,
                IsDefault = true
            }
        };

        _paymentServiceMock
            .Setup(s => s.GetPaymentMethodsAsync(companyId))
            .ReturnsAsync(ApiResponse<IEnumerable<StorePaymentMethodResponse>>.SuccessResponse(methods));

        var result = await _functions.GetPaymentMethods(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetArrayLength().Should().Be(1);
        var method = data[0];
        method.GetProperty("brand").GetString().Should().Be("visa");
        method.GetProperty("last4").GetString().Should().Be("4242");
        method.GetProperty("expirationMonth").GetInt32().Should().Be(12);
        method.GetProperty("expirationYear").GetInt32().Should().Be(2035);
        method.GetProperty("isDefault").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetInvoices_NoStripeCustomerId_Returns200WithEmptyList()
    {
        var companyId = 1;
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        _paymentRepositoryMock.Setup(p => p.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(Array.Empty<Models.PaymentMethod>());

        var result = await _functions.GetInvoices(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("invoices").GetArrayLength().Should().Be(0);
        data.GetProperty("hasMore").GetBoolean().Should().BeFalse();
        _invoiceServiceMock.Verify(s => s.ListAsync(It.IsAny<InvoiceListOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetInvoices_WithStripeCustomerId_Returns200WithInvoices()
    {
        var companyId = 1;
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);
        var stripeCustomerId = "cus_test123";

        _paymentRepositoryMock.Setup(p => p.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(new List<Models.PaymentMethod>
            {
                new Models.PaymentMethod { Id = 1, CompanyId = companyId, StripeCustomerId = stripeCustomerId }
            });

        var created = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var stripeInvoice = new Invoice
        {
            Id = "in_test1",
            Number = "INV-0001",
            Total = 2999,
            Currency = "usd",
            Status = "paid",
            Created = created,
            PeriodStart = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd = new DateTime(2025, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            HostedInvoiceUrl = "https://invoice.stripe.com/i/test",
            InvoicePdf = "https://pay.stripe.com/invoice/test/pdf",
        };

        var stripeList = new StripeList<Invoice>
        {
            Data = new List<Invoice> { stripeInvoice }
        };

        _invoiceServiceMock
            .Setup(s => s.ListAsync(It.IsAny<InvoiceListOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeList);

        var result = await _functions.GetInvoices(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("hasMore").GetBoolean().Should().BeFalse();
        var invoices = data.GetProperty("invoices");
        invoices.GetArrayLength().Should().Be(1);
        var inv = invoices[0];
        inv.GetProperty("id").GetString().Should().Be("in_test1");
        inv.GetProperty("number").GetString().Should().Be("INV-0001");
        inv.GetProperty("amount").GetDecimal().Should().Be(29.99m);
        inv.GetProperty("currency").GetString().Should().Be("usd");
        inv.GetProperty("status").GetString().Should().Be("paid");
    }

    [Fact]
    public async Task GetInvoices_StripeException_Returns200WithEmptyList()
    {
        var companyId = 1;
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);
        var stripeCustomerId = "cus_test123";

        _paymentRepositoryMock.Setup(p => p.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(new List<Models.PaymentMethod>
            {
                new Models.PaymentMethod { Id = 1, CompanyId = companyId, StripeCustomerId = stripeCustomerId }
            });

        _invoiceServiceMock
            .Setup(s => s.ListAsync(It.IsAny<InvoiceListOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException("No such customer"));

        var result = await _functions.GetInvoices(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("invoices").GetArrayLength().Should().Be(0);
        data.GetProperty("hasMore").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetInvoices_PaginationParams_ForwardedToStripe()
    {
        var companyId = 1;
        var stripeCustomerId = "cus_test123";
        var startingAfter = "in_prev";
        var request = TestHelpers.CreateHttpRequestDataWithRawBody(
            "",
            new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } },
            $"?limit=5&startingAfter={startingAfter}");

        _paymentRepositoryMock.Setup(p => p.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(new List<Models.PaymentMethod>
            {
                new Models.PaymentMethod { Id = 1, CompanyId = companyId, StripeCustomerId = stripeCustomerId }
            });

        var stripeList = new StripeList<Invoice>
        {
            Data = new List<Invoice>(),
            HasMore = true,
        };

        InvoiceListOptions? capturedOptions = null;
        _invoiceServiceMock
            .Setup(s => s.ListAsync(It.IsAny<InvoiceListOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<InvoiceListOptions, RequestOptions, CancellationToken>((opts, _, _) => capturedOptions = opts)
            .ReturnsAsync(stripeList);

        var result = await _functions.GetInvoices(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Limit.Should().Be(5);
        capturedOptions.StartingAfter.Should().Be(startingAfter);
        capturedOptions.Customer.Should().Be(stripeCustomerId);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("data").GetProperty("hasMore").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetInvoices_MissingCompanyId_Returns401()
    {
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("");

        var result = await _functions.GetInvoices(request);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _invoiceServiceMock.Verify(s => s.ListAsync(It.IsAny<InvoiceListOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSubscriptionStatus_MissingCompanyId_Returns401()
    {
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("");

        var result = await _functions.GetSubscriptionStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _subscriptionRepositoryMock.Verify(r => r.GetByCompanyIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetSubscriptionStatus_NoLocalSubscription_Returns404()
    {
        var companyId = 1;
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        _subscriptionRepositoryMock.Setup(r => r.GetByCompanyIdAsync(companyId))
            .ReturnsAsync((MyStore.Models.Subscription?)null);

        var result = await _functions.GetSubscriptionStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await TestHelpers.ReadResponseBody(result);
        body.Should().Contain("No subscription found");
    }

    [Fact]
    public async Task GetSubscriptionStatus_ActiveSubscription_ReturnsPlanNameStatusPeriodsAndNextInvoice()
    {
        var companyId = 2;
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        var periodStart = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodStartUnix = new DateTimeOffset(periodStart).ToUnixTimeSeconds();
        var periodEndUnix = new DateTimeOffset(periodEnd).ToUnixTimeSeconds();

        var localSub = new MyStore.Models.Subscription
        {
            Id = 1,
            CompanyId = companyId,
            StripeSubscriptionId = "sub_active123",
            StripeCustomerId = "cus_active123",
            Status = "active",
        };

        var subJson = $$"""
            {
                "id": "sub_active123",
                "object": "subscription",
                "status": "active",
                "current_period_start": {{periodStartUnix}},
                "current_period_end": {{periodEndUnix}},
                "cancel_at_period_end": false,
                "customer": "cus_active123",
                "items": {
                    "object": "list",
                    "data": [{
                        "id": "si_test",
                        "object": "subscription_item",
                        "price": {
                            "id": "price_test",
                            "object": "price",
                            "product": {
                                "id": "prod_test",
                                "object": "product",
                                "name": "Basic Plan"
                            }
                        }
                    }]
                }
            }
            """;
        var stripeSub = Newtonsoft.Json.JsonConvert.DeserializeObject<Stripe.Subscription>(subJson)!;

        var upcomingInvoice = new Invoice { Total = 2999, Currency = "usd" };

        _subscriptionRepositoryMock.Setup(r => r.GetByCompanyIdAsync(companyId)).ReturnsAsync(localSub);
        _stripeSubscriptionServiceMock
            .Setup(s => s.GetAsync("sub_active123", It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeSub);
        _invoiceServiceMock
            .Setup(s => s.CreatePreviewAsync(It.IsAny<InvoiceCreatePreviewOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(upcomingInvoice);

        var result = await _functions.GetSubscriptionStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("status").GetString().Should().Be("active");
        data.GetProperty("planName").GetString().Should().Be("Basic Plan");
        data.GetProperty("currentPeriodStart").GetDateTime().Should().Be(periodStart);
        data.GetProperty("currentPeriodEnd").GetDateTime().Should().Be(periodEnd);
        data.GetProperty("trialStart").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("trialEnd").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("nextInvoiceAmount").GetDecimal().Should().Be(29.99m);
        data.GetProperty("currency").GetString().Should().Be("usd");
    }

    [Fact]
    public async Task GetSubscriptionStatus_TrialingSubscription_ReturnsTrialStartAndEnd()
    {
        var companyId = 3;
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        var trialStart = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var trialEnd = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodStart = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var trialStartUnix = new DateTimeOffset(trialStart).ToUnixTimeSeconds();
        var trialEndUnix = new DateTimeOffset(trialEnd).ToUnixTimeSeconds();
        var periodStartUnix = new DateTimeOffset(periodStart).ToUnixTimeSeconds();
        var periodEndUnix = new DateTimeOffset(periodEnd).ToUnixTimeSeconds();

        var localSub = new MyStore.Models.Subscription
        {
            Id = 2,
            CompanyId = companyId,
            StripeSubscriptionId = "sub_trialing123",
            StripeCustomerId = "cus_trialing123",
            Status = "trialing",
        };

        var subJson = $$"""
            {
                "id": "sub_trialing123",
                "object": "subscription",
                "status": "trialing",
                "current_period_start": {{periodStartUnix}},
                "current_period_end": {{periodEndUnix}},
                "trial_start": {{trialStartUnix}},
                "trial_end": {{trialEndUnix}},
                "cancel_at_period_end": false,
                "customer": "cus_trialing123",
                "items": {
                    "object": "list",
                    "data": [{
                        "id": "si_test",
                        "object": "subscription_item",
                        "price": {
                            "id": "price_test",
                            "object": "price",
                            "product": {
                                "id": "prod_test",
                                "object": "product",
                                "name": "Premium Plan"
                            }
                        }
                    }]
                }
            }
            """;
        var stripeSub = Newtonsoft.Json.JsonConvert.DeserializeObject<Stripe.Subscription>(subJson)!;

        _subscriptionRepositoryMock.Setup(r => r.GetByCompanyIdAsync(companyId)).ReturnsAsync(localSub);
        _stripeSubscriptionServiceMock
            .Setup(s => s.GetAsync("sub_trialing123", It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeSub);
        _invoiceServiceMock
            .Setup(s => s.CreatePreviewAsync(It.IsAny<InvoiceCreatePreviewOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException("No upcoming invoice") { StripeError = new StripeError { Code = "invoice_upcoming_none" } });

        var result = await _functions.GetSubscriptionStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("status").GetString().Should().Be("trialing");
        data.GetProperty("planName").GetString().Should().Be("Premium Plan");
        data.GetProperty("trialStart").GetDateTime().Should().Be(trialStart);
        data.GetProperty("trialEnd").GetDateTime().Should().Be(trialEnd);
        data.GetProperty("nextInvoiceAmount").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("currency").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetSubscriptionStatus_StripeGetAsyncFails_ReturnsLocalSubscriptionData()
    {
        var companyId = 5;
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        var localSub = new MyStore.Models.Subscription
        {
            Id = 5,
            CompanyId = companyId,
            StripeSubscriptionId = "sub_missing123",
            StripeCustomerId = "cus_missing123",
            Status = "active",
        };

        _subscriptionRepositoryMock.Setup(r => r.GetByCompanyIdAsync(companyId)).ReturnsAsync(localSub);
        _stripeSubscriptionServiceMock
            .Setup(s => s.GetAsync("sub_missing123", It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException("No such subscription") { StripeError = new StripeError { Code = "resource_missing" } });
        _invoiceServiceMock
            .Setup(s => s.CreatePreviewAsync(It.IsAny<InvoiceCreatePreviewOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException("No such customer") { StripeError = new StripeError { Code = "invoice_upcoming_none" } });

        var result = await _functions.GetSubscriptionStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        body.Should().Contain("active");
    }

    [Fact]
    public async Task GetSubscriptionStatus_InvoicePreviewStripeExceptionNotUpcomingNone_ReturnsOkWithSubscriptionData()
    {
        var companyId = 6;
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        var periodStart = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodStartUnix = new DateTimeOffset(periodStart).ToUnixTimeSeconds();
        var periodEndUnix = new DateTimeOffset(periodEnd).ToUnixTimeSeconds();

        var localSub = new MyStore.Models.Subscription
        {
            Id = 6,
            CompanyId = companyId,
            StripeSubscriptionId = "sub_noinvoice123",
            StripeCustomerId = "",
            Status = "active",
        };

        var subJson = $$"""
            {
                "id": "sub_noinvoice123",
                "object": "subscription",
                "status": "active",
                "current_period_start": {{periodStartUnix}},
                "current_period_end": {{periodEndUnix}},
                "cancel_at_period_end": false,
                "customer": "cus_noinvoice123",
                "items": {
                    "object": "list",
                    "data": [{
                        "id": "si_test",
                        "object": "subscription_item",
                        "price": {
                            "id": "price_test",
                            "object": "price",
                            "product": {
                                "id": "prod_test",
                                "object": "product",
                                "name": "Basic Plan"
                            }
                        }
                    }]
                }
            }
            """;
        var stripeSub = Newtonsoft.Json.JsonConvert.DeserializeObject<Stripe.Subscription>(subJson)!;

        _subscriptionRepositoryMock.Setup(r => r.GetByCompanyIdAsync(companyId)).ReturnsAsync(localSub);
        _stripeSubscriptionServiceMock
            .Setup(s => s.GetAsync("sub_noinvoice123", It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeSub);
        _invoiceServiceMock
            .Setup(s => s.CreatePreviewAsync(It.IsAny<InvoiceCreatePreviewOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException("No such customer") { StripeError = new StripeError { Code = "resource_missing" } });

        var result = await _functions.GetSubscriptionStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        body.Should().Contain("active");
    }

    [Fact]
    public async Task GetSubscriptionStatus_CanceledSubscription_ReturnsCanceledStatusAndNullNextInvoice()
    {
        var companyId = 4;
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        var periodStart = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodStartUnix = new DateTimeOffset(periodStart).ToUnixTimeSeconds();
        var periodEndUnix = new DateTimeOffset(periodEnd).ToUnixTimeSeconds();

        var localSub = new MyStore.Models.Subscription
        {
            Id = 3,
            CompanyId = companyId,
            StripeSubscriptionId = "sub_canceled123",
            StripeCustomerId = "cus_canceled123",
            Status = "canceled",
        };

        var subJson = $$"""
            {
                "id": "sub_canceled123",
                "object": "subscription",
                "status": "canceled",
                "current_period_start": {{periodStartUnix}},
                "current_period_end": {{periodEndUnix}},
                "cancel_at_period_end": false,
                "customer": "cus_canceled123",
                "items": {
                    "object": "list",
                    "data": [{
                        "id": "si_test",
                        "object": "subscription_item",
                        "price": {
                            "id": "price_test",
                            "object": "price",
                            "product": {
                                "id": "prod_test",
                                "object": "product",
                                "name": "Basic Plan"
                            }
                        }
                    }]
                }
            }
            """;
        var stripeSub = Newtonsoft.Json.JsonConvert.DeserializeObject<Stripe.Subscription>(subJson)!;

        _subscriptionRepositoryMock.Setup(r => r.GetByCompanyIdAsync(companyId)).ReturnsAsync(localSub);
        _stripeSubscriptionServiceMock
            .Setup(s => s.GetAsync("sub_canceled123", It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeSub);
        _invoiceServiceMock
            .Setup(s => s.CreatePreviewAsync(It.IsAny<InvoiceCreatePreviewOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException("No upcoming invoice") { StripeError = new StripeError { Code = "invoice_upcoming_none" } });

        var result = await _functions.GetSubscriptionStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("status").GetString().Should().Be("canceled");
        data.GetProperty("nextInvoiceAmount").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("trialStart").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("trialEnd").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetSubscriptionStatus_ProductIsStringId_FetchesProductAndReturnsPlanName()
    {
        var companyId = 13;
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        var periodStart = new DateTime(2026, 5, 20, 0, 14, 30, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 6, 20, 0, 14, 30, DateTimeKind.Utc);
        var periodStartUnix = new DateTimeOffset(periodStart).ToUnixTimeSeconds();
        var periodEndUnix = new DateTimeOffset(periodEnd).ToUnixTimeSeconds();

        var localSub = new MyStore.Models.Subscription
        {
            Id = 13,
            CompanyId = companyId,
            StripeSubscriptionId = "sub_stringprod123",
            StripeCustomerId = "",
            Status = "active",
        };

        var subJson = $$"""
            {
                "id": "sub_stringprod123",
                "object": "subscription",
                "status": "active",
                "current_period_start": {{periodStartUnix}},
                "current_period_end": {{periodEndUnix}},
                "cancel_at_period_end": false,
                "customer": "cus_stringprod123",
                "items": {
                    "object": "list",
                    "data": [{
                        "id": "si_stringprod",
                        "object": "subscription_item",
                        "price": {
                            "id": "price_stringprod",
                            "object": "price",
                            "product": "prod_stringprod123"
                        }
                    }]
                }
            }
            """;
        var stripeSub = Newtonsoft.Json.JsonConvert.DeserializeObject<Stripe.Subscription>(subJson)!;

        var stripeProduct = new Stripe.Product { Id = "prod_stringprod123", Name = "Enterprise" };
        var upcomingInvoice = new Invoice { Total = 19999, Currency = "usd" };

        InvoiceCreatePreviewOptions? capturedOptions = null;
        _subscriptionRepositoryMock.Setup(r => r.GetByCompanyIdAsync(companyId)).ReturnsAsync(localSub);
        _stripeSubscriptionServiceMock
            .Setup(s => s.GetAsync("sub_stringprod123", It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeSub);
        _stripeProductServiceMock
            .Setup(s => s.GetAsync("prod_stringprod123", It.IsAny<ProductGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeProduct);
        _invoiceServiceMock
            .Setup(s => s.CreatePreviewAsync(It.IsAny<InvoiceCreatePreviewOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<InvoiceCreatePreviewOptions, RequestOptions, CancellationToken>((opts, _, _) => capturedOptions = opts)
            .ReturnsAsync(upcomingInvoice);

        var result = await _functions.GetSubscriptionStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Customer.Should().Be("cus_stringprod123");
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("planName").GetString().Should().Be("Enterprise");
        data.GetProperty("nextInvoiceAmount").GetDecimal().Should().Be(199.99m);
        data.GetProperty("currency").GetString().Should().Be("usd");
        data.GetProperty("status").GetString().Should().Be("active");
    }

    [Fact]
    public async Task GetSubscriptionStatus_ProductNotExpandedBySDK_ExtractsPlanNameFromRawJson()
    {
        var companyId = 7;
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        var periodStart = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodStartUnix = new DateTimeOffset(periodStart).ToUnixTimeSeconds();
        var periodEndUnix = new DateTimeOffset(periodEnd).ToUnixTimeSeconds();

        var localSub = new MyStore.Models.Subscription
        {
            Id = 7,
            CompanyId = companyId,
            StripeSubscriptionId = "sub_rawjson123",
            StripeCustomerId = "cus_rawjson123",
            Status = "active",
        };

        var subJson = $$"""
            {
                "id": "sub_rawjson123",
                "object": "subscription",
                "status": "active",
                "current_period_start": {{periodStartUnix}},
                "current_period_end": {{periodEndUnix}},
                "cancel_at_period_end": false,
                "customer": "cus_rawjson123",
                "items": {
                    "object": "list",
                    "data": [{
                        "id": "si_rawjson",
                        "object": "subscription_item",
                        "price": {
                            "id": "price_rawjson",
                            "object": "price",
                            "product": {
                                "id": "prod_rawjson",
                                "object": "product",
                                "name": "Enterprise"
                            }
                        }
                    }]
                }
            }
            """;
        var stripeSub = Newtonsoft.Json.JsonConvert.DeserializeObject<Stripe.Subscription>(subJson)!;

        var upcomingInvoice = new Invoice { Total = 19999, Currency = "usd" };

        _subscriptionRepositoryMock.Setup(r => r.GetByCompanyIdAsync(companyId)).ReturnsAsync(localSub);
        _stripeSubscriptionServiceMock
            .Setup(s => s.GetAsync("sub_rawjson123", It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeSub);
        _invoiceServiceMock
            .Setup(s => s.CreatePreviewAsync(It.IsAny<InvoiceCreatePreviewOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(upcomingInvoice);

        var result = await _functions.GetSubscriptionStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("planName").GetString().Should().Be("Enterprise");
        data.GetProperty("nextInvoiceAmount").GetDecimal().Should().Be(199.99m);
        data.GetProperty("currency").GetString().Should().Be("usd");
    }

    [Fact]
    public async Task GetSubscriptionStatus_ExpandedFetchFails_RetriesWithoutExpansionAndReturnsPlanNameAndInvoice()
    {
        var companyId = 9;
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        var periodStart = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodStartUnix = new DateTimeOffset(periodStart).ToUnixTimeSeconds();
        var periodEndUnix = new DateTimeOffset(periodEnd).ToUnixTimeSeconds();

        var localSub = new MyStore.Models.Subscription
        {
            Id = 9,
            CompanyId = companyId,
            StripeSubscriptionId = "sub_expandfail123",
            StripeCustomerId = "",
            Status = "active",
        };

        var subJson = $$"""
            {
                "id": "sub_expandfail123",
                "object": "subscription",
                "status": "active",
                "current_period_start": {{periodStartUnix}},
                "current_period_end": {{periodEndUnix}},
                "cancel_at_period_end": false,
                "customer": "cus_expandfail123",
                "items": {
                    "object": "list",
                    "data": [{
                        "id": "si_expandfail",
                        "object": "subscription_item",
                        "price": {
                            "id": "price_expandfail",
                            "object": "price",
                            "product": {
                                "id": "prod_expandfail",
                                "object": "product",
                                "name": "Enterprise"
                            }
                        }
                    }]
                }
            }
            """;
        var stripeSub = Newtonsoft.Json.JsonConvert.DeserializeObject<Stripe.Subscription>(subJson)!;

        var upcomingInvoice = new Invoice { Total = 19999, Currency = "usd" };

        InvoiceCreatePreviewOptions? capturedOptions = null;
        _subscriptionRepositoryMock.Setup(r => r.GetByCompanyIdAsync(companyId)).ReturnsAsync(localSub);
        _stripeSubscriptionServiceMock
            .Setup(s => s.GetAsync("sub_expandfail123", It.Is<SubscriptionGetOptions>(o => o.Expand != null && o.Expand.Count > 0), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException("expansion failed"));
        _stripeSubscriptionServiceMock
            .Setup(s => s.GetAsync("sub_expandfail123", It.Is<SubscriptionGetOptions>(o => o == null || o.Expand == null || o.Expand.Count == 0), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeSub);
        _invoiceServiceMock
            .Setup(s => s.CreatePreviewAsync(It.IsAny<InvoiceCreatePreviewOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<InvoiceCreatePreviewOptions, RequestOptions, CancellationToken>((opts, _, _) => capturedOptions = opts)
            .ReturnsAsync(upcomingInvoice);

        var result = await _functions.GetSubscriptionStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Customer.Should().Be("cus_expandfail123");
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("planName").GetString().Should().Be("Enterprise");
        data.GetProperty("nextInvoiceAmount").GetDecimal().Should().Be(199.99m);
        data.GetProperty("currency").GetString().Should().Be("usd");
        data.GetProperty("status").GetString().Should().Be("active");
    }

    [Fact]
    public async Task GetSubscriptionStatus_EmptyLocalStripeCustomerId_UsesCustomerIdFromStripeSub()
    {
        var companyId = 8;
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        var periodStart = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodStartUnix = new DateTimeOffset(periodStart).ToUnixTimeSeconds();
        var periodEndUnix = new DateTimeOffset(periodEnd).ToUnixTimeSeconds();

        var localSub = new MyStore.Models.Subscription
        {
            Id = 8,
            CompanyId = companyId,
            StripeSubscriptionId = "sub_nocustomer123",
            StripeCustomerId = "",
            Status = "active",
        };

        var subJson = $$"""
            {
                "id": "sub_nocustomer123",
                "object": "subscription",
                "status": "active",
                "current_period_start": {{periodStartUnix}},
                "current_period_end": {{periodEndUnix}},
                "cancel_at_period_end": false,
                "customer": "cus_fromstripe123",
                "items": {
                    "object": "list",
                    "data": [{
                        "id": "si_nocustomer",
                        "object": "subscription_item",
                        "price": {
                            "id": "price_nocustomer",
                            "object": "price",
                            "product": {
                                "id": "prod_nocustomer",
                                "object": "product",
                                "name": "Basic Plan"
                            }
                        }
                    }]
                }
            }
            """;
        var stripeSub = Newtonsoft.Json.JsonConvert.DeserializeObject<Stripe.Subscription>(subJson)!;

        var upcomingInvoice = new Invoice { Total = 2999, Currency = "usd" };

        InvoiceCreatePreviewOptions? capturedOptions = null;
        _subscriptionRepositoryMock.Setup(r => r.GetByCompanyIdAsync(companyId)).ReturnsAsync(localSub);
        _stripeSubscriptionServiceMock
            .Setup(s => s.GetAsync("sub_nocustomer123", It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeSub);
        _invoiceServiceMock
            .Setup(s => s.CreatePreviewAsync(It.IsAny<InvoiceCreatePreviewOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<InvoiceCreatePreviewOptions, RequestOptions, CancellationToken>((opts, _, _) => capturedOptions = opts)
            .ReturnsAsync(upcomingInvoice);

        var result = await _functions.GetSubscriptionStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Customer.Should().Be("cus_fromstripe123");
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("nextInvoiceAmount").GetDecimal().Should().Be(29.99m);
        data.GetProperty("currency").GetString().Should().Be("usd");
    }
}
