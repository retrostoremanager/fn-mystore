using MyStore.Models;

namespace MyStore.Repositories;

/// <summary>
/// Repository for location (store location) data (EPIC-0-007).
/// </summary>
public interface ILocationRepository
{
    Task<IEnumerable<Location>> GetByCompanyIdAsync(int companyId);
    Task<Location?> GetByIdAsync(int id, int companyId);
    Task<int> CreateAsync(Location location);
    Task<bool> UpdateAsync(Location location);
    Task<bool> DeleteAsync(int id, int companyId);
}
