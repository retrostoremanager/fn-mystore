namespace MyStore.Services;

/// <summary>
/// Service for checking user permissions (EPIC-0-008-002 RBAC).
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Checks if the user (identified by company and email) has the specified permission.
    /// </summary>
    /// <param name="companyId">Company ID from JWT/request</param>
    /// <param name="email">User email from JWT claims</param>
    /// <param name="permission">Permission name (e.g. "inventory.view", "billing.manage")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if user has the permission, false otherwise</returns>
    Task<bool> HasPermissionAsync(int companyId, string email, string permission, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all permission names for the user. Used by frontend for UI filtering.
    /// </summary>
    Task<IReadOnlySet<string>> GetPermissionsAsync(int companyId, string email, CancellationToken cancellationToken = default);
}
