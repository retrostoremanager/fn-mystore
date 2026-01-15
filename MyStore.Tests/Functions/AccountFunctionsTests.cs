using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Moq;
using MyStore.Functions;
using MyStore.Models;
using MyStore.Services;
using MyStore.Tests.Helpers;
using Xunit;

namespace MyStore.Tests.Functions;

public class AccountFunctionsTests
{
    private readonly Mock<ICompanyService> _serviceMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger<AccountFunctions>> _loggerMock;
    private readonly AccountFunctions _functions;

    public AccountFunctionsTests()
    {
        _serviceMock = new Mock<ICompanyService>();
        _loggerMock = new Mock<ILogger<AccountFunctions>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        
        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);
        
        _functions = new AccountFunctions(_serviceMock.Object, _loggerFactoryMock.Object);
    }

    /// <summary>
    /// Helper method to create a valid RegisterAccountRequest for testing.
    /// </summary>
    private static RegisterAccountRequest CreateValidRequest(string? email = null, string? password = null, string? companyName = null, string? subscriptionTier = null)
    {
        return new RegisterAccountRequest
        {
            Email = email ?? "test@example.com",
            Password = password ?? "ValidPass123",
            CompanyName = companyName ?? "Test Company",
            SubscriptionTier = subscriptionTier ?? "Trial"
        };
    }

    [Fact]
    public async Task RegisterAccount_ValidRequest_Returns201Created()
    {
        // Arrange
        var request = CreateValidRequest();

        var response = new RegisterAccountResponse
        {
            Id = 1,
            Email = "test@example.com",
            Status = "Pending",
            TrialStartDate = DateTime.UtcNow,
            TrialEndDate = DateTime.UtcNow.AddDays(30),
            SubscriptionTier = "Trial"
        };

        var apiResponse = ApiResponse<RegisterAccountResponse>.SuccessResponse(response, "Account registered successfully");

        _serviceMock
            .Setup(s => s.RegisterAccountAsync(It.IsAny<RegisterAccountRequest>()))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequestData = TestHelpers.CreateHttpRequestData(context.Object, request);

        // Act
        var result = await _functions.RegisterAccount(httpRequestData);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserializedResponse = JsonSerializer.Deserialize<ApiResponse<RegisterAccountResponse>>(
            responseBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserializedResponse.Should().NotBeNull();
        deserializedResponse!.Success.Should().BeTrue();
        deserializedResponse.Data.Should().NotBeNull();
        deserializedResponse.Data!.Id.Should().Be(1);
        deserializedResponse.Data.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task RegisterAccount_DuplicateEmail_Returns409Conflict()
    {
        // Arrange
        var request = CreateValidRequest(email: "existing@example.com");

        var apiResponse = ApiResponse<RegisterAccountResponse>.ErrorResponse(
            "An account with this email already exists");

        _serviceMock
            .Setup(s => s.RegisterAccountAsync(It.IsAny<RegisterAccountRequest>()))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequestData = TestHelpers.CreateHttpRequestData(context.Object, request);

        // Act
        var result = await _functions.RegisterAccount(httpRequestData);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
        
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserializedResponse = JsonSerializer.Deserialize<ApiResponse<RegisterAccountResponse>>(
            responseBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserializedResponse.Should().NotBeNull();
        deserializedResponse!.Success.Should().BeFalse();
        deserializedResponse.Message.Should().Contain("already exists", because: "Duplicate email should return 409");
    }

    [Fact]
    public async Task RegisterAccount_InvalidRequestBody_Returns400BadRequest()
    {
        // Arrange
        var context = new Mock<FunctionContext>();
        var httpRequestData = TestHelpers.CreateHttpRequestData(context.Object, "invalid json");

        // Act
        var result = await _functions.RegisterAccount(httpRequestData);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserializedResponse = JsonSerializer.Deserialize<ApiResponse<RegisterAccountResponse>>(
            responseBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserializedResponse.Should().NotBeNull();
        deserializedResponse!.Success.Should().BeFalse();
        deserializedResponse.Message.Should().Contain("Invalid request body", because: "Invalid JSON should return error");
    }

    [Fact]
    public async Task RegisterAccount_ServiceValidationError_Returns400BadRequest()
    {
        // Arrange
        var request = CreateValidRequest(email: "invalid-email");

        var apiResponse = ApiResponse<RegisterAccountResponse>.ErrorResponse("Invalid email format");

        _serviceMock
            .Setup(s => s.RegisterAccountAsync(It.IsAny<RegisterAccountRequest>()))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequestData = TestHelpers.CreateHttpRequestData(context.Object, request);

        // Act
        var result = await _functions.RegisterAccount(httpRequestData);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserializedResponse = JsonSerializer.Deserialize<ApiResponse<RegisterAccountResponse>>(
            responseBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserializedResponse.Should().NotBeNull();
        deserializedResponse!.Success.Should().BeFalse();
        deserializedResponse.Message.Should().Contain("Invalid email format", because: "Invalid email should return error");
    }

    [Fact]
    public async Task RegisterAccount_ServiceException_Returns500InternalServerError()
    {
        // Arrange
        var request = CreateValidRequest();

        _serviceMock
            .Setup(s => s.RegisterAccountAsync(It.IsAny<RegisterAccountRequest>()))
            .ThrowsAsync(new Exception("Database connection failed"));

        var context = new Mock<FunctionContext>();
        var httpRequestData = TestHelpers.CreateHttpRequestData(context.Object, request);

        // Act
        var result = await _functions.RegisterAccount(httpRequestData);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        
        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserializedResponse = JsonSerializer.Deserialize<ApiResponse<RegisterAccountResponse>>(
            responseBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserializedResponse.Should().NotBeNull();
        deserializedResponse!.Success.Should().BeFalse();
        deserializedResponse.Message.Should().Contain("error occurred", because: "Exception should return error message");
    }

    [Fact]
    public async Task RegisterAccount_ResponseHasCorrectContentType()
    {
        // Arrange
        var request = CreateValidRequest();

        var response = new RegisterAccountResponse
        {
            Id = 1,
            Email = "test@example.com",
            Status = "Pending",
            TrialStartDate = DateTime.UtcNow,
            TrialEndDate = DateTime.UtcNow.AddDays(30),
            SubscriptionTier = "Trial"
        };

        var apiResponse = ApiResponse<RegisterAccountResponse>.SuccessResponse(response, "Account registered successfully");

        _serviceMock
            .Setup(s => s.RegisterAccountAsync(It.IsAny<RegisterAccountRequest>()))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequestData = TestHelpers.CreateHttpRequestData(context.Object, request);

        // Act
        var result = await _functions.RegisterAccount(httpRequestData);

        // Assert
        result.Headers.Should().ContainKey("Content-Type");
        result.Headers.GetValues("Content-Type").Should().Contain("application/json; charset=utf-8");
    }

    [Fact]
    public async Task RegisterAccount_ResponseUsesCamelCase()
    {
        // Arrange
        var request = CreateValidRequest();

        var response = new RegisterAccountResponse
        {
            Id = 1,
            Email = "test@example.com",
            Status = "Pending",
            TrialStartDate = DateTime.UtcNow,
            TrialEndDate = DateTime.UtcNow.AddDays(30),
            SubscriptionTier = "Trial"
        };

        var apiResponse = ApiResponse<RegisterAccountResponse>.SuccessResponse(response, "Account registered successfully");

        _serviceMock
            .Setup(s => s.RegisterAccountAsync(It.IsAny<RegisterAccountRequest>()))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequestData = TestHelpers.CreateHttpRequestData(context.Object, request);

        // Act
        var result = await _functions.RegisterAccount(httpRequestData);

        // Assert
        var responseBody = await TestHelpers.ReadResponseBody(result);
        responseBody.Should().Contain("success"); // camelCase property
        responseBody.Should().Contain("data"); // camelCase property
        responseBody.Should().NotContain("Success"); // Not PascalCase
        responseBody.Should().NotContain("Data"); // Not PascalCase
    }

    [Fact]
    public async Task RegisterAccount_LogsInformation()
    {
        // Arrange
        var request = CreateValidRequest();

        var response = new RegisterAccountResponse
        {
            Id = 1,
            Email = "test@example.com",
            Status = "Pending",
            TrialStartDate = DateTime.UtcNow,
            TrialEndDate = DateTime.UtcNow.AddDays(30),
            SubscriptionTier = "Trial"
        };

        var apiResponse = ApiResponse<RegisterAccountResponse>.SuccessResponse(response, "Account registered successfully");

        _serviceMock
            .Setup(s => s.RegisterAccountAsync(It.IsAny<RegisterAccountRequest>()))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var httpRequestData = TestHelpers.CreateHttpRequestData(context.Object, request);

        // Act
        await _functions.RegisterAccount(httpRequestData);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Registering new account")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RegisterAccount_ServiceException_LogsError()
    {
        // Arrange
        var request = CreateValidRequest();

        var exception = new Exception("Database connection failed");

        _serviceMock
            .Setup(s => s.RegisterAccountAsync(It.IsAny<RegisterAccountRequest>()))
            .ThrowsAsync(exception);

        var context = new Mock<FunctionContext>();
        var httpRequestData = TestHelpers.CreateHttpRequestData(context.Object, request);

        // Act
        await _functions.RegisterAccount(httpRequestData);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error registering account")),
                It.Is<Exception>(e => e == exception),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

}
