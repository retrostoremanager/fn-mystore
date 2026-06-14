using Dapper;
using Npgsql;
using MyStore.Models;

namespace MyStore.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private readonly string _connectionString;

    private const string SelectColumns =
        "id, company_id, first_name, last_name, email, phone, address, city, state, zip_code, created_date, last_modified_date";

    private const string SelectColumnsWithPoints =
        "c.id, c.company_id, c.first_name, c.last_name, c.email, c.phone, c.address, c.city, c.state, c.zip_code, c.created_date, c.last_modified_date, " +
        "COALESCE((SELECT SUM(lt.points) FROM loyalty_transaction lt WHERE lt.customer_id = c.id AND lt.company_id = c.company_id), 0) AS points_balance";

    static CustomerRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public CustomerRepository()
    {
        _connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("PostgresConnectionString")
            ?? throw new InvalidOperationException("Connection string environment variable is not set");
    }

    public async Task<List<Customer>> GetAllAsync(int companyId)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);
        var rows = await connection.QueryAsync<Customer>(
            $@"SELECT {SelectColumnsWithPoints} FROM customer c
               WHERE c.company_id = @p_company_id
               ORDER BY c.last_name, c.first_name",
            new { p_company_id = companyId });
        return rows.ToList();
    }

    public async Task<Customer?> GetByIdAsync(int id, int companyId)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);
        return await connection.QueryFirstOrDefaultAsync<Customer>(
            $@"SELECT {SelectColumnsWithPoints} FROM customer c
               WHERE c.id = @p_id AND c.company_id = @p_company_id",
            new { p_id = id, p_company_id = companyId });
    }

    public async Task<Customer?> GetByEmailAsync(string email, int companyId)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);
        return await connection.QueryFirstOrDefaultAsync<Customer>(
            $@"SELECT {SelectColumnsWithPoints} FROM customer c
               WHERE c.company_id = @p_company_id
                 AND c.email IS NOT NULL
                 AND lower(c.email) = lower(@p_email)",
            new { p_company_id = companyId, p_email = email });
    }

    public async Task<Customer> CreateAsync(Customer customer)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, customer.CompanyId);
        return await connection.QuerySingleAsync<Customer>(
            $@"INSERT INTO customer (
                company_id, first_name, last_name, email, phone, address, city, state, zip_code, created_date, last_modified_date)
              VALUES (
                @p_company_id, @p_first_name, @p_last_name, @p_email, @p_phone, @p_address, @p_city, @p_state, @p_zip_code, NOW(), NOW())
              RETURNING {SelectColumns}, 0 AS points_balance",
            new
            {
                p_company_id = customer.CompanyId,
                p_first_name = customer.FirstName,
                p_last_name = customer.LastName,
                p_email = customer.Email,
                p_phone = customer.Phone,
                p_address = customer.Address,
                p_city = customer.City,
                p_state = customer.State,
                p_zip_code = customer.ZipCode,
            });
    }

    public async Task<Customer?> UpdateAsync(int id, Customer customer, int companyId)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);
        var rows = await connection.ExecuteAsync(
            @"UPDATE customer SET
                first_name = @p_first_name,
                last_name = @p_last_name,
                email = @p_email,
                phone = @p_phone,
                address = @p_address,
                city = @p_city,
                state = @p_state,
                zip_code = @p_zip_code,
                last_modified_date = NOW()
              WHERE id = @p_id AND company_id = @p_company_id",
            new
            {
                p_id = id,
                p_company_id = companyId,
                p_first_name = customer.FirstName,
                p_last_name = customer.LastName,
                p_email = customer.Email,
                p_phone = customer.Phone,
                p_address = customer.Address,
                p_city = customer.City,
                p_state = customer.State,
                p_zip_code = customer.ZipCode,
            });
        if (rows == 0) return null;
        return await GetByIdAsync(id, companyId);
    }

    public async Task<bool> DeleteAsync(int id, int companyId)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);
        var rows = await connection.ExecuteAsync(
            "DELETE FROM customer WHERE id = @p_id AND company_id = @p_company_id",
            new { p_id = id, p_company_id = companyId });
        return rows > 0;
    }

    public async Task<List<Customer>> SearchAsync(string searchTerm, int companyId)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return await GetAllAsync(companyId);

        var needle = searchTerm.Trim().ToLowerInvariant();
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);
        var rows = await connection.QueryAsync<Customer>(
            $@"SELECT {SelectColumnsWithPoints} FROM customer c
               WHERE c.company_id = @p_company_id
                 AND strpos(
                   lower(concat_ws(' ', c.first_name, c.last_name, coalesce(c.email, ''), coalesce(c.phone, ''))),
                   @p_needle) > 0
               ORDER BY c.last_name, c.first_name",
            new { p_company_id = companyId, p_needle = needle });
        return rows.ToList();
    }
}
