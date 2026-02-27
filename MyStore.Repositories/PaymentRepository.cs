using Dapper;
using Npgsql;
using MyStore.Models;

namespace MyStore.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly string _connectionString;

    static PaymentRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public PaymentRepository()
    {
        _connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("PostgresConnectionString")
            ?? throw new InvalidOperationException("Connection string environment variable is not set");
    }

    public async Task<PaymentMethod?> CreateAsync(PaymentMethod paymentMethod)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var id = await connection.QuerySingleAsync<int>(
            @"INSERT INTO payment_method (company_id, stripe_customer_id, stripe_payment_method_id, last4, expiration_month, expiration_year, is_default, created_date)
              VALUES (@company_id, @stripe_customer_id, @stripe_payment_method_id, @last4, @expiration_month, @expiration_year, @is_default, @created_date)
              RETURNING id",
            new
            {
                company_id = paymentMethod.CompanyId,
                stripe_customer_id = paymentMethod.StripeCustomerId,
                stripe_payment_method_id = paymentMethod.StripePaymentMethodId,
                last4 = paymentMethod.Last4,
                expiration_month = paymentMethod.ExpirationMonth,
                expiration_year = paymentMethod.ExpirationYear,
                is_default = paymentMethod.IsDefault,
                created_date = paymentMethod.CreatedDate
            });
        paymentMethod.Id = id;
        return paymentMethod;
    }

    public async Task<IEnumerable<PaymentMethod>> GetByCompanyIdAsync(int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryAsync<PaymentMethod>(
            "SELECT * FROM payment_method WHERE company_id = @company_id ORDER BY is_default DESC, created_date DESC",
            new { company_id = companyId });
    }

    public async Task<PaymentMethod?> GetByIdAsync(int id, int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<PaymentMethod>(
            "SELECT * FROM payment_method WHERE id = @id AND company_id = @company_id",
            new { id, company_id = companyId });
    }

    public async Task SetDefaultAsync(int companyId, int paymentMethodId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(
            @"UPDATE payment_method SET is_default = false, last_modified_date = NOW() WHERE company_id = @company_id;
              UPDATE payment_method SET is_default = true, last_modified_date = NOW() WHERE id = @id AND company_id = @company_id",
            new { company_id = companyId, id = paymentMethodId });
    }

    public async Task<int?> GetCompanyIdByStripeCustomerIdAsync(string stripeCustomerId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<int?>(
            "SELECT company_id FROM payment_method WHERE stripe_customer_id = @stripe_customer_id LIMIT 1",
            new { stripe_customer_id = stripeCustomerId });
    }
}
