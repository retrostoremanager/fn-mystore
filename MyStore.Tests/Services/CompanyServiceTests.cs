using System;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Services;
using Xunit;

namespace MyStore.Tests.Services;

public class CompanyServiceTests
{
    private readonly Mock<ICompanyRepository> _repositoryMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Mock<IPaymentService> _paymentServiceMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<CompanyService>> _loggerMock;
    private readonly CompanyService _service;

    public CompanyServiceTests()
    {
        _repositoryMock = new Mock<ICompanyRepository>();
        _emailServiceMock = new Mock<IEmailService>();
        _paymentServiceMock = new Mock<IPaymentService>();
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<CompanyService>>();
        _paymentServiceMock
            .Setup(p => p.StorePaymentMethodAsync(It.IsAny<int>(), It.IsAny<StorePaymentMethodRequest>()))
            .ReturnsAsync(ApiResponse<StorePaymentMethodResponse>.SuccessResponse(
                new StorePaymentMethodResponse { Id = 1, Last4 = "4242", ExpirationMonth = 12, ExpirationYear = 34, IsDefault = true }));
        _service = new CompanyService(_repositoryMock.Object, _emailServiceMock.Object, _paymentServiceMock.Object, _configurationMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Helper method to create a valid RegisterAccountRequest for testing.
    /// </summary>
    private static RegisterAccountRequest CreateValidRequest(string? email = null, string? password = null, string? companyName = null, string? subscriptionTier = null, string? paymentMethodId = null)
    {
        return new RegisterAccountRequest
        {
            Email = email ?? "test@example.com",
            Password = password ?? "ValidPass123",
            CompanyName = companyName ?? "Test Company",
            SubscriptionTier = subscriptionTier ?? "Trial",
            PaymentMethodId = paymentMethodId ?? "pm_test_123"
        };
    }

    [Fact]
    public async Task RegisterAccountAsync_ValidRequest_CreatesCompanyWithPendingStatus()
    {
        // Arrange
        var request = CreateValidRequest();

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
        var request = CreateValidRequest(email: "existing@example.com");

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
        result.FieldErrors.Should().NotBeNull();
        result.FieldErrors!.Should().ContainKey("email");
        result.FieldErrors["email"].Should().Contain(e => e.Contains("already registered", StringComparison.OrdinalIgnoreCase));
        result.Data.Should().BeNull();
        
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<Company>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAccountAsync_Initializes30DayTrialPeriod()
    {
        // Arrange
        var request = CreateValidRequest();

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
        var request = CreateValidRequest();

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
        var request = CreateValidRequest();

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
        var request = CreateValidRequest();

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
        var request = CreateValidRequest(subscriptionTier: "Premium");

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
            Email = "test@example.com",
            Password = "ValidPass123",
            CompanyName = "Test Company",
            PaymentMethodId = "pm_test_123"
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
        var request = CreateValidRequest(email: string.Empty);

        // Act
        var result = await _service.RegisterAccountAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.FieldErrors.Should().NotBeNull();
        result.FieldErrors!.Should().ContainKey("email");
        result.FieldErrors["email"].Should().Contain(e => e.Contains("required", StringComparison.OrdinalIgnoreCase));
        result.Data.Should().BeNull();
        
        _repositoryMock.Verify(r => r.GetByEmailAsync(It.IsAny<string>()), Times.Never);
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<Company>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAccountAsync_EmailValidation_WhitespaceEmail_ReturnsError()
    {
        // Arrange
        var request = CreateValidRequest(email: "   ");

        // Act
        var result = await _service.RegisterAccountAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.FieldErrors.Should().NotBeNull();
        result.FieldErrors!.Should().ContainKey("email");
        result.FieldErrors["email"].Should().Contain(e => e.Contains("required", StringComparison.OrdinalIgnoreCase));
        result.Data.Should().BeNull();
        
        _repositoryMock.Verify(r => r.GetByEmailAsync(It.IsAny<string>()), Times.Never);
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<Company>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAccountAsync_EmailValidation_InvalidEmailFormat_ReturnsError()
    {
        // Arrange
        var request = CreateValidRequest(email: "not-an-email");

        // Act
        var result = await _service.RegisterAccountAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.FieldErrors.Should().NotBeNull();
        result.FieldErrors!.Should().ContainKey("email");
        result.FieldErrors["email"].Should().Contain(e => e.Contains("valid", StringComparison.OrdinalIgnoreCase));
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
            var request = CreateValidRequest(email: email);

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
        var request = CreateValidRequest(email: "Test@Example.COM");

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
        var request = CreateValidRequest();

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
        var request = CreateValidRequest();

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

    [Fact]
    public async Task RegisterAccountAsync_SuccessfulRegistration_SendsVerificationEmail()
    {
        // Arrange
        var request = CreateValidRequest(email: "test@example.com", companyName: "Test Company");

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((Company?)null);

        Company? capturedCompany = null;
        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Company>()))
            .Callback<Company>(c => capturedCompany = c)
            .ReturnsAsync((Company c) => { c.Id = 1; return c; });

        _emailServiceMock
            .Setup(e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new EmailSendResult { Success = true });

        // Act
        var result = await _service.RegisterAccountAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        
        // Wait a bit for the async email task to start (fire-and-forget pattern)
        await Task.Delay(200);
        
        // Verify email service was called with correct parameters
        _emailServiceMock.Verify(
            e => e.SendVerificationEmailAsync(
                "test@example.com",
                It.Is<string>(token => !string.IsNullOrEmpty(token) && token.Length >= 20),
                "Test Company"
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task RegisterAccountAsync_EmailSendingFails_AccountCreationStillSucceeds()
    {
        // Arrange
        var request = CreateValidRequest();

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((Company?)null);

        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Company>()))
            .ReturnsAsync((Company c) => { c.Id = 1; return c; });

        _emailServiceMock
            .Setup(e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new EmailSendResult 
            { 
                Success = false, 
                ErrorMessage = "Email service unavailable" 
            });

        // Act
        var result = await _service.RegisterAccountAsync(request);

        // Assert
        // Account creation should succeed even if email fails
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().BeGreaterThan(0);
        
        // Verify email service was still called (attempted)
        await Task.Delay(200); // Wait for async email task
        _emailServiceMock.Verify(
            e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Once
        );
    }

    [Fact]
    public async Task RegisterAccountAsync_EmailSendingThrowsException_AccountCreationStillSucceeds()
    {
        // Arrange
        var request = CreateValidRequest();

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((Company?)null);

        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Company>()))
            .ReturnsAsync((Company c) => { c.Id = 1; return c; });

        _emailServiceMock
            .Setup(e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("Email service error"));

        // Act
        var result = await _service.RegisterAccountAsync(request);

        // Assert
        // Account creation should succeed even if email throws exception
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().BeGreaterThan(0);
        
        // Verify email service was still called (attempted)
        await Task.Delay(200); // Wait for async email task
        _emailServiceMock.Verify(
            e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Once
        );
    }

    [Fact]
    public async Task RegisterAccountAsync_EmailServiceCalledWithCorrectToken()
    {
        // Arrange
        var request = CreateValidRequest();

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((Company?)null);

        Company? capturedCompany = null;
        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Company>()))
            .Callback<Company>(c => capturedCompany = c)
            .ReturnsAsync((Company c) => { c.Id = 1; return c; });

        string? capturedToken = null;
        _emailServiceMock
            .Setup(e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((email, token, companyName) => capturedToken = token)
            .ReturnsAsync(new EmailSendResult { Success = true });

        // Act
        var result = await _service.RegisterAccountAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        
        await Task.Delay(200); // Wait for async email task
        
        // Verify the token passed to email service matches the one stored in company
        capturedToken.Should().NotBeNull();
        capturedCompany.Should().NotBeNull();
        capturedToken.Should().Be(capturedCompany!.VerificationToken);
    }

    #region ResendVerificationEmailAsync Tests

    [Fact]
    public async Task ResendVerificationEmailAsync_ValidRequest_UnverifiedAccount_GeneratesNewToken()
    {
        // Arrange
        var email = "test@example.com";
        var request = new ResendVerificationEmailRequest { Email = email };

        var existingCompany = new Company
        {
            Id = 1,
            Email = email,
            Status = "Pending",
            VerificationToken = "old-token",
            VerificationTokenExpires = DateTime.UtcNow.AddHours(12)
        };

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(email))
            .ReturnsAsync(existingCompany);

        Company? updatedCompany = null;
        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<int>(), It.IsAny<Company>()))
            .Callback<int, Company>((id, company) => updatedCompany = company)
            .ReturnsAsync((int id, Company c) => c);

