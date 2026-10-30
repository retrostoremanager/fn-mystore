using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly Mock<ISubscriptionAddOnRepository> _addOnRepoMock;
    private readonly Mock<IPaymentRepository> _paymentRepoMock;
    private readonly Mock<ILogger<MyStore.Services.SubscriptionService>> _loggerMock;
    private readonly MyStore.Services.SubscriptionService _service;

    public SubscriptionServiceTests()
    {
        _subscriptionRepoMock = new Mock<ISubscriptionRepository>();
        _addOnRepoMock = new Mock<ISubscriptionAddOnRepository>();
        _paymentRepoMock = new Mock<IPaymentRepository>();
        _loggerMock = new Mock<ILogger<MyStore.Services.SubscriptionService>>();
        var stripeOptions = Options.Create(new StripeOptions());
        _service = new MyStore.Services.SubscriptionService(
            _subscriptionRepoMock.Object, _addOnRepoMock.Object, _paymentRepoMock.Object, stripeOptions, _loggerMock.Object);
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
    public async Task ProcessStripeEventAsync_SubscriptionUpdated_WithAddOnItem_StoresBasePriceAndCreatesAddOnRow()
    {
        // Arrange -- 2-item subscription (base + Lot Scanner add-on). Before the fix, the
        // service's `.FirstOrDefault()` read would silently store whichever item happened to
        // be first and never touch the add-on repository at all.
        var stripeOptionsWithAddOn = Options.Create(new StripeOptions { AddOnPriceIdLotScanner = "price_addon_lotscanner" });
        var service = new MyStore.Services.SubscriptionService(
            _subscriptionRepoMock.Object, _addOnRepoMock.Object, _paymentRepoMock.Object, stripeOptionsWithAddOn, _loggerMock.Object);

        var subPayload = StripeTestHelpers.CreateSubscriptionObjectPayload(
            subscriptionId: "sub_withaddon",
            customerId: "cus_company2",
            status: "active",
            secondItemId: "si_addon",
            secondItemPriceId: "price_addon_lotscanner");
        var fullPayload = StripeTestHelpers.WrapInEventPayload(subPayload, "evt_3", "customer.subscription.updated");
        var stripeEvent = StripeTestHelpers.CreateSignedEvent(fullPayload);

        _paymentRepoMock
            .Setup(p => p.GetCompanyIdByStripeCustomerIdAsync("cus_company2"))
            .ReturnsAsync(99);
        _subscriptionRepoMock
            .Setup(s => s.GetByStripeSubscriptionIdAsync("sub_withaddon"))
            .ReturnsAsync((Models.Subscription?)null);
        Models.Subscription? capturedSub = null;
        _subscriptionRepoMock
            .Setup(s => s.CreateAsync(It.IsAny<Models.Subscription>()))
            .Callback<Models.Subscription>(c => capturedSub = c)
            .ReturnsAsync((Models.Subscription c) => { c.Id = 7; return c; });
        _addOnRepoMock
            .Setup(a => a.GetBySubscriptionIdAndTypeAsync(7, "lot_scanner"))
            .ReturnsAsync((Models.SubscriptionAddOn?)null);
        Models.SubscriptionAddOn? capturedAddOn = null;
        _addOnRepoMock
            .Setup(a => a.CreateAsync(It.IsAny<Models.SubscriptionAddOn>()))
            .Callback<Models.SubscriptionAddOn>(c => capturedAddOn = c)
            .ReturnsAsync((Models.SubscriptionAddOn c) => c);

        // Act
        await service.ProcessStripeEventAsync(stripeEvent);

        // Assert
        capturedSub.Should().NotBeNull();
        capturedSub!.StripePriceId.Should().Be("price_test123", "the base item's price, not the add-on's");

        capturedAddOn.Should().NotBeNull();
        capturedAddOn!.SubscriptionId.Should().Be(7);
        capturedAddOn.AddonType.Should().Be("lot_scanner");
        capturedAddOn.StripeSubscriptionItemId.Should().Be("si_addon");
        capturedAddOn.StripePriceId.Should().Be("price_addon_lotscanner");
        capturedAddOn.Status.Should().Be("active");
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
