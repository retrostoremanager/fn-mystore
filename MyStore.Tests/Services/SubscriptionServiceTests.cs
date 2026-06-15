using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Services;
using MyStore.Tests.Helpers;
using Stripe;
using Xunit;

namespace MyStore.Tests.Services;

public class SubscriptionServiceTests
{
    private readonly Mock<ISubscriptionRepository> _subscriptionRepoMock;
    private readonly Mock<IPaymentRepository> _paymentRepoMock;
    private readonly Mock<ILogger<MyStore.Services.SubscriptionService>> _loggerMock;
    private readonly MyStore.Services.SubscriptionService _service;

    public SubscriptionServiceTests()
    {
        _subscriptionRepoMock = new Mock<ISubscriptionRepository>();
        _paymentRepoMock = new Mock<IPaymentRepository>();
        _loggerMock = new Mock<ILogger<MyStore.Services.SubscriptionService>>();
        _service = new MyStore.Services.SubscriptionService(_subscriptionRepoMock.Object, _paymentRepoMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task ProcessStripeEventAsync_SubscriptionCreated_ResolvesCompany_CreatesSubscription()
    {
        // Arrange
        var subPayload = StripeTestHelpers.CreateSubscriptionObjectPayload(
            subscriptionId: "sub_new123",
            customerId: "cus_company1",
            status: "active");
        var fullPayload = StripeTestHelpers.WrapInEventPayload(subPayload, "evt_1", "customer.subscription.created");
        var stripeEvent = StripeTestHelpers.CreateSignedEvent(fullPayload);

        _paymentRepoMock
            .Setup(p => p.GetCompanyIdByStripeCustomerIdAsync("cus_company1"))
            .ReturnsAsync(42);
        _subscriptionRepoMock
            .Setup(s => s.GetByStripeSubscriptionIdAsync("sub_new123"))
            .ReturnsAsync((Models.Subscription?)null);
        Models.Subscription? captured = null;
        _subscriptionRepoMock
            .Setup(s => s.CreateAsync(It.IsAny<Models.Subscription>()))
            .Callback<Models.Subscription>(c => captured = c)
            .ReturnsAsync((Models.Subscription c) => { c.Id = 1; return c; });

        // Act
        await _service.ProcessStripeEventAsync(stripeEvent);

        // Assert
        captured.Should().NotBeNull();
        captured!.CompanyId.Should().Be(42);
        captured.StripeSubscriptionId.Should().Be("sub_new123");
        captured.StripeCustomerId.Should().Be("cus_company1");
        captured.Status.Should().Be("active");
        captured.CurrentPeriodStart.Should().NotBeNull();
        captured.CurrentPeriodEnd.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessStripeEventAsync_SubscriptionCreated_CompanyNotFound_DoesNotCreate()
    {
        // Arrange
        var subPayload = StripeTestHelpers.CreateSubscriptionObjectPayload(
            subscriptionId: "sub_orphan",
            customerId: "cus_unknown");
        var fullPayload = StripeTestHelpers.WrapInEventPayload(subPayload, "evt_2", "customer.subscription.created");
        var stripeEvent = StripeTestHelpers.CreateSignedEvent(fullPayload);

        _paymentRepoMock
            .Setup(p => p.GetCompanyIdByStripeCustomerIdAsync("cus_unknown"))
            .ReturnsAsync((int?)null);

        // Act
        await _service.ProcessStripeEventAsync(stripeEvent);

        // Assert
        _subscriptionRepoMock.Verify(s => s.CreateAsync(It.IsAny<Models.Subscription>()), Times.Never);
    }

    [Fact]
    public async Task ProcessStripeEventAsync_SubscriptionUpdated_UpdatesExisting()
    {
        // Arrange
        var existing = new Models.Subscription
        {
            Id = 5,
            CompanyId = 42,
            StripeSubscriptionId = "sub_existing",
            Status = "active",
            CreatedDate = DateTime.UtcNow.AddDays(-30)
        };
        var subPayload = StripeTestHelpers.CreateSubscriptionObjectPayload(
            subscriptionId: "sub_existing",
            customerId: "cus_company1",
            status: "past_due");
        var fullPayload = StripeTestHelpers.WrapInEventPayload(subPayload, "evt_3", "customer.subscription.updated");
        var stripeEvent = StripeTestHelpers.CreateSignedEvent(fullPayload);

        _paymentRepoMock
            .Setup(p => p.GetCompanyIdByStripeCustomerIdAsync("cus_company1"))
            .ReturnsAsync(42);
        _subscriptionRepoMock
            .Setup(s => s.GetByStripeSubscriptionIdAsync("sub_existing"))
            .ReturnsAsync(existing);
        Models.Subscription? captured = null;
        _subscriptionRepoMock
            .Setup(s => s.UpdateAsync(It.IsAny<Models.Subscription>()))
            .Callback<Models.Subscription>(c => captured = c)
            .ReturnsAsync((Models.Subscription c) => c);

        // Act
        await _service.ProcessStripeEventAsync(stripeEvent);

        // Assert
        captured.Should().NotBeNull();
        captured!.Status.Should().Be("past_due");
        captured.Id.Should().Be(5);
    }

    [Fact]
    public async Task ProcessStripeEventAsync_InvoicePaymentFailed_UpdatesSubscriptionToPastDue()
    {
        // Arrange
        var existing = new Models.Subscription
        {
            Id = 5,
            CompanyId = 42,
            StripeSubscriptionId = "sub_billing",
            Status = "active"
        };
        var invoicePayload = StripeTestHelpers.CreateInvoiceObjectPayload(
            invoiceId: "in_failed",
            subscriptionId: "sub_billing");
        var fullPayload = StripeTestHelpers.WrapInEventPayload(invoicePayload, "evt_4", "invoice.payment_failed");
        var stripeEvent = StripeTestHelpers.CreateSignedEvent(fullPayload);

        _subscriptionRepoMock
            .Setup(s => s.GetByStripeSubscriptionIdAsync("sub_billing"))
            .ReturnsAsync(existing);
        _subscriptionRepoMock
            .Setup(s => s.UpdateAsync(It.IsAny<Models.Subscription>()))
            .ReturnsAsync((Models.Subscription c) => c);

        // Act
        await _service.ProcessStripeEventAsync(stripeEvent);

        // Assert
        existing.Status.Should().Be("past_due");
        _subscriptionRepoMock.Verify(s => s.UpdateAsync(It.Is<Models.Subscription>(x => x.Status == "past_due")), Times.Once);
    }

    [Fact]
    public async Task ProcessStripeEventAsync_InvoicePaymentFailed_UnknownSubscription_DoesNotUpdate()
    {
        // Arrange
        var invoicePayload = StripeTestHelpers.CreateInvoiceObjectPayload(
            invoiceId: "in_orphan",
            subscriptionId: "sub_unknown");
        var fullPayload = StripeTestHelpers.WrapInEventPayload(invoicePayload, "evt_5", "invoice.payment_failed");
        var stripeEvent = StripeTestHelpers.CreateSignedEvent(fullPayload);

        _subscriptionRepoMock
            .Setup(s => s.GetByStripeSubscriptionIdAsync("sub_unknown"))
            .ReturnsAsync((Models.Subscription?)null);

        // Act
        await _service.ProcessStripeEventAsync(stripeEvent);

        // Assert
        _subscriptionRepoMock.Verify(s => s.UpdateAsync(It.IsAny<Models.Subscription>()), Times.Never);
    }

    [Fact]
    public async Task ProcessStripeEventAsync_UnhandledEventType_DoesNotThrow()
    {
        // Arrange - use a minimal event type that we don't handle
        var objectPayload = """{"id":"cus_123","object":"customer"}""";
        var fullPayload = StripeTestHelpers.WrapInEventPayload(objectPayload, "evt_6", "customer.updated");
        var stripeEvent = StripeTestHelpers.CreateSignedEvent(fullPayload);

        // Act
        var act = () => _service.ProcessStripeEventAsync(stripeEvent);

        // Assert
        await act.Should().NotThrowAsync();
    }
}
