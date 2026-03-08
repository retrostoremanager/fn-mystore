using Dapper;
using Npgsql;
using MyStore.Models;

namespace MyStore.Repositories;

public class InventoryRepository : IInventoryRepository
{
    private readonly string _connectionString;

    static InventoryRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public InventoryRepository()
    {
        _connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("PostgresConnectionString")
            ?? throw new InvalidOperationException("Connection string environment variable is not set");
    }

    public async Task<List<InventoryItem>> GetAllAsync(int companyId, int? locationId = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var sql = @"SELECT ii.id, ii.company_id, ii.location_id, ii.quantity, ii.price as sell_price, ii.cost as buy_price,
                     ii.condition, ii.game_id, ii.notes, ii.created_date as added_date, ii.last_modified_date,
                     COALESCE(g.title, '') as name, COALESCE(g.genre, '') as category,
                     l.name as location_name,
                     g.id as game_id_val, g.title as game_title, g.console as game_console, g.release_date as game_release_date,
                     g.publisher as game_publisher, g.genre as game_genre
              FROM inventory_item ii
              LEFT JOIN location l ON ii.location_id = l.id
              LEFT JOIN game g ON ii.game_id = g.id
              WHERE ii.company_id = @p_company_id
                AND (@p_location_id IS NULL OR ii.location_id = @p_location_id)
              ORDER BY COALESCE(g.title, '')";
        var rows = await connection.QueryAsync<InventoryItemRow>(sql,
            new { p_company_id = companyId, p_location_id = locationId });
        return rows.Select(MapToInventoryItem).ToList();
    }

    public async Task<InventoryItem?> GetByIdAsync(int id, int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var row = await connection.QueryFirstOrDefaultAsync<InventoryItemRow>(
            @"SELECT ii.id, ii.company_id, ii.location_id, ii.quantity, ii.price as sell_price, ii.cost as buy_price,
                     ii.condition, ii.game_id, ii.notes, ii.created_date as added_date, ii.last_modified_date,
                     COALESCE(g.title, '') as name, COALESCE(g.genre, '') as category,
                     l.name as location_name,
                     g.id as game_id_val, g.title as game_title, g.console as game_console, g.release_date as game_release_date,
                     g.publisher as game_publisher, g.genre as game_genre
              FROM inventory_item ii
              LEFT JOIN location l ON ii.location_id = l.id
              LEFT JOIN game g ON ii.game_id = g.id
              WHERE ii.id = @p_id AND ii.company_id = @p_company_id",
            new { p_id = id, p_company_id = companyId });
        return row == null ? null : MapToInventoryItem(row);
    }

