using MyStore.Models;

namespace MyStore.Repositories;

public interface IRoleRepository
{
    Task<List<Role>> GetForCompanyAsync(int companyId, CancellationToken cancellationToken = default);
    Task<Role?> GetByIdAsync(int id, int companyId, CancellationToken cancellationToken = default);
    Task<List<PermissionInfo>> GetAllPermissionsAsync(CancellationToken cancellationToken = default);
    Task<Role> CreateAsync(Role role, int companyId, CancellationToken cancellationToken = default);
    Task<Role?> UpdateAsync(int id, string name, string? description, int companyId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, int companyId, CancellationToken cancellationToken = default);
    Task AssignPermissionsAsync(int roleId, IEnumerable<string> permissionNames, CancellationToken cancellationToken = default);
}
