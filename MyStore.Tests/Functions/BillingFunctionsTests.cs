using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MyStore.Functions;
using MyStore.Services;
using MyStore.Tests.Helpers;
using Stripe;
using Xunit;

namespace MyStore.Tests.Functions;

public class BillingFunctionsTests
{
    private readonly Mock<IPaymentService> _paymentServiceMock;
    private readonly Mock<ISubscriptionService> _subscriptionServiceMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger<BillingFunctions>> _loggerMock;
    private readonly BillingFunctions _functions;

    public BillingFunctionsTests()
    {
        _paymentServiceMock = new Mock<IPaymentService>();
        _subscriptionServiceMock = new Mock<ISubscriptionService>();
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
}
