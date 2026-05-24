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
        data.GetArrayLength().Should().Be(0);
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
        data.GetArrayLength().Should().Be(1);
        var inv = data[0];
        inv.GetProperty("id").GetString().Should().Be("in_test1");
        inv.GetProperty("number").GetString().Should().Be("INV-0001");
        inv.GetProperty("amount").GetInt64().Should().Be(2999);
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
        parsed.GetProperty("data").GetArrayLength().Should().Be(0);
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
        _companyRepositoryMock.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetSubscriptionStatus_InTrial_NoStripeSubscription_ReturnsTrialStatus()
    {
        var companyId = 1;
        var trialEnd = DateTime.UtcNow.AddDays(10);
        var company = new Company
        {
            Id = companyId,
            Status = "Active",
            TrialStartDate = trialEnd.AddDays(-20),
            TrialEndDate = trialEnd,
            SubscriptionTier = "Trial"
        };
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        _companyRepositoryMock.Setup(r => r.GetByIdAsync(companyId)).ReturnsAsync(company);
        _paymentRepositoryMock.Setup(p => p.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(new List<Models.PaymentMethod> { new Models.PaymentMethod { Id = 1, CompanyId = companyId } });
        _subscriptionRepositoryMock.Setup(r => r.GetByCompanyIdAsync(companyId))
            .ReturnsAsync((MyStore.Models.Subscription?)null);

        var result = await _functions.GetSubscriptionStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("status").GetString().Should().Be("trial");
        data.GetProperty("isInTrial").GetBoolean().Should().BeTrue();
        data.GetProperty("daysRemainingInTrial").GetInt32().Should().BeInRange(9, 11);
        data.GetProperty("hasPaymentMethod").GetBoolean().Should().BeTrue();
        data.GetProperty("tier").GetString().Should().Be("Trial");
        _stripeSubscriptionServiceMock.Verify(
            s => s.GetAsync(It.IsAny<string>(), It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetSubscriptionStatus_ActiveStripeSubscription_ReturnsActiveStatus()
    {
        var companyId = 2;
        var trialEnd = DateTime.UtcNow.AddDays(-5);
        var company = new Company
        {
            Id = companyId,
            Status = "Active",
            TrialStartDate = trialEnd.AddDays(-30),
            TrialEndDate = trialEnd,
            SubscriptionTier = "Basic"
        };
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        var localSub = new MyStore.Models.Subscription
        {
            Id = 1,
            CompanyId = companyId,
            StripeSubscriptionId = "sub_active123",
            Status = "active"
        };

        var stripeSub = new Stripe.Subscription
        {
            Id = "sub_active123",
            Status = "active",
            Items = new StripeList<SubscriptionItem>
            {
                Data = new List<SubscriptionItem>
                {
                    new SubscriptionItem
                    {
                        Plan = new Plan { Interval = "month" }
                    }
                }
            }
        };

        _companyRepositoryMock.Setup(r => r.GetByIdAsync(companyId)).ReturnsAsync(company);
        _paymentRepositoryMock.Setup(p => p.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(new List<Models.PaymentMethod> { new Models.PaymentMethod { Id = 1, CompanyId = companyId } });
        _subscriptionRepositoryMock.Setup(r => r.GetByCompanyIdAsync(companyId)).ReturnsAsync(localSub);
        _stripeSubscriptionServiceMock
            .Setup(s => s.GetAsync("sub_active123", It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeSub);

        var result = await _functions.GetSubscriptionStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("status").GetString().Should().Be("active");
        data.GetProperty("isInTrial").GetBoolean().Should().BeFalse();
        data.GetProperty("billingCycle").GetString().Should().Be("monthly");
        data.GetProperty("tier").GetString().Should().Be("Basic");
    }

    [Fact]
    public async Task GetSubscriptionStatus_Suspended_ReturnsSuspendedStatus()
    {
        var companyId = 3;
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
        _subscriptionRepositoryMock.Setup(r => r.GetByCompanyIdAsync(companyId))
            .ReturnsAsync((MyStore.Models.Subscription?)null);

        var result = await _functions.GetSubscriptionStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("status").GetString().Should().Be("suspended");
        data.GetProperty("isInTrial").GetBoolean().Should().BeFalse();
        _stripeSubscriptionServiceMock.Verify(
            s => s.GetAsync(It.IsAny<string>(), It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetSubscriptionStatus_ActivePaidNoLocalSub_FetchesStripeByCustomerAndReturnsBillingData()
    {
        var companyId = 13;
        var trialEnd = DateTime.UtcNow.AddDays(-60);
        var company = new Company
        {
            Id = companyId,
            Status = "Active",
            TrialStartDate = trialEnd.AddDays(-30),
            TrialEndDate = trialEnd,
            SubscriptionTier = "Enterprise"
        };
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        var periodStart = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodStartUnix = new DateTimeOffset(periodStart).ToUnixTimeSeconds();
        var periodEndUnix = new DateTimeOffset(periodEnd).ToUnixTimeSeconds();

        var subJson = $$"""
            {
                "id": "sub_enterprise123",
                "object": "subscription",
                "status": "active",
                "current_period_start": {{periodStartUnix}},
                "current_period_end": {{periodEndUnix}},
                "cancel_at_period_end": false,
                "customer": "cus_enterprise123",
                "items": {
                    "object": "list",
                    "data": [{
                        "id": "si_test",
                        "object": "subscription_item",
                        "current_period_start": {{periodStartUnix}},
                        "current_period_end": {{periodEndUnix}},
                        "plan": {
                            "id": "plan_test",
                            "object": "plan",
                            "interval": "month"
                        },
                        "price": {
                            "id": "price_test",
                            "object": "price"
                        }
                    }]
                }
            }
            """;
        var stripeSub = Newtonsoft.Json.JsonConvert.DeserializeObject<Stripe.Subscription>(subJson)!;

        var stripeList = new StripeList<Stripe.Subscription>
        {
            Data = new List<Stripe.Subscription> { stripeSub }
        };

        _companyRepositoryMock.Setup(r => r.GetByIdAsync(companyId)).ReturnsAsync(company);
        _paymentRepositoryMock.Setup(p => p.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(new List<Models.PaymentMethod>
            {
                new Models.PaymentMethod { Id = 1, CompanyId = companyId, StripeCustomerId = "cus_enterprise123" }
            });
        _subscriptionRepositoryMock.Setup(r => r.GetByCompanyIdAsync(companyId))
            .ReturnsAsync((MyStore.Models.Subscription?)null);
        _stripeSubscriptionServiceMock
            .Setup(s => s.ListAsync(It.Is<SubscriptionListOptions>(o => o.Customer == "cus_enterprise123"), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeList);

        var result = await _functions.GetSubscriptionStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("status").GetString().Should().Be("active");
        data.GetProperty("isInTrial").GetBoolean().Should().BeFalse();
        data.GetProperty("billingCycle").GetString().Should().Be("monthly");
        data.GetProperty("currentPeriodStart").GetDateTime().Should().Be(periodStart);
        data.GetProperty("currentPeriodEnd").GetDateTime().Should().Be(periodEnd);
        data.GetProperty("hasPaymentMethod").GetBoolean().Should().BeTrue();
        data.GetProperty("tier").GetString().Should().Be("Enterprise");
    }

    [Fact]
    public async Task GetSubscriptionStatus_ActivePaidNoLocalSubNoStripeMatch_ReturnsActiveWithNullBillingFields()
    {
        var companyId = 14;
        var trialEnd = DateTime.UtcNow.AddDays(-60);
        var company = new Company
        {
            Id = companyId,
            Status = "Active",
            TrialStartDate = trialEnd.AddDays(-30),
            TrialEndDate = trialEnd,
            SubscriptionTier = "Enterprise"
        };
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        var emptyStripeList = new StripeList<Stripe.Subscription>
        {
            Data = new List<Stripe.Subscription>()
        };

        _companyRepositoryMock.Setup(r => r.GetByIdAsync(companyId)).ReturnsAsync(company);
        _paymentRepositoryMock.Setup(p => p.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(new List<Models.PaymentMethod>
            {
                new Models.PaymentMethod { Id = 1, CompanyId = companyId, StripeCustomerId = "cus_enterprise456" }
            });
        _subscriptionRepositoryMock.Setup(r => r.GetByCompanyIdAsync(companyId))
            .ReturnsAsync((MyStore.Models.Subscription?)null);
        _stripeSubscriptionServiceMock
            .Setup(s => s.ListAsync(It.IsAny<SubscriptionListOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyStripeList);

        var result = await _functions.GetSubscriptionStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("status").GetString().Should().Be("active");
        data.GetProperty("isInTrial").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetSubscriptionStatus_ActivePaidNoLocalSubNoPaymentMethod_ReturnsActiveWithNullBillingFields()
    {
        var companyId = 15;
        var trialEnd = DateTime.UtcNow.AddDays(-60);
        var company = new Company
        {
            Id = companyId,
            Status = "Active",
            TrialStartDate = trialEnd.AddDays(-30),
            TrialEndDate = trialEnd,
            SubscriptionTier = "Enterprise"
        };
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        _companyRepositoryMock.Setup(r => r.GetByIdAsync(companyId)).ReturnsAsync(company);
        _paymentRepositoryMock.Setup(p => p.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(Array.Empty<Models.PaymentMethod>());
        _subscriptionRepositoryMock.Setup(r => r.GetByCompanyIdAsync(companyId))
            .ReturnsAsync((MyStore.Models.Subscription?)null);

        var result = await _functions.GetSubscriptionStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("status").GetString().Should().Be("active");
        data.GetProperty("isInTrial").GetBoolean().Should().BeFalse();
        _stripeSubscriptionServiceMock.Verify(
            s => s.ListAsync(It.IsAny<SubscriptionListOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetSubscriptionStatus_PastDueStripeSubscription_ReturnsPastDueStatus()
    {
        var companyId = 4;
        var trialEnd = DateTime.UtcNow.AddDays(-5);
        var company = new Company
        {
            Id = companyId,
            Status = "Active",
            TrialStartDate = trialEnd.AddDays(-30),
            TrialEndDate = trialEnd,
            SubscriptionTier = "Basic"
        };
        var headers = new Dictionary<string, string> { { "X-Company-Id", companyId.ToString() } };
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("", headers);

        var localSub = new MyStore.Models.Subscription
        {
            Id = 1,
            CompanyId = companyId,
            StripeSubscriptionId = "sub_pastdue123",
            Status = "past_due"
        };

        var stripeSub = new Stripe.Subscription
        {
            Id = "sub_pastdue123",
            Status = "past_due",
            Items = new StripeList<SubscriptionItem>
            {
                Data = new List<SubscriptionItem>
                {
                    new SubscriptionItem
                    {
                        Plan = new Plan { Interval = "month" }
                    }
                }
            }
        };

        _companyRepositoryMock.Setup(r => r.GetByIdAsync(companyId)).ReturnsAsync(company);
        _paymentRepositoryMock.Setup(p => p.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(Array.Empty<Models.PaymentMethod>());
        _subscriptionRepositoryMock.Setup(r => r.GetByCompanyIdAsync(companyId)).ReturnsAsync(localSub);
        _stripeSubscriptionServiceMock
            .Setup(s => s.GetAsync("sub_pastdue123", It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeSub);

        var result = await _functions.GetSubscriptionStatus(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHelpers.ReadResponseBody(result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = parsed.GetProperty("data");
        data.GetProperty("status").GetString().Should().Be("past_due");
    }
}
