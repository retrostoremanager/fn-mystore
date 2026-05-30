using Dapper;
using Npgsql;
using MyStore.Models;

namespace MyStore.Repositories;

public class LoyaltyRepository : ILoyaltyRepository
{
    private readonly string _connectionString;

    static LoyaltyRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public LoyaltyRepository()
    {
        _connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("PostgresConnectionString")
            ?? throw new InvalidOperationException("Connection string environment variable is not set");
    }

    public async Task<LoyaltySettings?> GetSettingsAsync(int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<LoyaltySettings>(
            @"SELECT id, company_id, points_per_dollar_spent, points_per_dollar_trade_in, redemption_rate, is_enabled
              FROM loyalty_settings
              WHERE company_id = @p_company_id",
            new { p_company_id = companyId });
    }

    public async Task<LoyaltySettings> UpsertSettingsAsync(LoyaltySettings settings)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleAsync<LoyaltySettings>(
            @"INSERT INTO loyalty_settings (company_id, points_per_dollar_spent, points_per_dollar_trade_in, redemption_rate, is_enabled)
              VALUES (@p_company_id, @p_points_per_dollar_spent, @p_points_per_dollar_trade_in, @p_redemption_rate, @p_is_enabled)
              ON CONFLICT (company_id) DO UPDATE SET
                points_per_dollar_spent    = EXCLUDED.points_per_dollar_spent,
                points_per_dollar_trade_in = EXCLUDED.points_per_dollar_trade_in,
                redemption_rate            = EXCLUDED.redemption_rate,
                is_enabled                 = EXCLUDED.is_enabled
              RETURNING id, company_id, points_per_dollar_spent, points_per_dollar_trade_in, redemption_rate, is_enabled",
            new
            {
                p_company_id = settings.CompanyId,
                p_points_per_dollar_spent = settings.PointsPerDollarSpent,
                p_points_per_dollar_trade_in = settings.PointsPerDollarTradeIn,
                p_redemption_rate = settings.RedemptionRate,
                p_is_enabled = settings.IsEnabled,
            });
    }

    public async Task<int> GetBalanceAsync(int companyId, int customerId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var result = await connection.QueryFirstOrDefaultAsync<int?>(
            @"SELECT COALESCE(SUM(points), 0)
              FROM loyalty_transaction
              WHERE company_id = @p_company_id AND customer_id = @p_customer_id",
            new { p_company_id = companyId, p_customer_id = customerId });
        return result ?? 0;
    }

    public async Task<LoyaltyTransaction> AddTransactionAsync(LoyaltyTransaction transaction)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleAsync<LoyaltyTransaction>(
            @"INSERT INTO loyalty_transaction (company_id, customer_id, points, transaction_type, reference_id, notes, created_at)
              VALUES (@p_company_id, @p_customer_id, @p_points, @p_transaction_type, @p_reference_id, @p_notes, NOW())
              RETURNING id, company_id, customer_id, points, transaction_type, reference_id, notes, created_at",
            new
            {
                p_company_id = transaction.CompanyId,
                p_customer_id = transaction.CustomerId,
                p_points = transaction.Points,
                p_transaction_type = transaction.TransactionType,
                p_reference_id = transaction.ReferenceId,
                p_notes = transaction.Notes,
            });
    }

    public async Task<List<LoyaltyTransaction>> GetTransactionsAsync(int companyId, int customerId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.QueryAsync<LoyaltyTransaction>(
            @"SELECT id, company_id, customer_id, points, transaction_type, reference_id, notes, created_at
              FROM loyalty_transaction
              WHERE company_id = @p_company_id AND customer_id = @p_customer_id
              ORDER BY created_at DESC",
            new { p_company_id = companyId, p_customer_id = customerId });
        return rows.ToList();
    }
}
