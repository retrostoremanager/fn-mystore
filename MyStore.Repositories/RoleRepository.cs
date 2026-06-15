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

    public async Task<List<PermissionInfo>> GetAllPermissionsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT id, name, description FROM permission ORDER BY name";

        await using var connection = new NpgsqlConnection(_connectionString);
        var list = await connection.QueryAsync<PermissionInfo>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return list.ToList();
    }

    public async Task<Role> CreateAsync(Role role, int companyId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO role (name, description, company_id)
            VALUES (@name, @description, @companyId)
            RETURNING id, name, description, company_id
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var created = await connection.QuerySingleAsync<Role>(new CommandDefinition(sql, new
        {
            role.Name,
            role.Description,
            companyId
        }, cancellationToken: cancellationToken));
        return created;
    }

    public async Task<Role?> UpdateAsync(int id, string name, string? description, int companyId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE role
            SET name = @name, description = @description, last_modified_date = NOW()
            WHERE id = @id AND company_id = @companyId
            RETURNING id, name, description, company_id
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<Role>(new CommandDefinition(sql, new { id, name, description, companyId }, cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteAsync(int id, int companyId, CancellationToken cancellationToken = default)
    {
        const string checkUsersSql = "SELECT COUNT(1) FROM user_role WHERE role_id = @id";
        const string deleteSql = "DELETE FROM role WHERE id = @id AND company_id = @companyId";

        await using var connection = new NpgsqlConnection(_connectionString);
        var userCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(checkUsersSql, new { id }, cancellationToken: cancellationToken));
        if (userCount > 0)
            return false;

        var rows = await connection.ExecuteAsync(new CommandDefinition(deleteSql, new { id, companyId }, cancellationToken: cancellationToken));
        return rows > 0;
    }

    public async Task AssignPermissionsAsync(int roleId, IEnumerable<string> permissionNames, CancellationToken cancellationToken = default)
    {
        var names = permissionNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToArray();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM role_permission WHERE role_id = @roleId", new { roleId }, cancellationToken: cancellationToken));

        if (names.Length == 0)
            return;

        const string insertSql = """
            INSERT INTO role_permission (role_id, permission_id)
            SELECT @roleId, p.id FROM permission p WHERE p.name = ANY(@names)
            ON CONFLICT (role_id, permission_id) DO NOTHING
            """;
        await connection.ExecuteAsync(new CommandDefinition(insertSql, new { roleId, names }, cancellationToken: cancellationToken));
    }
}
