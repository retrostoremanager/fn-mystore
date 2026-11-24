using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Services;
using Xunit;

namespace MyStore.Tests.Services;

public class AdminServiceTests
{
    private readonly Mock<ICompanyRepository> _companyRepositoryMock;
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ISubscriptionRepository> _subscriptionRepositoryMock;
    private readonly Mock<ISubscriptionAddOnRepository> _addOnRepositoryMock;
    private readonly Mock<ILogger<AdminService>> _loggerMock;
    private readonly AdminService _service;

    public AdminServiceTests()
    {
        _companyRepositoryMock = new Mock<ICompanyRepository>();
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _subscriptionRepositoryMock = new Mock<ISubscriptionRepository>();
        _addOnRepositoryMock = new Mock<ISubscriptionAddOnRepository>();
        _loggerMock = new Mock<ILogger<AdminService>>();

        _companyRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Company>()))
            .ReturnsAsync((Company c) => { c.Id = 42; return c; });
        _roleRepositoryMock
            .Setup(r => r.GetForCompanyAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Role { Id = 7, Name = "Owner", CompanyId = null }]);
        _userRepositoryMock
            .Setup(u => u.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User u, CancellationToken _) => { u.Id = 1; return u; });
        _subscriptionRepositoryMock
            .Setup(s => s.CreateAsync(It.IsAny<Subscription>()))
            .ReturnsAsync((Subscription s) => { s.Id = 99; return s; });

        _service = new AdminService(
            _companyRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _userRepositoryMock.Object,
            _subscriptionRepositoryMock.Object,
            _addOnRepositoryMock.Object,
            _loggerMock.Object);
    }

    private static CreateTestCompanyRequest ValidRequest(bool grantAddOn = true) => new()
    {
        Email = "test-owner@example.com",
        CompanyName = "Test Co",
        Password = "ValidPass123",
        GrantLotScannerAddOn = grantAddOn
    };

    [Fact]
    public async Task CreateTestCompanyAsync_NeverTouchesPaymentMethodStorage()
    {
        // The whole point of this path is skipping Stripe entirely -- unlike
        // RegisterAccountAsync, there is no IPaymentService dependency at all, so there is
        // no payment method call to make in the first place.
        var result = await _service.CreateTestCompanyAsync(ValidRequest());

        result.CompanyId.Should().Be(42);
        result.Status.Should().Be("Active");
    }

    [Fact]
    public async Task CreateTestCompanyAsync_CreatesCompanyAsImmediatelyActiveWithATenYearTrialWindow()
    {
        Company? captured = null;
        _companyRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Company>()))
            .Callback<Company>(c => captured = c)
            .ReturnsAsync((Company c) => { c.Id = 42; return c; });

        await _service.CreateTestCompanyAsync(ValidRequest());

        captured.Should().NotBeNull();
        captured!.Status.Should().Be("Active"); // no email verification step to wait on
        (captured.TrialEndDate - captured.TrialStartDate).Should().BeCloseTo(TimeSpan.FromDays(3650), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task CreateTestCompanyAsync_BootstrapsTheOwnerRoleSoRbacGatedEndpointsWork()
    {
        await _service.CreateTestCompanyAsync(ValidRequest());

        _userRepositoryMock.Verify(u => u.CreateAsync(
            It.Is<User>(usr => usr.CompanyId == 42 && usr.UserType == "owner" && usr.Email == "test-owner@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);
        _userRepositoryMock.Verify(u => u.AssignRolesAsync(1, It.Is<IEnumerable<int>>(ids => ids.Contains(7)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateTestCompanyAsync_CreatesASubscriptionRowSinceNoStripeWebhookWillEverDoIt()
    {
        // Real sign-ups only get a `subscription` row via a Stripe webhook
        // (SubscriptionService.HandleSubscriptionEventAsync) once a real plan is picked --
        // there's no Stripe object here to fire that webhook, so GetSubscriptionStatus would
        // 404 "No subscription found" for this company without this.
        await _service.CreateTestCompanyAsync(ValidRequest());

        _subscriptionRepositoryMock.Verify(s => s.CreateAsync(It.Is<Subscription>(
            sub => sub.CompanyId == 42 && sub.Status == "active" && !string.IsNullOrEmpty(sub.StripeSubscriptionId))), Times.Once);
    }

    [Fact]
    public async Task CreateTestCompanyAsync_GrantsTheLotScannerAddOnWhenRequested()
    {
        await _service.CreateTestCompanyAsync(ValidRequest(grantAddOn: true));

        _addOnRepositoryMock.Verify(a => a.CreateAsync(It.Is<SubscriptionAddOn>(
            addOn => addOn.SubscriptionId == 99 && addOn.AddonType == "lot_scanner" && addOn.Status == "active")), Times.Once);
    }

    [Fact]
    public async Task CreateTestCompanyAsync_SkipsTheAddOnWhenNotRequested()
    {
        await _service.CreateTestCompanyAsync(ValidRequest(grantAddOn: false));

        _addOnRepositoryMock.Verify(a => a.CreateAsync(It.IsAny<SubscriptionAddOn>()), Times.Never);
    }

    [Theory]
    [InlineData("", "Test Co", "ValidPass123")]
    [InlineData("test@example.com", "", "ValidPass123")]
    [InlineData("test@example.com", "Test Co", "")]
    public async Task CreateTestCompanyAsync_RejectsMissingRequiredFields(string email, string companyName, string password)
    {
        var request = new CreateTestCompanyRequest { Email = email, CompanyName = companyName, Password = password };

        var act = async () => await _service.CreateTestCompanyAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
        _companyRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<Company>()), Times.Never);
    }
}
