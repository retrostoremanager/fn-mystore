using Dapper;
using Npgsql;
using MyStore.Models;

namespace MyStore.Repositories;

public class SubscriptionRepository : ISubscriptionRepository
{
    private readonly string _connectionString;

    static SubscriptionRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public SubscriptionRepository()
    {
        _connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("PostgresConnectionString")
            ?? throw new InvalidOperationException("Connection string environment variable is not set");
    }

    public async Task<Subscription?> CreateAsync(Subscription subscription)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var id = await connection.QuerySingleAsync<int>(
            @"INSERT INTO subscription (company_id, stripe_subscription_id, stripe_customer_id, stripe_price_id, status, current_period_start, current_period_end, cancel_at_period_end, created_date)
              VALUES (@company_id, @stripe_subscription_id, @stripe_customer_id, @stripe_price_id, @status, @current_period_start, @current_period_end, @cancel_at_period_end, @created_date)
              RETURNING id",
            new
            {
                company_id = subscription.CompanyId,
                stripe_subscription_id = subscription.StripeSubscriptionId,
                stripe_customer_id = subscription.StripeCustomerId,
                stripe_price_id = subscription.StripePriceId,
                status = subscription.Status,
                current_period_start = subscription.CurrentPeriodStart,
                current_period_end = subscription.CurrentPeriodEnd,
                cancel_at_period_end = subscription.CancelAtPeriodEnd,
                created_date = subscription.CreatedDate
            });
        subscription.Id = id;
        return subscription;
    }

    public async Task<Subscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<Subscription>(
            "SELECT * FROM subscription WHERE stripe_subscription_id = @stripe_subscription_id",
            new { stripe_subscription_id = stripeSubscriptionId });
    }

    public async Task<Subscription?> GetByCompanyIdAsync(int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<Subscription>(
            "SELECT * FROM subscription WHERE company_id = @company_id ORDER BY created_date DESC LIMIT 1",
            new { company_id = companyId });
    }

    public async Task<Subscription?> UpdateAsync(Subscription subscription)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.ExecuteAsync(
            @"UPDATE subscription SET
                status = @status,
                stripe_price_id = @stripe_price_id,
                current_period_start = @current_period_start,
                current_period_end = @current_period_end,
                cancel_at_period_end = @cancel_at_period_end,
                last_modified_date = NOW()
              WHERE id = @id",
            new
            {
                id = subscription.Id,
                status = subscription.Status,
                stripe_price_id = subscription.StripePriceId,
                current_period_start = subscription.CurrentPeriodStart,
                current_period_end = subscription.CurrentPeriodEnd,
                cancel_at_period_end = subscription.CancelAtPeriodEnd
            });
        return rows > 0 ? subscription : null;
    }
}
