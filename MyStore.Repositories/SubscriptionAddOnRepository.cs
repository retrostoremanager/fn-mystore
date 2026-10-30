using Dapper;
using Npgsql;
using MyStore.Models;

namespace MyStore.Repositories;

public class SubscriptionAddOnRepository : ISubscriptionAddOnRepository
{
    private readonly string _connectionString;

    public SubscriptionAddOnRepository()
    {
        _connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("PostgresConnectionString")
            ?? throw new InvalidOperationException("Connection string environment variable is not set");
    }

    public async Task<SubscriptionAddOn?> CreateAsync(SubscriptionAddOn addOn)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var id = await connection.QuerySingleAsync<int>(
            @"INSERT INTO subscription_addon (subscription_id, addon_type, stripe_subscription_item_id, stripe_price_id, status, created_date)
              VALUES (@subscription_id, @addon_type, @stripe_subscription_item_id, @stripe_price_id, @status, @created_date)
              RETURNING id",
            new
            {
                subscription_id = addOn.SubscriptionId,
                addon_type = addOn.AddonType,
                stripe_subscription_item_id = addOn.StripeSubscriptionItemId,
                stripe_price_id = addOn.StripePriceId,
                status = addOn.Status,
                created_date = addOn.CreatedDate
            });
        addOn.Id = id;
        return addOn;
    }

    public async Task<SubscriptionAddOn?> GetBySubscriptionIdAndTypeAsync(int subscriptionId, string addonType)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<SubscriptionAddOn>(
            "SELECT * FROM subscription_addon WHERE subscription_id = @subscription_id AND addon_type = @addon_type",
            new { subscription_id = subscriptionId, addon_type = addonType });
    }

    public async Task<IEnumerable<SubscriptionAddOn>> GetBySubscriptionIdAsync(int subscriptionId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryAsync<SubscriptionAddOn>(
            "SELECT * FROM subscription_addon WHERE subscription_id = @subscription_id",
            new { subscription_id = subscriptionId });
    }

    public async Task<SubscriptionAddOn?> UpdateAsync(SubscriptionAddOn addOn)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.ExecuteAsync(
            @"UPDATE subscription_addon SET
                stripe_subscription_item_id = @stripe_subscription_item_id,
                stripe_price_id = @stripe_price_id,
                status = @status,
                last_modified_date = NOW()
              WHERE id = @id",
            new
            {
                id = addOn.Id,
                stripe_subscription_item_id = addOn.StripeSubscriptionItemId,
                stripe_price_id = addOn.StripePriceId,
                status = addOn.Status
            });
        return rows > 0 ? addOn : null;
    }
}
