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
            stripeOptions,
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
            stripeOptions,
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
        var body = await TestHelpers.ReadResponseBody(result);
        body.Should().Contain("Company ID");
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
}
