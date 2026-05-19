using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Moq;
using MyStore.Functions;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Tests.Helpers;
using Xunit;

namespace MyStore.Tests.Functions;

public class RoleFunctionsTests
{
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger<RoleFunctions>> _loggerMock;
    private readonly RoleFunctions _functions;

    private const int CompanyId = 42;

    private static readonly IReadOnlyDictionary<string, string> CompanyHeaders =
        new Dictionary<string, string> { ["X-Company-Id"] = CompanyId.ToString() };

    private static readonly JsonSerializerOptions CamelCaseOptions =
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static HttpRequestData CreateRequestWithBody(FunctionContext context, object? body, IReadOnlyDictionary<string, string>? headers = null)
    {
        var req = new MockHttpRequestData(context, headers, null);
        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, CamelCaseOptions);
            req.SetBody(Encoding.UTF8.GetBytes(json));
        }
        return req;
    }

    public RoleFunctionsTests()
    {
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _loggerMock = new Mock<ILogger<RoleFunctions>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();

        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        _functions = new RoleFunctions(_roleRepositoryMock.Object, _loggerFactoryMock.Object);
    }

    private static Role MakeRole(int id, int? companyId = CompanyId, bool isSystem = false) => new Role
    {
        Id = id,
        Name = $"Role {id}",
        Description = $"Description {id}",
        CompanyId = isSystem ? null : companyId,
        Permissions = new List<string> { "users.view" }
    };

    #region GetAllRoles

    [Fact]
    public async Task GetAllRoles_Returns200WithRoleList()
    {
        var roles = new List<Role> { MakeRole(1), MakeRole(2) };

        _roleRepositoryMock
            .Setup(r => r.GetForCompanyAsync(CompanyId, default))
            .ReturnsAsync(roles);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetAllRoles(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var response = JsonSerializer.Deserialize<ApiResponse<List<Role>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        response!.Success.Should().BeTrue();
        response.Data.Should().HaveCount(2);
        response.Data![0].Id.Should().Be(1);
        response.Data[1].Id.Should().Be(2);
    }

    [Fact]
    public async Task GetAllRoles_ScopedToCompany_OnlyPassesCompanyIdToRepository()
    {
        _roleRepositoryMock
            .Setup(r => r.GetForCompanyAsync(CompanyId, default))
            .ReturnsAsync(new List<Role>());

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        await _functions.GetAllRoles(req);

        _roleRepositoryMock.Verify(r => r.GetForCompanyAsync(CompanyId, default), Times.Once);
        _roleRepositoryMock.Verify(r => r.GetForCompanyAsync(It.Is<int>(id => id != CompanyId), default), Times.Never);
    }

    [Fact]
    public async Task GetAllRoles_EmptyList_Returns200WithEmptyData()
    {
        _roleRepositoryMock
            .Setup(r => r.GetForCompanyAsync(CompanyId, default))
            .ReturnsAsync(new List<Role>());

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetAllRoles(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var response = JsonSerializer.Deserialize<ApiResponse<List<Role>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        response!.Success.Should().BeTrue();
        response.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllRoles_MissingCompanyId_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.GetAllRoles(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAllRoles_ResponseUsesCamelCase()
    {
        _roleRepositoryMock
            .Setup(r => r.GetForCompanyAsync(CompanyId, default))
            .ReturnsAsync(new List<Role> { MakeRole(1) });

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetAllRoles(req);

        var body = await TestHelpers.ReadResponseBody(result);
        body.Should().Contain("success");
        body.Should().Contain("data");
        body.Should().NotContain("\"Success\"");
        body.Should().NotContain("\"Data\"");
    }

    #endregion

    #region GetRoleById

    [Fact]
    public async Task GetRoleById_ExistingRole_Returns200WithRole()
    {
        var role = MakeRole(1);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(1, CompanyId, default))
            .ReturnsAsync(role);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetRoleById(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var response = JsonSerializer.Deserialize<ApiResponse<Role>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        response!.Success.Should().BeTrue();
        response.Data!.Id.Should().Be(1);
        response.Data.Name.Should().Be("Role 1");
    }

    [Fact]
    public async Task GetRoleById_NotFound_Returns404()
    {
        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(99, CompanyId, default))
            .ReturnsAsync((Role?)null);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetRoleById(req, 99);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await TestHelpers.ReadResponseBody(result);
        var response = JsonSerializer.Deserialize<ApiResponse<Role>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        response!.Success.Should().BeFalse();
        response.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task GetRoleById_CrossCompanyAccess_Returns404()
    {
        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(1, CompanyId, default))
            .ReturnsAsync((Role?)null);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetRoleById(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _roleRepositoryMock.Verify(r => r.GetByIdAsync(1, CompanyId, default), Times.Once);
        _roleRepositoryMock.Verify(r => r.GetByIdAsync(1, It.Is<int>(id => id != CompanyId), default), Times.Never);
    }

    [Fact]
    public async Task GetRoleById_MissingCompanyId_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.GetRoleById(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetRoleById_IncludesPermissionsInResponse()
    {
        var role = new Role
        {
            Id = 1,
            Name = "Manager",
            CompanyId = CompanyId,
            Permissions = new List<string> { "users.view", "users.manage" }
        };

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(1, CompanyId, default))
            .ReturnsAsync(role);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetRoleById(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var response = JsonSerializer.Deserialize<ApiResponse<Role>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        response!.Data!.Permissions.Should().Contain("users.view");
        response.Data.Permissions.Should().Contain("users.manage");
    }

    #endregion

    #region CreateRole

    [Fact]
    public async Task CreateRole_ValidRequest_Returns201WithCreatedRole()
    {
        var createRequest = new CreateRoleRequest
        {
            Name = "New Role",
            Description = "A new custom role",
            Permissions = new List<string> { "users.view" }
        };

        var created = new Role { Id = 10, Name = "New Role", Description = "A new custom role", CompanyId = CompanyId };
        var withPerms = new Role { Id = 10, Name = "New Role", Description = "A new custom role", CompanyId = CompanyId, Permissions = new List<string> { "users.view" } };

        _roleRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Role>(), CompanyId, default))
            .ReturnsAsync(created);

        _roleRepositoryMock
            .Setup(r => r.AssignPermissionsAsync(10, It.IsAny<IEnumerable<string>>(), default))
            .Returns(Task.CompletedTask);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(10, CompanyId, default))
            .ReturnsAsync(withPerms);

        var context = new Mock<FunctionContext>();
        var req = CreateRequestWithBody(context.Object, createRequest, CompanyHeaders);

        var result = await _functions.CreateRole(req);

        result.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await TestHelpers.ReadResponseBody(result);
        var response = JsonSerializer.Deserialize<ApiResponse<Role>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        response!.Success.Should().BeTrue();
        response.Data!.Id.Should().Be(10);
        response.Data.Name.Should().Be("New Role");
    }

    [Fact]
    public async Task CreateRole_MissingName_Returns400()
    {
        var createRequest = new CreateRoleRequest { Name = "", Description = "No name" };

        var context = new Mock<FunctionContext>();
        var req = CreateRequestWithBody(context.Object, createRequest, CompanyHeaders);

        var result = await _functions.CreateRole(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var response = JsonSerializer.Deserialize<ApiResponse<Role>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        response!.Success.Should().BeFalse();
        response.Message.Should().Contain("Name is required");
    }

    [Fact]
    public async Task CreateRole_WhitespaceName_Returns400()
    {
        var createRequest = new CreateRoleRequest { Name = "   " };

        var context = new Mock<FunctionContext>();
        var req = CreateRequestWithBody(context.Object, createRequest, CompanyHeaders);

        var result = await _functions.CreateRole(req);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var response = JsonSerializer.Deserialize<ApiResponse<Role>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        response!.Success.Should().BeFalse();
        response.Message.Should().Contain("Name is required");
    }

    [Fact]
    public async Task CreateRole_SetsCompanyIdFromRequest()
    {
        var createRequest = new CreateRoleRequest { Name = "Role", Permissions = new List<string>() };

        var created = new Role { Id = 5, Name = "Role", CompanyId = CompanyId };

        _roleRepositoryMock
            .Setup(r => r.CreateAsync(It.Is<Role>(role => role.CompanyId == CompanyId), CompanyId, default))
            .ReturnsAsync(created);

        _roleRepositoryMock
            .Setup(r => r.AssignPermissionsAsync(5, It.IsAny<IEnumerable<string>>(), default))
            .Returns(Task.CompletedTask);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(5, CompanyId, default))
            .ReturnsAsync(created);

        var context = new Mock<FunctionContext>();
        var req = CreateRequestWithBody(context.Object, createRequest, CompanyHeaders);

        await _functions.CreateRole(req);

        _roleRepositoryMock.Verify(r => r.CreateAsync(
            It.Is<Role>(role => role.CompanyId == CompanyId), CompanyId, default), Times.Once);
    }

    [Fact]
    public async Task CreateRole_AssignsPermissions()
    {
        var createRequest = new CreateRoleRequest
        {
            Name = "Role",
            Permissions = new List<string> { "users.view", "inventory.view" }
        };

        var created = new Role { Id = 5, Name = "Role", CompanyId = CompanyId };
        var withPerms = new Role { Id = 5, Name = "Role", CompanyId = CompanyId, Permissions = createRequest.Permissions };

        _roleRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Role>(), CompanyId, default))
            .ReturnsAsync(created);

        _roleRepositoryMock
            .Setup(r => r.AssignPermissionsAsync(5, It.IsAny<IEnumerable<string>>(), default))
            .Returns(Task.CompletedTask);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(5, CompanyId, default))
            .ReturnsAsync(withPerms);

        var context = new Mock<FunctionContext>();
        var req = CreateRequestWithBody(context.Object, createRequest, CompanyHeaders);

        var result = await _functions.CreateRole(req);

        _roleRepositoryMock.Verify(r => r.AssignPermissionsAsync(5,
            It.Is<IEnumerable<string>>(p => p.Contains("users.view") && p.Contains("inventory.view")),
            default), Times.Once);
    }

    [Fact]
    public async Task CreateRole_MissingCompanyId_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var req = CreateRequestWithBody(context.Object, new CreateRoleRequest { Name = "Role" });

        var result = await _functions.CreateRole(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region UpdateRole

    [Fact]
    public async Task UpdateRole_ValidRequest_Returns200WithUpdatedRole()
    {
        var existing = MakeRole(1);
        var updateRequest = new UpdateRoleRequest { Name = "Updated Name", Description = "Updated" };
        var updated = new Role { Id = 1, Name = "Updated Name", Description = "Updated", CompanyId = CompanyId };
        var final = new Role { Id = 1, Name = "Updated Name", Description = "Updated", CompanyId = CompanyId, Permissions = new List<string> { "users.view" } };

        _roleRepositoryMock.SetupSequence(r => r.GetByIdAsync(1, CompanyId, default)).ReturnsAsync(existing).ReturnsAsync(final);
        _roleRepositoryMock.Setup(r => r.UpdateAsync(1, "Updated Name", "Updated", CompanyId, default)).ReturnsAsync(updated);

        var context = new Mock<FunctionContext>();
        var req = CreateRequestWithBody(context.Object, updateRequest, CompanyHeaders);

        var result = await _functions.UpdateRole(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var response = JsonSerializer.Deserialize<ApiResponse<Role>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        response!.Success.Should().BeTrue();
        response.Data!.Id.Should().Be(1);
    }

    [Fact]
    public async Task UpdateRole_NotFound_Returns404()
    {
        _roleRepositoryMock.Setup(r => r.GetByIdAsync(99, CompanyId, default)).ReturnsAsync((Role?)null);

        var context = new Mock<FunctionContext>();
        var req = CreateRequestWithBody(context.Object, new UpdateRoleRequest { Name = "X" }, CompanyHeaders);

        var result = await _functions.UpdateRole(req, 99);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await TestHelpers.ReadResponseBody(result);
        var response = JsonSerializer.Deserialize<ApiResponse<Role>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        response!.Success.Should().BeFalse();
        response.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task UpdateRole_SystemRole_Returns400()
    {
        var systemRole = MakeRole(1, null, isSystem: true);

        _roleRepositoryMock.Setup(r => r.GetByIdAsync(1, CompanyId, default)).ReturnsAsync(systemRole);

        var context = new Mock<FunctionContext>();
        var req = CreateRequestWithBody(context.Object, new UpdateRoleRequest { Name = "Hack" }, CompanyHeaders);

        var result = await _functions.UpdateRole(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var response = JsonSerializer.Deserialize<ApiResponse<Role>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        response!.Success.Should().BeFalse();
        response.Message.Should().Contain("System roles cannot be modified");
    }

    [Fact]
    public async Task UpdateRole_UpdatesPermissionsWhenProvided()
    {
        var existing = MakeRole(1);
        var updateRequest = new UpdateRoleRequest
        {
            Name = "Role 1",
            Permissions = new List<string> { "inventory.view" }
        };
        var updated = MakeRole(1);
        var final = new Role { Id = 1, Name = "Role 1", CompanyId = CompanyId, Permissions = new List<string> { "inventory.view" } };

        _roleRepositoryMock.SetupSequence(r => r.GetByIdAsync(1, CompanyId, default))
            .ReturnsAsync(existing)
            .ReturnsAsync(final);

        _roleRepositoryMock
            .Setup(r => r.UpdateAsync(1, It.IsAny<string>(), It.IsAny<string?>(), CompanyId, default))
            .ReturnsAsync(updated);

        _roleRepositoryMock
            .Setup(r => r.AssignPermissionsAsync(1, It.IsAny<IEnumerable<string>>(), default))
            .Returns(Task.CompletedTask);

        var context = new Mock<FunctionContext>();
        var req = CreateRequestWithBody(context.Object, updateRequest, CompanyHeaders);

        await _functions.UpdateRole(req, 1);

        _roleRepositoryMock.Verify(r => r.AssignPermissionsAsync(1,
            It.Is<IEnumerable<string>>(p => p.Contains("inventory.view")), default), Times.Once);
    }

    [Fact]
    public async Task UpdateRole_MissingCompanyId_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var req = CreateRequestWithBody(context.Object, new UpdateRoleRequest { Name = "X" });

        var result = await _functions.UpdateRole(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region DeleteRole

    [Fact]
    public async Task DeleteRole_ExistingRole_Returns200()
    {
        var existing = MakeRole(1);

        _roleRepositoryMock.Setup(r => r.GetByIdAsync(1, CompanyId, default)).ReturnsAsync(existing);
        _roleRepositoryMock.Setup(r => r.DeleteAsync(1, CompanyId, default)).ReturnsAsync(true);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.DeleteRole(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var response = JsonSerializer.Deserialize<ApiResponse<object>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        response!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteRole_NotFound_Returns404()
    {
        _roleRepositoryMock.Setup(r => r.GetByIdAsync(99, CompanyId, default)).ReturnsAsync((Role?)null);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.DeleteRole(req, 99);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await TestHelpers.ReadResponseBody(result);
        var response = JsonSerializer.Deserialize<ApiResponse<object>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        response!.Success.Should().BeFalse();
        response.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task DeleteRole_SystemRole_Returns400()
    {
        var systemRole = MakeRole(1, null, isSystem: true);

        _roleRepositoryMock.Setup(r => r.GetByIdAsync(1, CompanyId, default)).ReturnsAsync(systemRole);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.DeleteRole(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var response = JsonSerializer.Deserialize<ApiResponse<object>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        response!.Success.Should().BeFalse();
        response.Message.Should().Contain("System roles cannot be deleted");
    }

    [Fact]
    public async Task DeleteRole_RoleInUse_Returns400()
    {
        var existing = MakeRole(1);

        _roleRepositoryMock.Setup(r => r.GetByIdAsync(1, CompanyId, default)).ReturnsAsync(existing);
        _roleRepositoryMock.Setup(r => r.DeleteAsync(1, CompanyId, default)).ReturnsAsync(false);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.DeleteRole(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await TestHelpers.ReadResponseBody(result);
        var response = JsonSerializer.Deserialize<ApiResponse<object>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        response!.Success.Should().BeFalse();
        response.Message.Should().Contain("Cannot delete role");
    }

    [Fact]
    public async Task DeleteRole_ScopedToCompany_OnlyDeletesOwnCompanyRole()
    {
        var existing = MakeRole(1);

        _roleRepositoryMock.Setup(r => r.GetByIdAsync(1, CompanyId, default)).ReturnsAsync(existing);
        _roleRepositoryMock.Setup(r => r.DeleteAsync(1, CompanyId, default)).ReturnsAsync(true);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        await _functions.DeleteRole(req, 1);

        _roleRepositoryMock.Verify(r => r.DeleteAsync(1, CompanyId, default), Times.Once);
        _roleRepositoryMock.Verify(r => r.DeleteAsync(1, It.Is<int>(id => id != CompanyId), default), Times.Never);
    }

    [Fact]
    public async Task DeleteRole_MissingCompanyId_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.DeleteRole(req, 1);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region GetAvailablePermissions

    [Fact]
    public async Task GetAvailablePermissions_Returns200WithPermissions()
    {
        var permissions = new List<PermissionInfo>
        {
            new PermissionInfo { Id = 1, Name = "users.view", Description = "View users" },
            new PermissionInfo { Id = 2, Name = "users.manage", Description = "Manage users" }
        };

        _roleRepositoryMock
            .Setup(r => r.GetAllPermissionsAsync(default))
            .ReturnsAsync(permissions);

        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null, CompanyHeaders);

        var result = await _functions.GetAvailablePermissions(req);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await TestHelpers.ReadResponseBody(result);
        var response = JsonSerializer.Deserialize<ApiResponse<List<PermissionInfo>>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        response!.Success.Should().BeTrue();
        response.Data.Should().HaveCount(2);
        response.Data![0].Name.Should().Be("users.view");
    }

    [Fact]
    public async Task GetAvailablePermissions_MissingCompanyId_Returns401()
    {
        var context = new Mock<FunctionContext>();
        var req = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = await _functions.GetAvailablePermissions(req);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion
}
