using Dapper;
using Npgsql;
using MyStore.Models;

namespace MyStore.Repositories;

public class PromotionRepository : IPromotionRepository
{
    private readonly string _connectionString;

    private const string SelectColumns =
        "id, company_id, name, type, discount_percent, buy_quantity, get_quantity, scope, scope_value, start_date, end_date, is_active, created_by, created_at";

    static PromotionRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public PromotionRepository()
    {
        _connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("PostgresConnectionString")
            ?? throw new InvalidOperationException("Connection string environment variable is not set");
    }

    public async Task<List<Promotion>> GetAllAsync(int companyId)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);
        var rows = await connection.QueryAsync<Promotion>(
            $@"SELECT {SelectColumns} FROM promotion
               WHERE company_id = @p_company_id
               ORDER BY created_at DESC",
            new { p_company_id = companyId });
        return rows.ToList();
    }

    public async Task<Promotion?> GetByIdAsync(int id, int companyId)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);
        return await connection.QueryFirstOrDefaultAsync<Promotion>(
            $@"SELECT {SelectColumns} FROM promotion
               WHERE id = @p_id AND company_id = @p_company_id",
            new { p_id = id, p_company_id = companyId });
    }

    public async Task<Promotion> CreateAsync(Promotion promotion)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, promotion.CompanyId);
        return await connection.QuerySingleAsync<Promotion>(
            $@"INSERT INTO promotion (
                company_id, name, type, discount_percent, buy_quantity, get_quantity,
                scope, scope_value, start_date, end_date, is_active, created_by, created_at)
               VALUES (
                @p_company_id, @p_name, @p_type, @p_discount_percent, @p_buy_quantity, @p_get_quantity,
                @p_scope, @p_scope_value, @p_start_date, @p_end_date, @p_is_active, @p_created_by, NOW())
               RETURNING {SelectColumns}",
            new
            {
                p_company_id = promotion.CompanyId,
                p_name = promotion.Name,
                p_type = promotion.Type,
                p_discount_percent = promotion.DiscountPercent,
                p_buy_quantity = promotion.BuyQuantity,
                p_get_quantity = promotion.GetQuantity,
                p_scope = promotion.Scope,
                p_scope_value = promotion.ScopeValue,
                p_start_date = promotion.StartDate,
                p_end_date = promotion.EndDate,
                p_is_active = promotion.IsActive,
                p_created_by = promotion.CreatedBy,
            });
    }

    public async Task<Promotion?> UpdateAsync(Promotion promotion)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, promotion.CompanyId);
        var rows = await connection.ExecuteAsync(
            @"UPDATE promotion SET
                name = @p_name,
                type = @p_type,
                discount_percent = @p_discount_percent,
                buy_quantity = @p_buy_quantity,
                get_quantity = @p_get_quantity,
                scope = @p_scope,
                scope_value = @p_scope_value,
                start_date = @p_start_date,
                end_date = @p_end_date,
                is_active = @p_is_active
              WHERE id = @p_id AND company_id = @p_company_id",
            new
            {
                p_id = promotion.Id,
                p_company_id = promotion.CompanyId,
                p_name = promotion.Name,
                p_type = promotion.Type,
                p_discount_percent = promotion.DiscountPercent,
                p_buy_quantity = promotion.BuyQuantity,
                p_get_quantity = promotion.GetQuantity,
                p_scope = promotion.Scope,
                p_scope_value = promotion.ScopeValue,
                p_start_date = promotion.StartDate,
                p_end_date = promotion.EndDate,
                p_is_active = promotion.IsActive,
            });
        if (rows == 0) return null;
        return await GetByIdAsync(promotion.Id, promotion.CompanyId);
    }

    public async Task<bool> DeleteAsync(int id, int companyId)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);
        var rows = await connection.ExecuteAsync(
            @"DELETE FROM promotion WHERE id = @p_id AND company_id = @p_company_id",
            new { p_id = id, p_company_id = companyId });
        return rows > 0;
    }

    public async Task<List<Promotion>> GetActiveAsync(int companyId, DateTime date)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);
        var rows = await connection.QueryAsync<Promotion>(
            $@"SELECT {SelectColumns} FROM promotion
               WHERE company_id = @p_company_id
                 AND is_active = TRUE
                 AND start_date <= @p_date
                 AND (end_date IS NULL OR end_date >= @p_date)
               ORDER BY created_at DESC",
            new { p_company_id = companyId, p_date = date });
        return rows.ToList();
    }
}
