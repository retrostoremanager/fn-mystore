using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MyStore.Functions;
using MyStore.Services;
using Xunit;

namespace MyStore.Tests.Functions;

public class TrialConversionFunctionsTests
{
    private readonly Mock<ITrialConversionService> _trialConversionServiceMock;
    private readonly Mock<ILogger<TrialConversionFunctions>> _loggerMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly TrialConversionFunctions _functions;

    public TrialConversionFunctionsTests()
    {
        _trialConversionServiceMock = new Mock<ITrialConversionService>();
        _loggerMock = new Mock<ILogger<TrialConversionFunctions>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        _functions = new TrialConversionFunctions(
            _trialConversionServiceMock.Object,
            _loggerFactoryMock.Object);
    }

    [Fact]
    public async Task Run_CallsProcessExpiredTrialsAsync()
    {
        _trialConversionServiceMock
            .Setup(s => s.ProcessExpiredTrialsAsync())
            .ReturnsAsync(2);

        var timerInfo = new Microsoft.Azure.Functions.Worker.TimerInfo { ScheduleStatus = null, IsPastDue = false };

        await _functions.Run(timerInfo);

        _trialConversionServiceMock.Verify(s => s.ProcessExpiredTrialsAsync(), Times.Once);
    }

    [Fact]
    public async Task Run_ReturnsZero_DoesNotThrow()
    {
        _trialConversionServiceMock
            .Setup(s => s.ProcessExpiredTrialsAsync())
            .ReturnsAsync(0);

        var timerInfo = new Microsoft.Azure.Functions.Worker.TimerInfo { ScheduleStatus = null, IsPastDue = false };

        var act = () => _functions.Run(timerInfo);
        await act.Should().NotThrowAsync();
    }
}
