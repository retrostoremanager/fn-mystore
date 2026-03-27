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
    public string UserType { get; set; } = "employee"; // owner | employee | customer
    public string? Phone { get; set; }
    public string Status { get; set; } = "active"; // pending_invitation | invitation_expired | active | removed
    public DateTime CreatedDate { get; set; }
    public DateTime? LastModifiedDate { get; set; }
    /// <summary>Token for invite/set-password flow. Used when creating new employee users.</summary>
    public string? PasswordInviteToken { get; set; }
    /// <summary>Expiration for invite token. Typically 7 days from creation.</summary>
    public DateTime? PasswordInviteTokenExpires { get; set; }
    /// <summary>BCrypt hash for employee login. Not returned in API responses; used internally for auth.</summary>
    public string? PasswordHash { get; set; }
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

/// <summary>
/// Request to set password from user invite (employee onboarding).
/// </summary>
public class SetPasswordFromInviteRequest
{
    public string Token { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Response after successfully setting password from invite. Includes slug for redirect to company login.
/// </summary>
public class SetPasswordFromInviteResponse
{
    public bool Success { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    /// <summary>employee | customer — drives redirect after set-password (staff login vs customer portal).</summary>
    public string UserType { get; set; } = "employee";
}
