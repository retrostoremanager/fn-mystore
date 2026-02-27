using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Services;
using Xunit;

namespace MyStore.Tests.Services;

public class TrialConversionServiceTests
{
    private readonly Mock<ICompanyRepository> _companyRepositoryMock;
    private readonly Mock<ILogger<TrialConversionService>> _loggerMock;
    private readonly TrialConversionService _service;

    public TrialConversionServiceTests()
    {
        _companyRepositoryMock = new Mock<ICompanyRepository>();
        _loggerMock = new Mock<ILogger<TrialConversionService>>();

        var stripeOptions = Options.Create(new StripeOptions
        {
            SecretKey = "sk_test_xxx",
            PriceIdBasic = "price_basic123"
        });

        _service = new TrialConversionService(
            _companyRepositoryMock.Object,
            stripeOptions,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ProcessExpiredTrialsAsync_NoCandidates_ReturnsZero()
    {
        _companyRepositoryMock
            .Setup(r => r.GetExpiredTrialsForConversionAsync())
            .ReturnsAsync(Array.Empty<TrialConversionCandidate>());

        var result = await _service.ProcessExpiredTrialsAsync();

        result.Should().Be(0);
    }

    [Fact]
    public async Task ProcessExpiredTrialsAsync_StripeNotConfigured_ReturnsZero()
    {
        var optionsNoStripe = Options.Create(new StripeOptions { SecretKey = "" });
        var service = new TrialConversionService(
            _companyRepositoryMock.Object,
            optionsNoStripe,
            _loggerMock.Object);

        var result = await service.ProcessExpiredTrialsAsync();

        result.Should().Be(0);
        _companyRepositoryMock.Verify(r => r.GetExpiredTrialsForConversionAsync(), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredTrialsAsync_PriceIdNotConfigured_ReturnsZero()
    {
        var optionsNoPrice = Options.Create(new StripeOptions
        {
            SecretKey = "sk_test_xxx",
            PriceIdBasic = ""
        });
        var service = new TrialConversionService(
            _companyRepositoryMock.Object,
            optionsNoPrice,
            _loggerMock.Object);

        var result = await service.ProcessExpiredTrialsAsync();

        result.Should().Be(0);
    }
}
