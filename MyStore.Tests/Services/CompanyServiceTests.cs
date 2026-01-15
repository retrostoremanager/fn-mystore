using FluentAssertions;
using Moq;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Services;
using Xunit;

namespace MyStore.Tests.Services;

public class CompanyServiceTests
{
    private readonly Mock<ICompanyRepository> _repositoryMock;
    private readonly CompanyService _service;

    public CompanyServiceTests()
    {
        _repositoryMock = new Mock<ICompanyRepository>();
        _service = new CompanyService(_repositoryMock.Object);
    }

    [Fact]
    public async Task RegisterAccountAsync_ValidRequest_CreatesCompanyWithPendingStatus()
    {
        // Arrange
        var request = new RegisterAccountRequest
        {
            Email = "test@example.com",
            SubscriptionTier = "Trial"
        };

        Company? capturedCompany = null;
        _repositoryMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((Company?)null);

        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Company>()))
            .Callback<Company>(c => capturedCompany = c)
            .ReturnsAsync((Company c) => { c.Id = 1; return c; });

        // Act
        var result = await _service.RegisterAccountAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Status.Should().Be("Pending");
        result.Data.Id.Should().BeGreaterThan(0);
        result.Data.Email.Should().Be("test@example.com");
        
        capturedCompany.Should().NotBeNull();
        capturedCompany!.Status.Should().Be("Pending");
        capturedCompany.Email.Should().Be("test@example.com");
    }

    [Fact]
    public async Task RegisterAccountAsync_DuplicateEmail_ReturnsErrorWith409Message()
    {
        // Arrange
        var request = new RegisterAccountRequest
        {
            Email = "existing@example.com",
            SubscriptionTier = "Trial"
        };

        var existingCompany = new Company
        {
            Id = 1,
            Email = "existing@example.com",
            Status = "Active"
        };

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(existingCompany);

        // Act
        var result = await _service.RegisterAccountAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("already exists", because: "Duplicate email should return error");
        result.Data.Should().BeNull();
        
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<Company>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAccountAsync_Initializes30DayTrialPeriod()
    {
        // Arrange
        var request = new RegisterAccountRequest
        {
            Email = "test@example.com",
            SubscriptionTier = "Trial"
        };

        var beforeCreation = DateTime.UtcNow;

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((Company?)null);

        Company? capturedCompany = null;
        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Company>()))
            .Callback<Company>(c => capturedCompany = c)
            .ReturnsAsync((Company c) => { c.Id = 1; return c; });

        // Act
        var result = await _service.RegisterAccountAsync(request);

        var afterCreation = DateTime.UtcNow;

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        
        capturedCompany.Should().NotBeNull();
        capturedCompany!.TrialStartDate.Should().BeCloseTo(beforeCreation, TimeSpan.FromSeconds(5));
        capturedCompany.TrialEndDate.Should().BeCloseTo(beforeCreation.AddDays(30), TimeSpan.FromSeconds(5));
        
        var trialDuration = capturedCompany.TrialEndDate - capturedCompany.TrialStartDate;
        trialDuration.TotalDays.Should().BeApproximately(30, 0.01);
        
        result.Data!.TrialStartDate.Should().BeCloseTo(beforeCreation, TimeSpan.FromSeconds(5));
        result.Data.TrialEndDate.Should().BeCloseTo(beforeCreation.AddDays(30), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RegisterAccountAsync_GeneratesSecureVerificationToken()
    {
        // Arrange
        var request = new RegisterAccountRequest
        {
            Email = "test@example.com",
            SubscriptionTier = "Trial"
        };

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((Company?)null);

        Company? capturedCompany = null;
        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Company>()))
            .Callback<Company>(c => capturedCompany = c)
            .ReturnsAsync((Company c) => { c.Id = 1; return c; });

        // Act
        var result = await _service.RegisterAccountAsync(request);

        // Assert
        capturedCompany.Should().NotBeNull();
        capturedCompany!.VerificationToken.Should().NotBeNullOrEmpty();
        capturedCompany.VerificationToken!.Length.Should().BeGreaterThanOrEqualTo(20); // Secure token should be substantial
        
        // Token should be URL-safe (no +, /, or = characters)
        capturedCompany.VerificationToken.Should().NotContain("+");
        capturedCompany.VerificationToken.Should().NotContain("/");
        capturedCompany.VerificationToken.Should().NotContain("=");
    }

    [Fact]
    public async Task RegisterAccountAsync_TokenExpiresIn24Hours()
    {
        // Arrange
        var request = new RegisterAccountRequest
        {
            Email = "test@example.com",
            SubscriptionTier = "Trial"
        };

        var beforeCreation = DateTime.UtcNow;

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((Company?)null);

        Company? capturedCompany = null;
        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Company>()))
            .Callback<Company>(c => capturedCompany = c)
            .ReturnsAsync((Company c) => { c.Id = 1; return c; });

        // Act
        var result = await _service.RegisterAccountAsync(request);

        var afterCreation = DateTime.UtcNow;

        // Assert
        capturedCompany.Should().NotBeNull();
        capturedCompany!.VerificationTokenExpires.Should().NotBeNull();
        capturedCompany.VerificationTokenExpires!.Value.Should().BeCloseTo(beforeCreation.AddHours(24), TimeSpan.FromSeconds(5));
        
        var expirationDuration = capturedCompany.VerificationTokenExpires.Value - beforeCreation;
        expirationDuration.TotalHours.Should().BeApproximately(24, 0.1);
    }

    [Fact]
    public async Task RegisterAccountAsync_Returns201CreatedResponse()
    {
        // Arrange
        var request = new RegisterAccountRequest
        {
            Email = "test@example.com",
            SubscriptionTier = "Trial"
        };

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((Company?)null);

        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Company>()))
            .ReturnsAsync((Company c) => { c.Id = 1; return c; });

        // Act
        var result = await _service.RegisterAccountAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Message.Should().Contain("successfully", because: "Successful registration should have success message");
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(1);
    }

    [Fact]
    public async Task RegisterAccountAsync_AssociatesSubscriptionTier()
    {
        // Arrange
        var request = new RegisterAccountRequest
        {
            Email = "test@example.com",
            SubscriptionTier = "Premium"
        };

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((Company?)null);

        Company? capturedCompany = null;
        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Company>()))
            .Callback<Company>(c => capturedCompany = c)
            .ReturnsAsync((Company c) => { c.Id = 1; return c; });

        // Act
        var result = await _service.RegisterAccountAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        capturedCompany.Should().NotBeNull();
        capturedCompany!.SubscriptionTier.Should().Be("Premium");
        result.Data!.SubscriptionTier.Should().Be("Premium");
    }

    [Fact]
    public async Task RegisterAccountAsync_DefaultSubscriptionTierIsTrial()
    {
        // Arrange
        var request = new RegisterAccountRequest
        {
            Email = "test@example.com"
            // SubscriptionTier not set, should default to "Trial"
        };

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((Company?)null);

        Company? capturedCompany = null;
        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Company>()))
            .Callback<Company>(c => capturedCompany = c)
            .ReturnsAsync((Company c) => { c.Id = 1; return c; });

        // Act
        var result = await _service.RegisterAccountAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        capturedCompany.Should().NotBeNull();
        capturedCompany!.SubscriptionTier.Should().Be("Trial");
        result.Data!.SubscriptionTier.Should().Be("Trial");
    }

    [Fact]
    public async Task RegisterAccountAsync_EmailValidation_EmptyEmail_ReturnsError()
    {
        // Arrange
        var request = new RegisterAccountRequest
        {
            Email = string.Empty,
            SubscriptionTier = "Trial"
        };

        // Act
        var result = await _service.RegisterAccountAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("required", because: "Empty email should return error");
        result.Data.Should().BeNull();
        
        _repositoryMock.Verify(r => r.GetByEmailAsync(It.IsAny<string>()), Times.Never);
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<Company>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAccountAsync_EmailValidation_WhitespaceEmail_ReturnsError()
    {
        // Arrange
        var request = new RegisterAccountRequest
        {
            Email = "   ",
            SubscriptionTier = "Trial"
        };

        // Act
        var result = await _service.RegisterAccountAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("required", because: "Empty email should return error");
        result.Data.Should().BeNull();
        
        _repositoryMock.Verify(r => r.GetByEmailAsync(It.IsAny<string>()), Times.Never);
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<Company>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAccountAsync_EmailValidation_InvalidEmailFormat_ReturnsError()
    {
        // Arrange
        var request = new RegisterAccountRequest
        {
            Email = "not-an-email",
            SubscriptionTier = "Trial"
        };

        // Act
        var result = await _service.RegisterAccountAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Invalid email format", because: "Invalid email should return error");
        result.Data.Should().BeNull();
        
        _repositoryMock.Verify(r => r.GetByEmailAsync(It.IsAny<string>()), Times.Never);
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<Company>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAccountAsync_EmailValidation_ValidEmailFormats_Accepted()
    {
        // Arrange
        var validEmails = new[]
        {
            "test@example.com",
            "user.name@example.co.uk",
            "user+tag@example.com",
            "user123@test-domain.com"
        };

        foreach (var email in validEmails)
        {
            var request = new RegisterAccountRequest
            {
                Email = email,
                SubscriptionTier = "Trial"
            };

            _repositoryMock
                .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync((Company?)null);

            _repositoryMock
                .Setup(r => r.CreateAsync(It.IsAny<Company>()))
                .ReturnsAsync((Company c) => { c.Id = 1; return c; });

            // Act
            var result = await _service.RegisterAccountAsync(request);

            // Assert
            result.Success.Should().BeTrue($"Email '{email}' should be valid");
            result.Data.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task RegisterAccountAsync_EmailNormalizedToLowercase()
    {
        // Arrange
        var request = new RegisterAccountRequest
        {
            Email = "Test@Example.COM",
            SubscriptionTier = "Trial"
        };

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((Company?)null);

        Company? capturedCompany = null;
        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Company>()))
            .Callback<Company>(c => capturedCompany = c)
            .ReturnsAsync((Company c) => { c.Id = 1; return c; });

        // Act
        var result = await _service.RegisterAccountAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        capturedCompany.Should().NotBeNull();
        capturedCompany!.Email.Should().Be("test@example.com");
        result.Data!.Email.Should().Be("test@example.com");
    }

    [Fact]
    public async Task RegisterAccountAsync_RepositoryException_ReturnsError()
    {
        // Arrange
        var request = new RegisterAccountRequest
        {
            Email = "test@example.com",
            SubscriptionTier = "Trial"
        };

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ThrowsAsync(new Exception("Database connection failed"));

        // Act
        var result = await _service.RegisterAccountAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Failed to register account", because: "Exception should return error message");
        result.Errors.Should().NotBeEmpty();
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task RegisterAccountAsync_UniqueTokensGenerated()
    {
        // Arrange
        var request = new RegisterAccountRequest
        {
            Email = "test@example.com",
            SubscriptionTier = "Trial"
        };

        var tokens = new List<string>();

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((Company?)null);

        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Company>()))
            .Callback<Company>(c => tokens.Add(c.VerificationToken!))
            .ReturnsAsync((Company c) => { c.Id = 1; return c; });

        // Act - Register multiple accounts
        for (int i = 0; i < 10; i++)
        {
            request.Email = $"test{i}@example.com";
            await _service.RegisterAccountAsync(request);
        }

        // Assert
        tokens.Should().HaveCount(10);
        tokens.Should().OnlyHaveUniqueItems();
    }
}
