using Dapper;
using Npgsql;
using MyStore.Models;

namespace MyStore.Repositories;

public class SalesRepository : ISalesRepository
{
    private readonly string _connectionString;

    static SalesRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public SalesRepository()
    {
        _connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("PostgresConnectionString")
            ?? throw new InvalidOperationException("Connection string environment variable is not set");
    }

    public async Task<List<Sale>> GetAllAsync(int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        const string sql = @"SELECT s.id, s.company_id, s.customer_id, s.user_id,
                    COALESCE(SUM(si.price), 0) AS subtotal, 0 AS tax,
                    COALESCE(SUM(si.price), 0) AS total,
                    s.payment_method, s.sale_date, s.notes
             FROM sale s
             LEFT JOIN sale_item si ON si.sale_id = s.id
             WHERE s.company_id = @p_company_id
             GROUP BY s.id, s.company_id, s.customer_id, s.user_id, s.payment_method, s.sale_date, s.notes
             ORDER BY s.sale_date DESC";
        var rows = (await connection.QueryAsync<SaleRow>(sql, new { p_company_id = companyId })).ToList();
        if (rows.Count == 0)
        {
            return new List<Sale>();
        }

        var saleIds = rows.Select(r => r.Id).ToArray();
        const string itemsSql = @"SELECT id, sale_id, inventory_item_id, quantity, unit_price, price AS total_price
            FROM sale_item WHERE sale_id = ANY(@p_sale_ids)";
        var itemRows = (await connection.QueryAsync<SaleItemRow>(itemsSql, new { p_sale_ids = saleIds })).ToList();
        var itemsBySale = itemRows.GroupBy(r => r.SaleId).ToDictionary(g => g.Key, g => g.ToList());

        return rows.Select(row => MapSale(row, itemsBySale.GetValueOrDefault(row.Id) ?? new List<SaleItemRow>())).ToList();
    }

    public async Task<Sale?> GetByIdAsync(int id, int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        const string sql = @"SELECT s.id, s.company_id, s.customer_id, s.user_id,
                    COALESCE(SUM(si.price), 0) AS subtotal, 0 AS tax,
                    COALESCE(SUM(si.price), 0) AS total,
                    s.payment_method, s.sale_date, s.notes
             FROM sale s
             LEFT JOIN sale_item si ON si.sale_id = s.id
             WHERE s.id = @p_id AND s.company_id = @p_company_id
             GROUP BY s.id, s.company_id, s.customer_id, s.user_id, s.payment_method, s.sale_date, s.notes";
        var row = await connection.QuerySingleOrDefaultAsync<SaleRow>(sql, new { p_id = id, p_company_id = companyId });
        if (row == null)
        {
            return null;
        }

        const string itemsSql = @"SELECT id, sale_id, inventory_item_id, quantity, unit_price, price AS total_price
            FROM sale_item WHERE sale_id = @p_sale_id";
        var itemRows = (await connection.QueryAsync<SaleItemRow>(itemsSql, new { p_sale_id = id })).ToList();
        return MapSale(row, itemRows);
    }

    public async Task<List<Sale>> GetByCustomerIdAsync(int customerId, int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        const string sql = @"SELECT s.id, s.company_id, s.customer_id, s.user_id,
                    COALESCE(SUM(si.price), 0) AS subtotal, 0 AS tax,
                    COALESCE(SUM(si.price), 0) AS total,
                    s.payment_method, s.sale_date, s.notes
             FROM sale s
             LEFT JOIN sale_item si ON si.sale_id = s.id
             WHERE s.customer_id = @p_customer_id AND s.company_id = @p_company_id
             GROUP BY s.id, s.company_id, s.customer_id, s.user_id, s.payment_method, s.sale_date, s.notes
             ORDER BY s.sale_date DESC";
        var rows = (await connection.QueryAsync<SaleRow>(sql, new { p_customer_id = customerId, p_company_id = companyId })).ToList();
        return await HydrateItemsAsync(connection, rows);
    }

    public async Task<List<Sale>> GetByUserIdAsync(int userId, int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        const string sql = @"SELECT s.id, s.company_id, s.customer_id, s.user_id,
                    COALESCE(SUM(si.price), 0) AS subtotal, 0 AS tax,
                    COALESCE(SUM(si.price), 0) AS total,
                    s.payment_method, s.sale_date, s.notes
             FROM sale s
             LEFT JOIN sale_item si ON si.sale_id = s.id
             WHERE s.user_id = @p_user_id AND s.company_id = @p_company_id
             GROUP BY s.id, s.company_id, s.customer_id, s.user_id, s.payment_method, s.sale_date, s.notes
             ORDER BY s.sale_date DESC";
        var rows = (await connection.QueryAsync<SaleRow>(sql, new { p_user_id = userId, p_company_id = companyId })).ToList();
        return await HydrateItemsAsync(connection, rows);
    }

