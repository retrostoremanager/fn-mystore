using Dapper;
using MyStore.Models;
using Npgsql;

namespace MyStore.Repositories;

public class RoleRepository : IRoleRepository
{
    private readonly string _connectionString;

    static RoleRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public RoleRepository()
    {
        _connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("PostgresConnectionString")
            ?? throw new InvalidOperationException("Connection string environment variable is not set");
    }

    public async Task<List<Role>> GetForCompanyAsync(int companyId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, name, description, company_id
            FROM role
            WHERE company_id IS NULL OR company_id = @companyId
            ORDER BY company_id NULLS FIRST, name
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var roles = (await connection.QueryAsync<Role>(new CommandDefinition(sql, new { companyId }, cancellationToken: cancellationToken))).ToList();
        foreach (var role in roles)
        {
            role.Permissions = (await GetPermissionsForRoleAsync(role.Id, cancellationToken)).ToList();
        }
        return roles;
    }

    public async Task<Role?> GetByIdAsync(int id, int companyId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, name, description, company_id
            FROM role
            WHERE id = @id AND (company_id IS NULL OR company_id = @companyId)
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var role = await connection.QueryFirstOrDefaultAsync<Role>(new CommandDefinition(sql, new { id, companyId }, cancellationToken: cancellationToken));
        if (role != null)
        {
            role.Permissions = (await GetPermissionsForRoleAsync(role.Id, cancellationToken)).ToList();
        }
        return role;
    }

    private async Task<IEnumerable<string>> GetPermissionsForRoleAsync(int roleId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT p.name
            FROM role_permission rp
            JOIN permission p ON p.id = rp.permission_id
            WHERE rp.role_id = @roleId
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryAsync<string>(new CommandDefinition(sql, new { roleId }, cancellationToken: cancellationToken));
    }
}
