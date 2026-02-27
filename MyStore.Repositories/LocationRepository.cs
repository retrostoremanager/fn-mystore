using Dapper;
using Npgsql;
using MyStore.Models;

namespace MyStore.Repositories;

public class LocationRepository : ILocationRepository
{
    private readonly string _connectionString;

    static LocationRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public LocationRepository()
    {
        _connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("PostgresConnectionString")
            ?? throw new InvalidOperationException("Connection string environment variable is not set");
    }

    public async Task<IEnumerable<Location>> GetByCompanyIdAsync(int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryAsync<Location>(
            "SELECT * FROM location_get_by_company_id(@p_company_id)",
            new { p_company_id = companyId });
    }

    public async Task<Location?> GetByIdAsync(int id, int companyId)
    {
        var locations = await GetByCompanyIdAsync(companyId);
        return locations.FirstOrDefault(l => l.Id == id);
    }

    public async Task<int> CreateAsync(Location location)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleAsync<int>(
            "SELECT location_create(@p_company_id, @p_name, @p_address, @p_city, @p_state, @p_zip_code, @p_phone, @p_timezone, @p_is_primary)",
            new
            {
                p_company_id = location.CompanyId,
                p_name = location.Name,
                p_address = location.Address,
                p_city = location.City,
                p_state = location.State,
                p_zip_code = location.ZipCode,
                p_phone = location.Phone,
                p_timezone = location.Timezone,
                p_is_primary = location.IsPrimary
            });
    }

    public async Task<bool> UpdateAsync(Location location)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var rowsAffected = await connection.QuerySingleAsync<int>(
            "SELECT location_update(@p_id, @p_company_id, @p_name, @p_address, @p_city, @p_state, @p_zip_code, @p_phone, @p_timezone, @p_is_primary)",
            new
            {
                p_id = location.Id,
                p_company_id = location.CompanyId,
                p_name = location.Name,
                p_address = location.Address,
                p_city = location.City,
                p_state = location.State,
                p_zip_code = location.ZipCode,
                p_phone = location.Phone,
                p_timezone = location.Timezone,
                p_is_primary = location.IsPrimary
            });
        return rowsAffected > 0;
    }

    public async Task<bool> DeleteAsync(int id, int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var rowsAffected = await connection.QuerySingleAsync<int>(
            "SELECT location_delete(@p_id, @p_company_id)",
            new { p_id = id, p_company_id = companyId });
        return rowsAffected > 0;
    }
}