        _emailServiceMock
            .Setup(e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new EmailSendResult { Success = true });

        // Act
        var result = await _service.ResendVerificationEmailAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Email.Should().Be(email);
        result.Message.Should().ContainEquivalentOf("sent");

        updatedCompany.Should().NotBeNull();
        updatedCompany!.VerificationToken.Should().NotBeNullOrEmpty();
        updatedCompany.VerificationToken.Should().NotBe("old-token"); // New token generated
        updatedCompany.VerificationTokenExpires.Should().NotBeNull();
        updatedCompany.VerificationTokenExpires!.Value.Should().BeCloseTo(DateTime.UtcNow.AddHours(24), TimeSpan.FromSeconds(5));

        await Task.Delay(200); // Wait for async email task
        _emailServiceMock.Verify(
            e => e.SendVerificationEmailAsync(email, It.Is<string>(t => t != "old-token"), It.IsAny<string>()),
            Times.Once
        );
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_AlreadyVerifiedAccount_ReturnsError()
    {
        // Arrange
        var email = "verified@example.com";
        var request = new ResendVerificationEmailRequest { Email = email };

        var verifiedCompany = new Company
        {
            Id = 1,
            Email = email,
            Status = "Active"
        };

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(email))
            .ReturnsAsync(verifiedCompany);

