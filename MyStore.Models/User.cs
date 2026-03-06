namespace MyStore.Models;

/// <summary>
/// User model (replaces Employee). Users have roles via user_role.
/// </summary>
public class User
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string UserType { get; set; } = "employee"; // owner | employee
    public string? Phone { get; set; }
    public string Status { get; set; } = "active"; // pending_invitation | active | removed
    public DateTime CreatedDate { get; set; }
    public DateTime? LastModifiedDate { get; set; }
    public List<string> Roles { get; set; } = new();
}

public class UserListResponse
{
    public List<User> Users { get; set; } = new();
}

public class CreateUserRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public List<int> RoleIds { get; set; } = new();
}

public class UpdateUserRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public List<int>? RoleIds { get; set; }
    public bool? IsActive { get; set; }
}
