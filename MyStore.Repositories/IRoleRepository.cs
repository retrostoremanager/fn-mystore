using MyStore.Models;

namespace MyStore.Repositories;

public interface IRoleRepository
{
    Task<List<Role>> GetForCompanyAsync(int companyId, CancellationToken cancellationToken = default);
    Task<Role?> GetByIdAsync(int id, int companyId, CancellationToken cancellationToken = default);
}
