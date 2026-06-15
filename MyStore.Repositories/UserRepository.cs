using Dapper;
using MyStore.Models;
using Npgsql;

namespace MyStore.Repositories;

public class UserRepository : IUserRepository
{
    private readonly string _connectionString;

    static UserRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public UserRepository()
    {
        _connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("PostgresConnectionString")
            ?? throw new InvalidOperationException("Connection string environment variable is not set");
    }

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(int companyId, string email, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT DISTINCT p.name
            FROM "user" u
            JOIN user_role ur ON ur.user_id = u.id
            JOIN role r ON r.id = ur.role_id
              AND (r.company_id IS NULL OR r.company_id = u.company_id)
            JOIN role_permission rp ON rp.role_id = r.id
            JOIN permission p ON p.id = rp.permission_id
            WHERE u.company_id = @companyId
              AND u.email = @email
              AND u.status = 'active'
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var names = await connection.QueryAsync<string>(new CommandDefinition(sql, new { companyId, email }, cancellationToken: cancellationToken));
        return names.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<List<User>> GetByCompanyIdAsync(int companyId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, company_id, email, first_name, last_name, user_type, phone, status, created_date, last_modified_date
            FROM "user"
            WHERE company_id = @companyId
              AND LOWER(COALESCE(user_type, 'employee')) <> 'customer'
            ORDER BY last_name, first_name
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var users = (await connection.QueryAsync<User>(new CommandDefinition(sql, new { companyId }, cancellationToken: cancellationToken))).ToList();
        foreach (var user in users)
        {
            user.Roles = (await GetRoleNamesForUserAsync(user.Id, cancellationToken)).ToList();
        }
        return users;
    }

    public async Task<User?> GetByIdAsync(int id, int companyId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, company_id, email, first_name, last_name, user_type, phone, status, created_date, last_modified_date
            FROM "user"
            WHERE id = @id AND company_id = @companyId
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var user = await connection.QueryFirstOrDefaultAsync<User>(new CommandDefinition(sql, new { id, companyId }, cancellationToken: cancellationToken));
        if (user != null)
        {
            user.Roles = (await GetRoleNamesForUserAsync(user.Id, cancellationToken)).ToList();
        }
        return user;
    }

    public async Task<User?> GetByEmailAsync(string email, int companyId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, company_id, email, first_name, last_name, user_type, phone, status, created_date, last_modified_date
            FROM "user"
            WHERE email = @email AND company_id = @companyId
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<User>(new CommandDefinition(sql, new { email, companyId }, cancellationToken: cancellationToken));
    }

    public async Task<User?> GetByEmailWithPasswordAsync(string email, int companyId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, company_id, email, first_name, last_name, user_type, phone, status, created_date, last_modified_date, password_hash
            FROM "user"
            WHERE email = @email AND company_id = @companyId AND status = 'active'
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<User>(new CommandDefinition(sql, new { email, companyId }, cancellationToken: cancellationToken));
    }

    public async Task<User?> GetByInviteTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, company_id, email, first_name, last_name, user_type, phone, status, created_date, last_modified_date
            FROM "user"
            WHERE password_invite_token = @token
              AND password_invite_token_expires > NOW()
              AND status = 'pending_invitation'
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<User>(new CommandDefinition(sql, new { token }, cancellationToken: cancellationToken));
    }

    public async Task<User> CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO "user" (company_id, email, first_name, last_name, user_type, phone, status, password_invite_token, password_invite_token_expires)
            VALUES (@companyId, @email, @firstName, @lastName, @userType, @phone, @status, @passwordInviteToken, @passwordInviteTokenExpires)
            RETURNING id, company_id, email, first_name, last_name, user_type, phone, status, created_date, last_modified_date
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var created = await connection.QuerySingleAsync<User>(new CommandDefinition(sql, new
        {
            user.CompanyId,
            user.Email,
            user.FirstName,
            user.LastName,
            user.UserType,
            user.Phone,
            user.Status,
            user.PasswordInviteToken,
            user.PasswordInviteTokenExpires
        }, cancellationToken: cancellationToken));
        return created;
    }

    public async Task<bool> UpdatePasswordFromInviteAsync(int userId, string passwordHash, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE "user"
            SET password_hash = @passwordHash,
                password_invite_token = NULL,
                password_invite_token_expires = NULL,
                status = 'active',
                last_modified_date = NOW()
            WHERE id = @userId AND status = 'pending_invitation'
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.ExecuteAsync(new CommandDefinition(sql, new { userId, passwordHash }, cancellationToken: cancellationToken));
        return rows > 0;
    }

    public async Task<bool> UpdateInviteTokenAsync(int userId, int companyId, string token, DateTime expires, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE "user"
            SET password_invite_token = @token,
                password_invite_token_expires = @expires,
                status = 'pending_invitation',
                last_modified_date = NOW()
            WHERE id = @userId AND company_id = @companyId AND status IN ('pending_invitation', 'invitation_expired')
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.ExecuteAsync(new CommandDefinition(sql, new { userId, companyId, token, expires }, cancellationToken: cancellationToken));
        return rows > 0;
    }

    public async Task<int> UpdateExpiredInvitesToInvitationExpiredAsync(int companyId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE "user"
            SET status = 'invitation_expired',
                last_modified_date = NOW()
            WHERE company_id = @companyId
              AND status = 'pending_invitation'
              AND (password_invite_token_expires IS NULL OR password_invite_token_expires < NOW())
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.ExecuteAsync(new CommandDefinition(sql, new { companyId }, cancellationToken: cancellationToken));
    }

    public async Task<User?> UpdateAsync(int id, User user, int companyId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE "user"
            SET first_name = @firstName, last_name = @lastName, email = @email, phone = @phone,
                status = @status, last_modified_date = NOW()
            WHERE id = @id AND company_id = @companyId
            RETURNING id, company_id, email, first_name, last_name, user_type, phone, status, created_date, last_modified_date
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var updated = await connection.QueryFirstOrDefaultAsync<User>(new CommandDefinition(sql, new
        {
            id,
            companyId,
            user.FirstName,
            user.LastName,
            user.Email,
            user.Phone,
            user.Status
        }, cancellationToken: cancellationToken));
        return updated;
    }

    public async Task<bool> DeleteAsync(int id, int companyId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            DELETE FROM "user"
            WHERE id = @id AND company_id = @companyId
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.ExecuteAsync(new CommandDefinition(sql, new { id, companyId }, cancellationToken: cancellationToken));
        return rows > 0;
    }

    public async Task AssignRolesAsync(int userId, IEnumerable<int> roleIds, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM user_role WHERE user_id = @userId", new { userId }, cancellationToken: cancellationToken));
        var roleId = roleIds.FirstOrDefault();
        if (roleId != 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO user_role (user_id, role_id) VALUES (@userId, @roleId) ON CONFLICT (user_id, role_id) DO NOTHING",
                new { userId, roleId }, cancellationToken: cancellationToken));
        }
    }

    public async Task<List<string>> GetRoleNamesForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT r.name
            FROM user_role ur
            JOIN role r ON r.id = ur.role_id
            WHERE ur.user_id = @userId
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var names = await connection.QueryAsync<string>(new CommandDefinition(sql, new { userId }, cancellationToken: cancellationToken));
        return names.ToList();
    }
}