    public async Task<List<Sale>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        const string sql = @"SELECT s.id, s.company_id, s.customer_id, s.user_id,
                    COALESCE(SUM(si.price), 0) AS subtotal, 0 AS tax,
                    COALESCE(SUM(si.price), 0) AS total,
                    s.payment_method, s.sale_date, s.notes
             FROM sale s
             LEFT JOIN sale_item si ON si.sale_id = s.id
             WHERE s.company_id = @p_company_id
               AND s.sale_date >= @p_start AND s.sale_date <= @p_end
             GROUP BY s.id, s.company_id, s.customer_id, s.user_id, s.payment_method, s.sale_date, s.notes
             ORDER BY s.sale_date DESC";
        var rows = (await connection.QueryAsync<SaleRow>(sql, new
        {
            p_company_id = companyId,
            p_start = startDate,
            p_end = endDate,
        })).ToList();
        return await HydrateItemsAsync(connection, rows);
    }

    public async Task<Sale> CreateAsync(Sale sale)
    {
        var saleDate = sale.SaleDate == default ? DateTime.UtcNow : sale.SaleDate;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        try
        {
            const string insertSale = @"INSERT INTO sale (company_id, customer_id, user_id,
                    payment_method, sale_date, notes)
                VALUES (@CompanyId, @CustomerId, @UserId, @PaymentMethod, @SaleDate, @Notes)
                RETURNING id";

            var saleId = await connection.ExecuteScalarAsync<int>(insertSale, new
            {
                sale.CompanyId,
                sale.CustomerId,
                UserId = sale.UserId,
                sale.PaymentMethod,
                SaleDate = saleDate,
                sale.Notes,
            }, tx);

            sale.Id = saleId;
            sale.SaleDate = saleDate;

            const string insertItem = @"INSERT INTO sale_item (sale_id, inventory_item_id, quantity, unit_price, price)
                VALUES (@SaleId, @InventoryItemId, @Quantity, @UnitPrice, @TotalPrice)
                RETURNING id";

            foreach (var item in sale.Items)
            {
                var itemId = await connection.ExecuteScalarAsync<int>(insertItem, new
                {
                    SaleId = saleId,
                    item.InventoryItemId,
                    item.Quantity,
                    item.UnitPrice,
                    item.TotalPrice,
                }, tx);
                item.Id = itemId;
                item.SaleId = saleId;
            }

            await tx.CommitAsync();
            return sale;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> DeleteAsync(int id, int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.ExecuteAsync(
            "DELETE FROM sale WHERE id = @p_id AND company_id = @p_company_id",
            new { p_id = id, p_company_id = companyId });
        return rows > 0;
    }

    private static async Task<List<Sale>> HydrateItemsAsync(NpgsqlConnection connection, List<SaleRow> rows)
    {
        if (rows.Count == 0)
        {
            return new List<Sale>();
        }

        var saleIds = rows.Select(r => r.Id).ToArray();
        const string itemsSql = @"SELECT id, sale_id, inventory_item_id, quantity, unit_price, price AS total_price
            FROM sale_item WHERE sale_id = ANY(@p_sale_ids)";
        var itemRows = (await connection.QueryAsync<SaleItemRow>(itemsSql, new { p_sale_ids = saleIds })).ToList();
        var itemsBySale = itemRows.GroupBy(r => r.SaleId).ToDictionary(g => g.Key, g => g.ToList());

        return rows.Select(row => MapSale(row, itemsBySale.GetValueOrDefault(row.Id) ?? new List<SaleItemRow>())).ToList();
    }

    private static Sale MapSale(SaleRow row, List<SaleItemRow> itemRows)
    {
        var sale = new Sale
        {
            Id = row.Id,
            CompanyId = row.CompanyId,
            CustomerId = row.CustomerId,
            UserId = row.UserId,
            Subtotal = row.Subtotal,
            Tax = row.Tax,
            Total = row.Total,
            PaymentMethod = row.PaymentMethod,
            SaleDate = row.SaleDate,
            Notes = row.Notes,
        };

        foreach (var ir in itemRows)
        {
            sale.Items.Add(new SaleItem
            {
                Id = ir.Id,
                SaleId = ir.SaleId,
                InventoryItemId = ir.InventoryItemId,
                Quantity = ir.Quantity,
                UnitPrice = ir.UnitPrice,
                TotalPrice = ir.TotalPrice,
            });
        }

        return sale;
    }

    private sealed class SaleRow
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public int CustomerId { get; set; }
        public int? UserId { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public DateTime SaleDate { get; set; }
        public string? Notes { get; set; }
    }

    private sealed class SaleItemRow
    {
        public int Id { get; set; }
        public int SaleId { get; set; }
        public int InventoryItemId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice { get; set; }
    }
}
