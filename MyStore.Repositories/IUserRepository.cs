using MyStore.Models;

namespace MyStore.Repositories;

/// <summary>
/// Repository for user, role, and permission queries (EPIC-0-008).
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Gets the distinct permission names for a user identified by company and email.
    /// </summary>
    Task<IReadOnlySet<string>> GetPermissionsAsync(int companyId, string email, CancellationToken cancellationToken = default);

    Task<List<User>> GetByCompanyIdAsync(int companyId, CancellationToken cancellationToken = default);
    Task<User?> GetByIdAsync(int id, int companyId, CancellationToken cancellationToken = default);
    Task<User?> GetByEmailAsync(string email, int companyId, CancellationToken cancellationToken = default);
    Task<User> CreateAsync(User user, CancellationToken cancellationToken = default);
    Task<User?> UpdateAsync(int id, User user, int companyId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, int companyId, CancellationToken cancellationToken = default);
    Task AssignRolesAsync(int userId, IEnumerable<int> roleIds, CancellationToken cancellationToken = default);
    Task<List<string>> GetRoleNamesForUserAsync(int userId, CancellationToken cancellationToken = default);
}
