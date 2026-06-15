using MyStore.Repositories;

namespace MyStore.Services;

public class PermissionService : IPermissionService
{
    private readonly IUserRepository _userRepository;

    public PermissionService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<bool> HasPermissionAsync(int companyId, string email, string permission, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(permission))
            return false;

        var permissions = await _userRepository.GetPermissionsAsync(companyId, email, cancellationToken);
        return permissions.Contains(permission);
    }

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(int companyId, string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return new HashSet<string>();

        return await _userRepository.GetPermissionsAsync(companyId, email, cancellationToken);
    }
}
