using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MyStore.Functions;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Services;
using MyStore.Tests.Helpers;
using Xunit;

namespace MyStore.Tests.Functions;

public class TrialNotificationFunctionsTests
{
    private readonly Mock<ICompanyRepository> _companyRepositoryMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Mock<ILogger<TrialNotificationFunctions>> _loggerMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly TrialNotificationFunctions _functions;

    public TrialNotificationFunctionsTests()
    {
        _companyRepositoryMock = new Mock<ICompanyRepository>();
        _emailServiceMock = new Mock<IEmailService>();
        _loggerMock = new Mock<ILogger<TrialNotificationFunctions>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        _functions = new TrialNotificationFunctions(
            _companyRepositoryMock.Object,
            _emailServiceMock.Object,
            _loggerFactoryMock.Object);
    }

    [Fact]
    public async Task Run_NoExpiringTrials_DoesNotSendEmails()
    {
        // Arrange
        _companyRepositoryMock
            .Setup(r => r.GetExpiringTrialsAsync(It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<Company>());

        var timerInfo = new Microsoft.Azure.Functions.Worker.TimerInfo { ScheduleStatus = null, IsPastDue = false };

        // Act
        await _functions.Run(timerInfo);

        // Assert
        _emailServiceMock.Verify(
            s => s.SendTrialExpirationEmailAsync(It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
        _companyRepositoryMock.Verify(
            r => r.MarkTrialNotificationSentAsync(It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_CompanyExpiringIn7Days_SendsEmailAndMarksSent()
    {
        // Arrange
        var company = new Company
        {
            Id = 1,
            Email = "owner@store.com",
            Status = "Active",
            SubscriptionTier = "Trial",
            TrialStartDate = DateTime.UtcNow.AddDays(-23),
            TrialEndDate = DateTime.UtcNow.AddDays(7)
        };

        _companyRepositoryMock
            .Setup(r => r.GetExpiringTrialsAsync(7))
            .ReturnsAsync(new[] { company });
        _companyRepositoryMock
            .Setup(r => r.GetExpiringTrialsAsync(3))
            .ReturnsAsync(Array.Empty<Company>());
        _companyRepositoryMock
            .Setup(r => r.GetExpiringTrialsAsync(1))
            .ReturnsAsync(Array.Empty<Company>());

        _emailServiceMock
            .Setup(s => s.SendTrialExpirationEmailAsync("owner@store.com", 7))
            .ReturnsAsync(new EmailSendResult { Success = true });

        var timerInfo = new Microsoft.Azure.Functions.Worker.TimerInfo { ScheduleStatus = null, IsPastDue = false };

        // Act
        await _functions.Run(timerInfo);

        // Assert
        _emailServiceMock.Verify(
            s => s.SendTrialExpirationEmailAsync("owner@store.com", 7),
            Times.Once);
        _companyRepositoryMock.Verify(
            r => r.MarkTrialNotificationSentAsync(1, 7),
            Times.Once);
    }

    [Fact]
    public async Task Run_EmailSendFails_DoesNotMarkAsSent()
    {
        // Arrange
        var company = new Company
        {
            Id = 2,
            Email = "failed@store.com",
            Status = "Active",
            SubscriptionTier = "Trial",
            TrialStartDate = DateTime.UtcNow.AddDays(-23),
            TrialEndDate = DateTime.UtcNow.AddDays(7)
        };

        _companyRepositoryMock
            .Setup(r => r.GetExpiringTrialsAsync(7))
            .ReturnsAsync(new[] { company });
        _companyRepositoryMock
            .Setup(r => r.GetExpiringTrialsAsync(3))
            .ReturnsAsync(Array.Empty<Company>());
        _companyRepositoryMock
            .Setup(r => r.GetExpiringTrialsAsync(1))
            .ReturnsAsync(Array.Empty<Company>());

        _emailServiceMock
            .Setup(s => s.SendTrialExpirationEmailAsync("failed@store.com", 7))
            .ReturnsAsync(new EmailSendResult { Success = false, ErrorMessage = "SMTP error" });

        var timerInfo = new Microsoft.Azure.Functions.Worker.TimerInfo { ScheduleStatus = null, IsPastDue = false };

        // Act
        await _functions.Run(timerInfo);

        // Assert
        _emailServiceMock.Verify(
            s => s.SendTrialExpirationEmailAsync("failed@store.com", 7),
            Times.Once);
        _companyRepositoryMock.Verify(
            r => r.MarkTrialNotificationSentAsync(It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }
}
