using Dapper;
using Npgsql;

namespace MyStore.Tests.Repositories;

/// <summary>
/// Helpers for the DB-integration tests, which run only inside the integration-tests workflow
/// (it provisions an ephemeral Postgres, applies the dbproj-mystore migrations, and exports
/// <c>MYSTORE_TEST_DB</c> + <c>RUN_DB_INTEGRATION_TESTS</c>).
///
/// The connection string is read from <c>MYSTORE_TEST_DB</c> rather than
/// <c>ConnectionStrings__DefaultConnection</c> because the repository test classes null the latter
/// in their constructors to exercise the "no connection string" contract. Each integration test
/// calls <see cref="UseForRepositories"/> to point the repository under test at the test DB before
/// constructing it.
///
/// Seed rows use fresh companies per test (SERIAL ids), so tests are isolated by tenant id without
/// needing truncation between runs.
/// </summary>
internal static class IntegrationDb
{
    public static string ConnectionString =>
        System.Environment.GetEnvironmentVariable("MYSTORE_TEST_DB")
        ?? throw new System.InvalidOperationException(
            "MYSTORE_TEST_DB is not set — DB integration tests must run via the integration-tests workflow.");

    /// <summary>Point the repositories (which read ConnectionStrings__DefaultConnection) at the test DB.</summary>
    public static void UseForRepositories() =>
        System.Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", ConnectionString);

    public static async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>Insert a company and return its id.</summary>
    public static Task<int> SeedCompanyAsync(NpgsqlConnection conn) =>
        conn.ExecuteScalarAsync<int>(
            @"INSERT INTO company (email, status, trial_start_date, trial_end_date, subscription_tier, created_date)
              VALUES (@email, 'active', NOW(), NOW() + INTERVAL '14 days', 'trial', NOW())
              RETURNING id",
            new { email = $"co-{System.Guid.NewGuid():N}@example.test" });

    /// <summary>Insert a user for the company and return its id (used as trade_in.created_by).</summary>
    public static Task<int> SeedUserAsync(NpgsqlConnection conn, int companyId) =>
        conn.ExecuteScalarAsync<int>(
            @"INSERT INTO ""user"" (company_id, email, first_name, last_name)
              VALUES (@companyId, @email, 'Test', 'User')
              RETURNING id",
            new { companyId, email = $"user-{System.Guid.NewGuid():N}@example.test" });

    /// <summary>Insert a customer for the company and return its id.</summary>
    public static Task<int> SeedCustomerAsync(NpgsqlConnection conn, int companyId) =>
        conn.ExecuteScalarAsync<int>(
            @"INSERT INTO customer (company_id, first_name, last_name, email, created_date)
              VALUES (@companyId, 'Cust', 'Omer', @email, NOW())
              RETURNING id",
            new { companyId, email = $"cust-{System.Guid.NewGuid():N}@example.test" });
}
