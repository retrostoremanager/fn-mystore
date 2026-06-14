using Dapper;
using Npgsql;
using MyStore.Models;

namespace MyStore.Repositories;

public class ConsignmentRepository : IConsignmentRepository
{
    private readonly string _connectionString;

    private const string ItemSelectColumns =
        "id, company_id, customer_id, description, asking_price, sale_price, split_percent, status, inventory_item_id, created_at, updated_at";

    static ConsignmentRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public ConsignmentRepository()
    {
        _connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("PostgresConnectionString")
            ?? throw new InvalidOperationException("Connection string environment variable is not set");
    }

    public async Task<List<ConsignmentItem>> GetAllAsync(int companyId, string? status = null)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);
        IEnumerable<ConsignmentItem> rows;
        if (status is null)
        {
            rows = await connection.QueryAsync<ConsignmentItem>(
                $@"SELECT {ItemSelectColumns} FROM consignment_item
                   WHERE company_id = @p_company_id
                   ORDER BY created_at DESC",
                new { p_company_id = companyId });
        }
        else
        {
            rows = await connection.QueryAsync<ConsignmentItem>(
                $@"SELECT {ItemSelectColumns} FROM consignment_item
                   WHERE company_id = @p_company_id
                     AND status = @p_status
                   ORDER BY created_at DESC",
                new { p_company_id = companyId, p_status = status });
        }
        return rows.ToList();
    }

    public async Task<ConsignmentItem?> GetByIdAsync(int id, int companyId)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);
        return await connection.QueryFirstOrDefaultAsync<ConsignmentItem>(
            $@"SELECT {ItemSelectColumns} FROM consignment_item
               WHERE id = @p_id AND company_id = @p_company_id",
            new { p_id = id, p_company_id = companyId });
    }

    public async Task<ConsignmentItem> CreateAsync(ConsignmentItem item)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, item.CompanyId);
        return await connection.QuerySingleAsync<ConsignmentItem>(
            $@"INSERT INTO consignment_item (
                company_id, customer_id, description, asking_price, split_percent, status, inventory_item_id, created_at)
               VALUES (
                @p_company_id, @p_customer_id, @p_description, @p_asking_price, @p_split_percent, @p_status, @p_inventory_item_id, NOW())
               RETURNING {ItemSelectColumns}",
            new
            {
                p_company_id = item.CompanyId,
                p_customer_id = item.CustomerId,
                p_description = item.Description,
                p_asking_price = item.AskingPrice,
                p_split_percent = item.SplitPercent,
                p_status = item.Status,
                p_inventory_item_id = item.InventoryItemId,
            });
    }

    public async Task<ConsignmentItem?> UpdateAsync(ConsignmentItem item)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, item.CompanyId);
        var rows = await connection.ExecuteAsync(
            @"UPDATE consignment_item SET
                customer_id = @p_customer_id,
                description = @p_description,
                asking_price = @p_asking_price,
                split_percent = @p_split_percent,
                status = @p_status,
                inventory_item_id = @p_inventory_item_id,
                updated_at = NOW()
              WHERE id = @p_id AND company_id = @p_company_id",
            new
            {
                p_id = item.Id,
                p_company_id = item.CompanyId,
                p_customer_id = item.CustomerId,
                p_description = item.Description,
                p_asking_price = item.AskingPrice,
                p_split_percent = item.SplitPercent,
                p_status = item.Status,
                p_inventory_item_id = item.InventoryItemId,
            });
        if (rows == 0) return null;
        return await GetByIdAsync(item.Id, item.CompanyId);
    }

    public async Task<ConsignmentItem?> MarkSoldAsync(int id, decimal salePrice, int companyId)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);
        var rows = await connection.ExecuteAsync(
            @"UPDATE consignment_item SET
                sale_price = @p_sale_price,
                status = 'sold',
                updated_at = NOW()
              WHERE id = @p_id AND company_id = @p_company_id",
            new { p_id = id, p_sale_price = salePrice, p_company_id = companyId });
        if (rows == 0) return null;
        return await GetByIdAsync(id, companyId);
    }

    public async Task<List<ConsignmentPayout>> GetPayoutsAsync(int itemId, int companyId)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);
        var rows = await connection.QueryAsync<ConsignmentPayout>(
            $@"SELECT p.id, p.consignment_item_id, p.amount, p.paid_at, p.notes
               FROM consignment_payout p
               INNER JOIN consignment_item i ON i.id = p.consignment_item_id
               WHERE p.consignment_item_id = @p_item_id AND i.company_id = @p_company_id
               ORDER BY p.paid_at DESC",
            new { p_item_id = itemId, p_company_id = companyId });
        return rows.ToList();
    }

    public async Task<ConsignmentPayout> CreatePayoutAsync(ConsignmentPayout payout)
    {
        // consignment_payout has no company_id (child of consignment_item) and no RLS policy,
        // so no tenant GUC is needed here. If RLS is ever added to consignment_payout, thread
        // companyId in and open via TenantConnection.OpenAsync.
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleAsync<ConsignmentPayout>(
            @"INSERT INTO consignment_payout (consignment_item_id, amount, paid_at, notes)
               VALUES (@p_consignment_item_id, @p_amount, NOW(), @p_notes)
               RETURNING id, consignment_item_id, amount, paid_at, notes",
            new
            {
                p_consignment_item_id = payout.ConsignmentItemId,
                p_amount = payout.Amount,
                p_notes = payout.Notes,
            });
    }
}
