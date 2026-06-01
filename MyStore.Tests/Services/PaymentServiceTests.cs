using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Services;
using Xunit;

namespace MyStore.Tests.Services;

public class PaymentServiceTests
{
    private readonly Mock<IPaymentRepository> _paymentRepositoryMock;
    private readonly Mock<ICompanyRepository> _companyRepositoryMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<PaymentService>> _loggerMock;
    private readonly PaymentService _service;

    public PaymentServiceTests()
    {
        _paymentRepositoryMock = new Mock<IPaymentRepository>();
        _companyRepositoryMock = new Mock<ICompanyRepository>();
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<PaymentService>>();
        _configurationMock.Setup(c => c["Stripe:SecretKey"]).Returns((string?)null);
        _service = new PaymentService(
            _paymentRepositoryMock.Object,
            _companyRepositoryMock.Object,
            _configurationMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetPaymentMethodsAsync_EmptyBrand_ReturnsUnknownFallback()
    {
        // Arrange: simulate the pre-migration row with empty brand
        var companyId = 13;
        _paymentRepositoryMock
            .Setup(r => r.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(new List<PaymentMethod>
            {
                new PaymentMethod
                {
                    Id = 1,
                    CompanyId = companyId,
                    StripePaymentMethodId = "pm_legacy",
                    StripeCustomerId = "cus_legacy",
                    Brand = string.Empty,
                    Last4 = "4242",
                    ExpirationMonth = 12,
                    ExpirationYear = 2035,
                    IsDefault = true
                }
            });

        // Act
        var result = await _service.GetPaymentMethodsAsync(companyId);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        var methods = result.Data!.ToList();
        methods.Should().HaveCount(1);
        methods[0].Brand.Should().Be("unknown");
        methods[0].Last4.Should().Be("4242");
    }

    [Fact]
    public async Task GetPaymentMethodsAsync_EmptyBrand_DoesNotCallUpdateBrandAsync()
    {
        // Arrange: empty-brand row must not trigger an UPDATE that targets a
        // non-existent payment_method.brand column (regression for issue #270).
        var companyId = 13;
        _paymentRepositoryMock
            .Setup(r => r.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(new List<PaymentMethod>
            {
                new PaymentMethod
                {
                    Id = 1,
                    CompanyId = companyId,
                    StripePaymentMethodId = "pm_legacy",
                    StripeCustomerId = "cus_legacy",
                    Brand = string.Empty,
                    Last4 = "4242",
                    ExpirationMonth = 12,
                    ExpirationYear = 2035,
                    IsDefault = true
                }
            });

        // Act
        var result = await _service.GetPaymentMethodsAsync(companyId);

        // Assert
        result.Success.Should().BeTrue();
        _paymentRepositoryMock.Verify(
            r => r.UpdateBrandAsync(It.IsAny<int>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task GetPaymentMethodsAsync_PopulatedBrand_ReturnsActualBrand()
    {
        // Arrange
        var companyId = 7;
        _paymentRepositoryMock
            .Setup(r => r.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(new List<PaymentMethod>
            {
                new PaymentMethod
                {
                    Id = 2,
                    CompanyId = companyId,
                    StripePaymentMethodId = "pm_visa",
                    StripeCustomerId = "cus_visa",
                    Brand = "visa",
                    Last4 = "1111",
                    ExpirationMonth = 1,
                    ExpirationYear = 2030,
                    IsDefault = true
                }
            });

        // Act
        var result = await _service.GetPaymentMethodsAsync(companyId);

        // Assert
        result.Success.Should().BeTrue();
        var methods = result.Data!.ToList();
        methods.Should().HaveCount(1);
        methods[0].Brand.Should().Be("visa");
    }

    [Fact]
    public async Task GetPaymentMethodsAsync_RepositoryThrows_Returns500ErrorResponse()
    {
        // Arrange
        var companyId = 99;
        _paymentRepositoryMock
            .Setup(r => r.GetByCompanyIdAsync(companyId))
            .ThrowsAsync(new InvalidOperationException("db is down"));

        // Act
        var result = await _service.GetPaymentMethodsAsync(companyId);

        // Assert: still returns the friendly error (existing behavior preserved)
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("retrieving payment methods");
    }

    [Fact]
    public async Task GetPaymentMethodsAsync_EmptyList_Returns200WithEmptyData()
    {
        // Arrange
        var companyId = 42;
        _paymentRepositoryMock
            .Setup(r => r.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(new List<PaymentMethod>());

        // Act
        var result = await _service.GetPaymentMethodsAsync(companyId);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Should().BeEmpty();
    }
}
