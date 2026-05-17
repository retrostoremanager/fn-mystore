using System.Collections.Specialized;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Moq;
using MyStore.Functions;
using MyStore.Models;
using MyStore.Services;
using MyStore.Tests.Helpers;
using Xunit;

namespace MyStore.Tests.Functions;

public class UserFunctionsTests
{
    private readonly Mock<IUserService> _userServiceMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger<UserFunctions>> _loggerMock;
    private readonly UserFunctions _functions;

    private static readonly IReadOnlyDictionary<string, string> CompanyHeaders =
        new Dictionary<string, string> { { "X-Company-Id", "1" } };

    public UserFunctionsTests()
    {
        _userServiceMock = new Mock<IUserService>();
        _loggerMock = new Mock<ILogger<UserFunctions>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();

        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        _functions = new UserFunctions(_userServiceMock.Object, _loggerFactoryMock.Object);
    }

    private static User CreateSampleUser(int id = 1) => new User
    {
        Id = id,
        CompanyId = 1,
        Email = "employee@example.com",
        FirstName = "Jane",
        LastName = "Doe",
        UserType = "employee",
        Status = "active",
        CreatedDate = DateTime.UtcNow
    };

    #region GetAllUsers Tests

    [Fact]
    public async Task GetAllUsers_ValidCompanyId_Returns200WithUserList()
    {
        var users = new List<User> { CreateSampleUser(1), CreateSampleUser(2) };
        var apiResponse = ApiResponse<List<User>>.SuccessResponse(users, "Users retrieved successfully");

        _userServiceMock
            .Setup(s => s.GetAllUsersAsync(1))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetAllUsers(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<User>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllUsers_MissingCompanyId_Returns401Unauthorized()
    {
        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.GetAllUsers(request);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<User>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
    }

    #endregion

    #region GetUserById Tests

    [Fact]
    public async Task GetUserById_UserFound_Returns200WithUser()
    {
        var user = CreateSampleUser(42);
        var apiResponse = ApiResponse<User>.SuccessResponse(user, "User retrieved");

        _userServiceMock
            .Setup(s => s.GetUserByIdAsync(42, 1))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetUserById(request, 42);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<User>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.Id.Should().Be(42);
    }

    [Fact]
    public async Task GetUserById_UserNotFound_Returns400()
    {
        var apiResponse = ApiResponse<User>.ErrorResponse("User not found");

        _userServiceMock
            .Setup(s => s.GetUserByIdAsync(999, 1))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetUserById(request, 999);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<User>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task GetUserById_MissingCompanyId_Returns401Unauthorized()
    {
        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.GetUserById(request, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region CreateUser Tests

    [Fact]
    public async Task CreateUser_ValidRequest_Returns201Created()
    {
        var createRequest = new CreateUserRequest
        {
            FirstName = "John",
            LastName = "Smith",
            Email = "john.smith@example.com",
            RoleIds = new List<int> { 1 }
        };

        var createdUser = new User
        {
            Id = 10,
            CompanyId = 1,
            Email = "john.smith@example.com",
            FirstName = "John",
            LastName = "Smith",
            Status = "pending_invitation"
        };

        var apiResponse = ApiResponse<User>.SuccessResponse(createdUser, "User created and invite sent");

        _userServiceMock
            .Setup(s => s.CreateUserAsync(It.IsAny<CreateUserRequest>(), 1))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, createRequest, CompanyHeaders);

        var result = await _functions.CreateUser(request);

        result.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<User>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.Data!.Id.Should().Be(10);
    }

    [Fact]
    public async Task CreateUser_InvalidRequestBody_Returns400BadRequest()
    {
        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("null",
            new Dictionary<string, string> { { "X-Company-Id", "1" } });

        var result = await _functions.CreateUser(request);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<User>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("Invalid request body");
    }

    [Fact]
    public async Task CreateUser_DuplicateEmail_Returns400BadRequest()
    {
        var createRequest = new CreateUserRequest
        {
            FirstName = "Jane",
            LastName = "Doe",
            Email = "existing@example.com",
            RoleIds = new List<int> { 1 }
        };

        var apiResponse = ApiResponse<User>.ErrorResponse("A user with this email already exists");

        _userServiceMock
            .Setup(s => s.CreateUserAsync(It.IsAny<CreateUserRequest>(), 1))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, createRequest, CompanyHeaders);

        var result = await _functions.CreateUser(request);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<User>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task CreateUser_MissingCompanyId_Returns401Unauthorized()
    {
        var createRequest = new CreateUserRequest
        {
            FirstName = "John",
            LastName = "Smith",
            Email = "john.smith@example.com"
        };

        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, createRequest);

        var result = await _functions.CreateUser(request);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region ResendInvite Tests

    [Fact]
    public async Task ResendInvite_Success_Returns200OK()
    {
        var apiResponse = ApiResponse<bool>.SuccessResponse(true, "Invite resent successfully");

        _userServiceMock
            .Setup(s => s.ResendInviteAsync(5, 1))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.ResendInvite(request, 5);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<bool>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ResendInvite_UserNotFound_Returns400()
    {
        var apiResponse = ApiResponse<bool>.ErrorResponse("User not found");

        _userServiceMock
            .Setup(s => s.ResendInviteAsync(999, 1))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.ResendInvite(request, 999);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<bool>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ResendInvite_MissingCompanyId_Returns401Unauthorized()
    {
        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.ResendInvite(request, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region UpdateUser Tests

    [Fact]
    public async Task UpdateUser_ValidRequest_Returns200OK()
    {
        var updateRequest = new UpdateUserRequest
        {
            FirstName = "Updated",
            LastName = "Name"
        };

        var updatedUser = CreateSampleUser(3);
        updatedUser.FirstName = "Updated";
        updatedUser.LastName = "Name";

        var apiResponse = ApiResponse<User>.SuccessResponse(updatedUser, "User updated successfully");

        _userServiceMock
            .Setup(s => s.UpdateUserAsync(3, It.IsAny<UpdateUserRequest>(), 1))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, updateRequest, CompanyHeaders);

        var result = await _functions.UpdateUser(request, 3);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<User>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateUser_InvalidRequestBody_Returns400BadRequest()
    {
        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestDataWithRawBody("null",
            new Dictionary<string, string> { { "X-Company-Id", "1" } });

        var result = await _functions.UpdateUser(request, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<User>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("Invalid request body");
    }

    [Fact]
    public async Task UpdateUser_MissingCompanyId_Returns401Unauthorized()
    {
        var updateRequest = new UpdateUserRequest { FirstName = "Updated" };

        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, updateRequest);

        var result = await _functions.UpdateUser(request, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region DeleteUser Tests

    [Fact]
    public async Task DeleteUser_ValidRequest_Returns200OK()
    {
        var apiResponse = ApiResponse<bool>.SuccessResponse(true, "User deleted successfully");

        _userServiceMock
            .Setup(s => s.DeleteUserAsync(7, 1))
            .ReturnsAsync(apiResponse);

        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.DeleteUser(request, 7);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<bool>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteUser_MissingCompanyId_Returns401Unauthorized()
    {
        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.DeleteUser(request, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region SearchUsers Tests

    [Fact]
    public async Task SearchUsers_ValidQuery_Returns200WithResults()
    {
        var users = new List<User> { CreateSampleUser(1) };
        var apiResponse = ApiResponse<List<User>>.SuccessResponse(users, "Search results");

        _userServiceMock
            .Setup(s => s.SearchUsersAsync("jane", 1))
            .ReturnsAsync(apiResponse);

        var query = new NameValueCollection { { "q", "jane" } };
        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders, query);

        var result = await _functions.SearchUsers(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<User>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().HaveCount(1);
    }

    [Fact]
    public async Task SearchUsers_MissingCompanyId_Returns401Unauthorized()
    {
        var query = new NameValueCollection { { "q", "jane" } };
        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null, null, query);

        var result = await _functions.SearchUsers(request);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SearchUsers_MissingQueryParam_Returns400BadRequest()
    {
        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.SearchUsers(request);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<User>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("'q'");
    }

    #endregion
}