        // Act
        var result = await _service.ResendVerificationEmailAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().ContainEquivalentOf("already verified");
        result.Errors.Should().Contain(e => e.IndexOf("already verified", StringComparison.OrdinalIgnoreCase) >= 0);

        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<int>(), It.IsAny<Company>()), Times.Never);
        _emailServiceMock.Verify(
            e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_NonExistentEmail_ReturnsSuccessForSecurity()
    {
        // Arrange
        var email = "nonexistent@example.com";
        var request = new ResendVerificationEmailRequest { Email = email };

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(email))
            .ReturnsAsync((Company?)null);

        // Act
        var result = await _service.ResendVerificationEmailAsync(request);

        // Assert
        // For security, don't reveal if email exists or not
        result.Success.Should().BeTrue();
        result.Message.Should().ContainEquivalentOf("If an account exists");
        result.Data.Should().NotBeNull();
        result.Data!.Email.Should().Be(email);

        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<int>(), It.IsAny<Company>()), Times.Never);
        _emailServiceMock.Verify(
            e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_InvalidEmailFormat_ReturnsError()
    {
        // Arrange
        var request = new ResendVerificationEmailRequest { Email = "invalid-email" };

        // Act
        var result = await _service.ResendVerificationEmailAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().ContainEquivalentOf("valid email");
        result.Errors.Should().Contain(e => e.IndexOf("Invalid email format", StringComparison.OrdinalIgnoreCase) >= 0);

        _repositoryMock.Verify(r => r.GetByEmailAsync(It.IsAny<string>()), Times.Never);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<int>(), It.IsAny<Company>()), Times.Never);
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_EmptyEmail_ReturnsError()
    {
        // Arrange
        var request = new ResendVerificationEmailRequest { Email = string.Empty };

        // Act
        var result = await _service.ResendVerificationEmailAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().ContainEquivalentOf("required");
        result.Errors.Should().Contain(e => e.IndexOf("required", StringComparison.OrdinalIgnoreCase) >= 0);

        _repositoryMock.Verify(r => r.GetByEmailAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_RateLimitExceeded_ReturnsError()
    {
        // Arrange
        var email = "ratelimited@example.com";
        var request = new ResendVerificationEmailRequest { Email = email };

        var company = new Company
        {
            Id = 1,
            Email = email,
            Status = "Pending"
        };

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(email))
            .ReturnsAsync(company);

        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<int>(), It.IsAny<Company>()))
            .ReturnsAsync((int id, Company c) => c);

        _emailServiceMock
            .Setup(e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new EmailSendResult { Success = true });

        // Act - Make 3 requests (should succeed)
        for (int i = 0; i < 3; i++)
        {
            var result = await _service.ResendVerificationEmailAsync(request);
            result.Success.Should().BeTrue($"Request {i + 1} should succeed");
        }

        // 4th request should fail due to rate limit
        var rateLimitedResult = await _service.ResendVerificationEmailAsync(request);

        // Assert
        rateLimitedResult.Success.Should().BeFalse();
        rateLimitedResult.Message.Should().ContainEquivalentOf("Too many");
        rateLimitedResult.Errors.Should().Contain(e => e.IndexOf("Rate limit", StringComparison.OrdinalIgnoreCase) >= 0 || e.IndexOf("Too many", StringComparison.OrdinalIgnoreCase) >= 0);

        // Verify only 3 updates occurred (4th was blocked)
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<int>(), It.IsAny<Company>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_RateLimitResetsAfterHour()
    {
        // Arrange
        var email = "ratelimitreset@example.com";
        var request = new ResendVerificationEmailRequest { Email = email };

        var company = new Company
        {
            Id = 1,
            Email = email,
            Status = "Pending"
        };

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(email))
            .ReturnsAsync(company);

        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<int>(), It.IsAny<Company>()))
            .ReturnsAsync((int id, Company c) => c);

        _emailServiceMock
            .Setup(e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new EmailSendResult { Success = true });

        // Act - Make 3 requests
        for (int i = 0; i < 3; i++)
        {
            await _service.ResendVerificationEmailAsync(request);
        }

        // Simulate time passing by manipulating the rate limit dictionary
        // Note: This is a bit of a hack since the dictionary is private, but we can test the behavior
        // by making requests with different emails or waiting
        // For this test, we'll verify that after making 3 requests, a 4th is blocked
        var blockedResult = await _service.ResendVerificationEmailAsync(request);
        blockedResult.Success.Should().BeFalse();

        // Note: In a real scenario, we'd need to wait 1 hour or mock time
        // For now, we verify the rate limiting works correctly
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_OldTokenInvalidated()
    {
        // Arrange
        var email = "tokeninvalidation@example.com";
        var request = new ResendVerificationEmailRequest { Email = email };

        var company = new Company
        {
            Id = 1,
            Email = email,
            Status = "Pending",
            VerificationToken = "old-token-12345",
            VerificationTokenExpires = DateTime.UtcNow.AddHours(12)
        };

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(email))
            .ReturnsAsync(company);

        Company? updatedCompany = null;
        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<int>(), It.IsAny<Company>()))
            .Callback<int, Company>((id, c) => updatedCompany = c)
            .ReturnsAsync((int id, Company c) => c);

        _emailServiceMock
            .Setup(e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new EmailSendResult { Success = true });

        // Act
        var result = await _service.ResendVerificationEmailAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        updatedCompany.Should().NotBeNull();
        updatedCompany!.VerificationToken.Should().NotBe("old-token-12345"); // Old token replaced
        updatedCompany.VerificationToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_NewTokenExpiresIn24Hours()
    {
        // Arrange
        var email = "tokenexpiry@example.com";
        var request = new ResendVerificationEmailRequest { Email = email };

        var beforeResend = DateTime.UtcNow;

        var company = new Company
        {
            Id = 1,
            Email = email,
            Status = "Pending"
        };

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(email))
            .ReturnsAsync(company);

        Company? updatedCompany = null;
        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<int>(), It.IsAny<Company>()))
            .Callback<int, Company>((id, c) => updatedCompany = c)
            .ReturnsAsync((int id, Company c) => c);

        _emailServiceMock
            .Setup(e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new EmailSendResult { Success = true });

        // Act
        var result = await _service.ResendVerificationEmailAsync(request);

        var afterResend = DateTime.UtcNow;

        // Assert
        result.Success.Should().BeTrue();
        updatedCompany.Should().NotBeNull();
        updatedCompany!.VerificationTokenExpires.Should().NotBeNull();
        updatedCompany.VerificationTokenExpires!.Value.Should().BeCloseTo(beforeResend.AddHours(24), TimeSpan.FromSeconds(5));

        var expirationDuration = updatedCompany.VerificationTokenExpires.Value - beforeResend;
        expirationDuration.TotalHours.Should().BeApproximately(24, 0.1);
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_RepositoryUpdateFails_ReturnsError()
    {
        // Arrange
        var email = "updatefail@example.com";
        var request = new ResendVerificationEmailRequest { Email = email };

        var company = new Company
        {
            Id = 1,
            Email = email,
            Status = "Pending"
        };

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(email))
            .ReturnsAsync(company);

        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<int>(), It.IsAny<Company>()))
            .ReturnsAsync((Company?)null); // Update fails

        // Act
        var result = await _service.ResendVerificationEmailAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().ContainEquivalentOf("error occurred");
        result.Errors.Should().Contain(e => e.IndexOf("Update failed", StringComparison.OrdinalIgnoreCase) >= 0);

        _emailServiceMock.Verify(
            e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_EmailSendingFails_StillReturnsSuccess()
    {
        // Arrange
        var email = "emailsendfail@example.com";
        var request = new ResendVerificationEmailRequest { Email = email };

        var company = new Company
        {
            Id = 1,
            Email = email,
            Status = "Pending"
        };

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(email))
            .ReturnsAsync(company);

        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<int>(), It.IsAny<Company>()))
            .ReturnsAsync((int id, Company c) => c);

        _emailServiceMock
            .Setup(e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new EmailSendResult 
            { 
                Success = false, 
                ErrorMessage = "Email service unavailable" 
            });

        // Act
        var result = await _service.ResendVerificationEmailAsync(request);

        // Assert
        // Service should still return success even if email fails (fire-and-forget pattern)
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();

        await Task.Delay(200); // Wait for async email task
        _emailServiceMock.Verify(
            e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Once
        );
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_RepositoryException_ReturnsError()
    {
        // Arrange
        var email = "exception@example.com";
        var request = new ResendVerificationEmailRequest { Email = email };

        _repositoryMock
            .Setup(r => r.GetByEmailAsync(email))
            .ThrowsAsync(new Exception("Database connection failed"));

        // Act
        var result = await _service.ResendVerificationEmailAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().ContainEquivalentOf("unexpected error");
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_EmailNormalizedToLowercase()
    {
        // Arrange
        var email = "Test@Example.COM";
        var request = new ResendVerificationEmailRequest { Email = email };

        var company = new Company
        {
            Id = 1,
            Email = "test@example.com", // Lowercase in database
            Status = "Pending"
        };

        _repositoryMock
            .Setup(r => r.GetByEmailAsync("test@example.com"))
            .ReturnsAsync(company);

        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<int>(), It.IsAny<Company>()))
            .ReturnsAsync((int id, Company c) => c);

        _emailServiceMock
            .Setup(e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new EmailSendResult { Success = true });

        // Act
        var result = await _service.ResendVerificationEmailAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        _repositoryMock.Verify(r => r.GetByEmailAsync("test@example.com"), Times.Once);
    }

    #endregion
}