    public async Task<InventoryItem> CreateAsync(InventoryItem item)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var id = await connection.QuerySingleAsync<int>(
            @"INSERT INTO inventory_item (company_id, location_id, quantity, price, cost, condition, game_id, notes, created_date, last_modified_date)
              VALUES (@p_company_id, @p_location_id, @p_quantity, @p_sell_price, @p_buy_price, @p_condition,
                  @p_game_id, @p_notes, NOW(), NOW())
              RETURNING id",
            new
            {
                p_company_id = item.CompanyId,
                p_location_id = item.LocationId,
                p_quantity = item.Quantity,
                p_sell_price = item.SellPrice,
                p_buy_price = item.BuyPrice,
                p_condition = item.Condition,
                p_game_id = item.Game?.Id,
                p_notes = item.Notes
            });
        item.Id = id;
        item.AddedDate = item.AddedDate == default ? DateTime.UtcNow : item.AddedDate;
        item.LastModifiedDate = DateTime.UtcNow;
        return item;
    }

    public async Task<InventoryItem?> UpdateAsync(int id, InventoryItem item, int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(
            @"UPDATE inventory_item SET
                location_id = @p_location_id,
                quantity = @p_quantity,
                price = @p_sell_price,
                cost = @p_buy_price,
                condition = @p_condition,
                game_id = @p_game_id,
                notes = @p_notes,
                last_modified_date = NOW()
              WHERE id = @p_id AND company_id = @p_company_id",
            new
            {
                p_id = id,
                p_company_id = companyId,
                p_location_id = item.LocationId,
                p_quantity = item.Quantity,
                p_sell_price = item.SellPrice,
                p_buy_price = item.BuyPrice,
                p_condition = item.Condition,
                p_game_id = item.Game?.Id,
                p_notes = item.Notes
            });
        if (rowsAffected == 0) return null;
        return await GetByIdAsync(id, companyId);
    }

    public async Task<bool> DeleteAsync(int id, int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(
            "DELETE FROM inventory_item WHERE id = @p_id AND company_id = @p_company_id",
            new { p_id = id, p_company_id = companyId });
        return rowsAffected > 0;
    }

    public async Task<List<InventoryItem>> SearchAsync(string searchTerm, int companyId, int? locationId = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var term = $"%{searchTerm.ToLowerInvariant()}%";
        var rows = await connection.QueryAsync<InventoryItemRow>(
            @"SELECT ii.id, ii.company_id, ii.location_id, ii.quantity, ii.price as sell_price, ii.cost as buy_price,
                     ii.condition, ii.game_id, ii.notes, ii.created_date as added_date, ii.last_modified_date,
                     COALESCE(g.title, '') as name, COALESCE(g.genre, '') as category,
                     l.name as location_name,
                     g.id as game_id_val, g.title as game_title, g.console as game_console, g.release_date as game_release_date,
                     g.publisher as game_publisher, g.genre as game_genre
              FROM inventory_item ii
              LEFT JOIN location l ON ii.location_id = l.id
              LEFT JOIN game g ON ii.game_id = g.id
              WHERE ii.company_id = @p_company_id
                AND (@p_location_id IS NULL OR ii.location_id = @p_location_id)
                AND (LOWER(COALESCE(g.title, '')) LIKE @p_term OR LOWER(COALESCE(g.genre, '')) LIKE @p_term
                     OR (g.id IS NOT NULL AND (LOWER(g.title) LIKE @p_term OR LOWER(g.console) LIKE @p_term)))
              ORDER BY COALESCE(g.title, '')",
            new { p_company_id = companyId, p_location_id = locationId, p_term = term });
        return rows.Select(MapToInventoryItem).ToList();
    }

    public async Task<bool> UpdateQuantityAsync(int id, int quantityChange, int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(
            @"UPDATE inventory_item SET quantity = GREATEST(0, quantity + @p_change), last_modified_date = NOW()
              WHERE id = @p_id AND company_id = @p_company_id",
            new { p_id = id, p_company_id = companyId, p_change = quantityChange });
        return rowsAffected > 0;
    }

    public async Task<int> GetCountByLocationIdAsync(int locationId, int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory_item WHERE location_id = @p_location_id AND company_id = @p_company_id",
            new { p_location_id = locationId, p_company_id = companyId });
    }

    public async Task<int> DeleteByLocationIdAsync(int locationId, int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.ExecuteAsync(
            "DELETE FROM inventory_item WHERE location_id = @p_location_id AND company_id = @p_company_id",
            new { p_location_id = locationId, p_company_id = companyId });
    }

    public async Task<int> ReassignToLocationAsync(int fromLocationId, int toLocationId, int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.ExecuteAsync(
            @"UPDATE inventory_item SET location_id = @p_to_id, last_modified_date = NOW()
              WHERE location_id = @p_from_id AND company_id = @p_company_id",
            new { p_from_id = fromLocationId, p_to_id = toLocationId, p_company_id = companyId });
    }

    private static InventoryItem MapToInventoryItem(InventoryItemRow row)
    {
        return new InventoryItem
        {
            Id = row.Id,
            CompanyId = row.CompanyId,
            LocationId = row.LocationId,
            LocationName = row.LocationName,
            Name = row.Name ?? "",
            Category = row.Category ?? "",
            Quantity = row.Quantity,
            SellPrice = row.SellPrice,
            BuyPrice = row.BuyPrice,
            Condition = row.Condition ?? "",
            Game = string.IsNullOrEmpty(row.GameIdVal) ? null : new Game
            {
                Id = row.GameIdVal,
                Title = row.GameTitle ?? "",
                Console = row.GameConsole ?? "",
                ReleaseDate = row.GameReleaseDate,
                Publisher = row.GamePublisher,
                Genre = row.GameGenre
            },
            Completeness = new Completeness
            {
                Box = row.HasBox,
                Instructions = row.HasInstructions,
                Game = row.HasGame,
                Inserts = row.HasInserts,
                Other = row.HasOther
            },
            Notes = row.Notes,
            AddedDate = row.AddedDate,
            LastModifiedDate = row.LastModifiedDate
        };
    }

    public async Task<List<ItemLocationInfo>> GetLocationsForItemAsync(int id, int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var item = await GetByIdAsync(id, companyId);
        if (item == null) return new List<ItemLocationInfo>();

        var rows = await connection.QueryAsync<(int location_id, string location_name, int quantity, string condition)>(
            @"SELECT ii.location_id, COALESCE(l.name, '') as location_name, ii.quantity, COALESCE(ii.condition, '') as condition
              FROM inventory_item ii
              LEFT JOIN location l ON ii.location_id = l.id
              LEFT JOIN game g ON ii.game_id = g.id
              WHERE ii.company_id = @p_company_id
                AND COALESCE(g.title, '') = @p_name
                AND ((ii.game_id IS NULL AND @p_game_id IS NULL) OR ii.game_id = @p_game_id)
              ORDER BY l.name",
            new { p_company_id = companyId, p_name = item.Name, p_game_id = item.Game?.Id });
        return rows.Select(r => new ItemLocationInfo
        {
            LocationId = r.location_id,
            LocationName = r.location_name,
            Quantity = r.quantity,
            Condition = r.condition
        }).ToList();
    }

    private sealed class InventoryItemRow
    {
        public int Id { get; init; }
        public int CompanyId { get; init; }
        public int LocationId { get; init; }
        public string? LocationName { get; init; }
        public string? Name { get; init; }
        public string? Category { get; init; }
        public int Quantity { get; init; }
        public decimal SellPrice { get; init; }
        public decimal? BuyPrice { get; init; }
        public string? Condition { get; init; }
        public string? GameId { get; init; }
        public bool HasBox { get; init; }
        public bool HasInstructions { get; init; }
        public bool HasGame { get; init; }
        public bool HasInserts { get; init; }
        public bool HasOther { get; init; }
        public string? Notes { get; init; }
        public DateTime AddedDate { get; init; }
        public DateTime? LastModifiedDate { get; init; }
        public string? GameIdVal { get; init; }
        public string? GameTitle { get; init; }
        public string? GameConsole { get; init; }
        public DateTime? GameReleaseDate { get; init; }
        public string? GamePublisher { get; init; }
        public string? GameGenre { get; init; }
    }
}
