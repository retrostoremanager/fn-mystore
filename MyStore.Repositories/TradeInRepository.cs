using Dapper;
using Npgsql;
using MyStore.Models;

namespace MyStore.Repositories;

public class TradeInRepository : ITradeInRepository
{
    private readonly string _connectionString;

    private const string TradeInSelectColumns =
        "id, company_id, customer_id, status, total_offered_value, total_accepted_value, payment_type, notes, created_by, created_at, completed_at";

    private const string ItemSelectColumns =
        "id, trade_in_id, game_title, platform, condition, offered_value, accepted_value, inventory_item_id, parsed_by_ai, created_at";

    static TradeInRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public TradeInRepository()
    {
        _connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("PostgresConnectionString")
            ?? throw new InvalidOperationException("Connection string environment variable is not set");
    }

    public async Task<List<TradeIn>> GetAllAsync(int companyId, string? status = null, DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);

        var conditions = new List<string> { "t.company_id = @p_company_id" };
        if (status is not null) conditions.Add("t.status = @p_status");
        if (dateFrom.HasValue) conditions.Add("t.created_at >= @p_date_from");
        if (dateTo.HasValue) conditions.Add("t.created_at <= @p_date_to");

        var where = string.Join(" AND ", conditions);

        var tCols = string.Join(", ", TradeInSelectColumns.Split(", ").Select(c => "t." + c.Trim()));
        var iCols = string.Join(", ", ItemSelectColumns.Split(", ").Select(c => "i." + c.Trim()));

        var tradeInDict = new Dictionary<int, TradeIn>();

        await connection.QueryAsync<TradeIn, TradeInItem, TradeIn>(
            $@"SELECT {tCols}, {iCols}
               FROM trade_in t
               LEFT JOIN trade_in_item i ON i.trade_in_id = t.id
               WHERE {where}
               ORDER BY t.created_at DESC",
            (tradeIn, item) =>
            {
                if (!tradeInDict.TryGetValue(tradeIn.Id, out var existing))
                {
                    existing = tradeIn;
                    existing.Items = new List<TradeInItem>();
                    tradeInDict[tradeIn.Id] = existing;
                }
                if (item is not null && item.Id != 0)
                    existing.Items.Add(item);
                return existing;
            },
            new
            {
                p_company_id = companyId,
                p_status = status,
                p_date_from = dateFrom,
                p_date_to = dateTo
            },
            splitOn: "id");

        return tradeInDict.Values.ToList();
    }

    public async Task<TradeIn?> GetByIdAsync(int id, int companyId)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);

        var tCols = string.Join(", ", TradeInSelectColumns.Split(", ").Select(c => "t." + c.Trim()));
        var iCols = string.Join(", ", ItemSelectColumns.Split(", ").Select(c => "i." + c.Trim()));

        var tradeInDict = new Dictionary<int, TradeIn>();

        await connection.QueryAsync<TradeIn, TradeInItem, TradeIn>(
            $@"SELECT {tCols}, {iCols}
               FROM trade_in t
               LEFT JOIN trade_in_item i ON i.trade_in_id = t.id
               WHERE t.id = @p_id AND t.company_id = @p_company_id",
            (tradeIn, item) =>
            {
                if (!tradeInDict.TryGetValue(tradeIn.Id, out var existing))
                {
                    existing = tradeIn;
                    existing.Items = new List<TradeInItem>();
                    tradeInDict[tradeIn.Id] = existing;
                }
                if (item is not null && item.Id != 0)
                    existing.Items.Add(item);
                return existing;
            },
            new { p_id = id, p_company_id = companyId },
            splitOn: "id");

        return tradeInDict.Values.FirstOrDefault();
    }

    public async Task<TradeIn> CreateAsync(TradeIn tradeIn)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, tradeIn.CompanyId);
        var created = await connection.QuerySingleAsync<TradeIn>(
            $@"INSERT INTO trade_in (
                company_id, customer_id, status, total_offered_value, total_accepted_value,
                payment_type, notes, created_by, created_at)
               VALUES (
                @p_company_id, @p_customer_id, @p_status, @p_total_offered_value, @p_total_accepted_value,
                @p_payment_type, @p_notes, @p_created_by, NOW())
               RETURNING {TradeInSelectColumns}",
            new
            {
                p_company_id = tradeIn.CompanyId,
                p_customer_id = tradeIn.CustomerId,
                p_status = tradeIn.Status,
                p_total_offered_value = tradeIn.TotalOfferedValue,
                p_total_accepted_value = tradeIn.TotalAcceptedValue,
                p_payment_type = tradeIn.PaymentType,
                p_notes = tradeIn.Notes,
                p_created_by = tradeIn.CreatedBy,
            });
        created.Items = new List<TradeInItem>();
        return created;
    }

    public async Task<TradeIn?> UpdateAsync(TradeIn tradeIn)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, tradeIn.CompanyId);
        var rows = await connection.ExecuteAsync(
            @"UPDATE trade_in SET
                customer_id = @p_customer_id,
                status = @p_status,
                total_offered_value = @p_total_offered_value,
                total_accepted_value = @p_total_accepted_value,
                payment_type = @p_payment_type,
                notes = @p_notes
              WHERE id = @p_id AND company_id = @p_company_id",
            new
            {
                p_id = tradeIn.Id,
                p_company_id = tradeIn.CompanyId,
                p_customer_id = tradeIn.CustomerId,
                p_status = tradeIn.Status,
                p_total_offered_value = tradeIn.TotalOfferedValue,
                p_total_accepted_value = tradeIn.TotalAcceptedValue,
                p_payment_type = tradeIn.PaymentType,
                p_notes = tradeIn.Notes,
            });
        if (rows == 0) return null;
        return await GetByIdAsync(tradeIn.Id, tradeIn.CompanyId);
    }

    public async Task<TradeInItem> AddItemAsync(TradeInItem item)
    {
        // trade_in_item has no company_id (child of trade_in) and no RLS policy, so no tenant
        // GUC is needed here. If RLS is ever added to trade_in_item, thread companyId in and
        // open via TenantConnection.OpenAsync.
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleAsync<TradeInItem>(
            $@"INSERT INTO trade_in_item (
                trade_in_id, game_title, platform, condition, offered_value, accepted_value, parsed_by_ai, created_at)
               VALUES (
                @p_trade_in_id, @p_game_title, @p_platform, @p_condition, @p_offered_value, @p_accepted_value, @p_parsed_by_ai, NOW())
               RETURNING {ItemSelectColumns}",
            new
            {
                p_trade_in_id = item.TradeInId,
                p_game_title = item.GameTitle,
                p_platform = item.Platform,
                p_condition = item.Condition,
                p_offered_value = item.OfferedValue,
                p_accepted_value = item.AcceptedValue,
                p_parsed_by_ai = item.ParsedByAi,
            });
    }

    public async Task<TradeInItem?> UpdateItemAsync(TradeInItem item)
    {
        // child of trade_in (no company_id / no RLS) — see AddItemAsync note.
        await using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.ExecuteAsync(
            @"UPDATE trade_in_item SET
                game_title = @p_game_title,
                platform = @p_platform,
                condition = @p_condition,
                offered_value = @p_offered_value,
                accepted_value = @p_accepted_value,
                inventory_item_id = @p_inventory_item_id,
                parsed_by_ai = @p_parsed_by_ai
              WHERE id = @p_id AND trade_in_id = @p_trade_in_id",
            new
            {
                p_id = item.Id,
                p_trade_in_id = item.TradeInId,
                p_game_title = item.GameTitle,
                p_platform = item.Platform,
                p_condition = item.Condition,
                p_offered_value = item.OfferedValue,
                p_accepted_value = item.AcceptedValue,
                p_inventory_item_id = item.InventoryItemId,
                p_parsed_by_ai = item.ParsedByAi,
            });
        if (rows == 0) return null;
        return await connection.QueryFirstOrDefaultAsync<TradeInItem>(
            $"SELECT {ItemSelectColumns} FROM trade_in_item WHERE id = @p_id",
            new { p_id = item.Id });
    }

    public async Task<TradeIn?> CompleteAsync(int id, int companyId, string paymentType, DateTime completedAt)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);
        var tradeIn = await connection.QueryFirstOrDefaultAsync<TradeIn>(
            $"SELECT {TradeInSelectColumns} FROM trade_in WHERE id = @p_id AND company_id = @p_company_id",
            new { p_id = id, p_company_id = companyId });
        if (tradeIn is null) return null;

        var rows = await connection.ExecuteAsync(
            @"UPDATE trade_in SET
                status = 'completed',
                payment_type = @p_payment_type,
                completed_at = @p_completed_at,
                total_accepted_value = (
                    SELECT COALESCE(SUM(accepted_value), 0)
                    FROM trade_in_item
                    WHERE trade_in_id = @p_id AND accepted_value > 0
                )
              WHERE id = @p_id AND company_id = @p_company_id AND status = 'draft'",
            new { p_id = id, p_company_id = companyId, p_payment_type = paymentType, p_completed_at = completedAt });
        if (rows == 0) return null;
        return await GetByIdAsync(id, companyId);
    }

    public async Task<(TradeIn? TradeIn, IReadOnlyList<InventoryUpsertResult> Upserts)> CompleteWithInventoryUpsertAsync(
        int id,
        int companyId,
        string paymentType,
        DateTime completedAt,
        IEnumerable<InventoryUpsertRequest> inventoryUpserts)
    {
        var upsertList = inventoryUpserts?.ToList() ?? new List<InventoryUpsertRequest>();
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var tradeIn = await connection.QueryFirstOrDefaultAsync<TradeIn>(
                $"SELECT {TradeInSelectColumns} FROM trade_in WHERE id = @p_id AND company_id = @p_company_id",
                new { p_id = id, p_company_id = companyId },
                transaction);
            if (tradeIn is null || tradeIn.Status != "draft")
            {
                await transaction.RollbackAsync();
                return (null, Array.Empty<InventoryUpsertResult>());
            }

            var results = new List<InventoryUpsertResult>(upsertList.Count);
            foreach (var spec in upsertList)
            {
                // Race-safe INSERT-or-INCREMENT inside the open transaction. game_inventory
                // does not currently have a UNIQUE (company_id, game_id, condition) constraint
                // (audited against dbproj-mystore migrations 001-062), so we cannot use
                // INSERT ... ON CONFLICT. Instead we SELECT ... FOR UPDATE on the matching row
                // to serialize concurrent completers, then INSERT if absent or UPDATE quantity
                // if present. The row lock + transactional context guarantees the find-then-write
                // is atomic relative to other transactions targeting the same (company, game,
                // condition) tuple.
                var existingId = await connection.QueryFirstOrDefaultAsync<int?>(
                    @"SELECT id FROM game_inventory
                      WHERE company_id = @p_company_id
                        AND game_id = @p_game_id
                        AND condition = @p_condition
                      ORDER BY id
                      LIMIT 1
                      FOR UPDATE",
                    new
                    {
                        p_company_id = companyId,
                        p_game_id = spec.GameId,
                        p_condition = spec.Condition,
                    },
                    transaction);

                int inventoryId;
                if (existingId.HasValue)
                {
                    await connection.ExecuteAsync(
                        @"UPDATE game_inventory
                          SET quantity = quantity + 1,
                              last_modified_date = NOW()
                          WHERE id = @p_id AND company_id = @p_company_id",
                        new { p_id = existingId.Value, p_company_id = companyId },
                        transaction);
                    inventoryId = existingId.Value;
                }
                else
                {
                    inventoryId = await connection.QuerySingleAsync<int>(
                        @"INSERT INTO game_inventory (
                            company_id, location_id, quantity, price, cost, condition, game_id,
                            notes, created_date, last_modified_date)
                          VALUES (
                            @p_company_id, @p_location_id, 1, 0, @p_buy_price, @p_condition,
                            @p_game_id, NULL, NOW(), NOW())
                          RETURNING id",
                        new
                        {
                            p_company_id = companyId,
                            p_location_id = spec.LocationId,
                            p_buy_price = spec.BuyPrice,
                            p_condition = spec.Condition,
                            p_game_id = spec.GameId,
                        },
                        transaction);
                }

                var linkRows = await connection.ExecuteAsync(
                    @"UPDATE trade_in_item SET inventory_item_id = @p_inventory_item_id
                      WHERE id = @p_id AND trade_in_id = @p_trade_in_id",
                    new
                    {
                        p_id = spec.TradeInItemId,
                        p_trade_in_id = id,
                        p_inventory_item_id = inventoryId,
                    },
                    transaction);
                if (linkRows == 0)
                {
                    await transaction.RollbackAsync();
                    return (null, Array.Empty<InventoryUpsertResult>());
                }

                results.Add(new InventoryUpsertResult(spec.TradeInItemId, inventoryId));
            }

            var rows = await connection.ExecuteAsync(
                @"UPDATE trade_in SET
                    status = 'completed',
                    payment_type = @p_payment_type,
                    completed_at = @p_completed_at,
                    total_accepted_value = (
                        SELECT COALESCE(SUM(accepted_value), 0)
                        FROM trade_in_item
                        WHERE trade_in_id = @p_id
                    )
                  WHERE id = @p_id AND company_id = @p_company_id AND status = 'draft'",
                new { p_id = id, p_company_id = companyId, p_payment_type = paymentType, p_completed_at = completedAt },
                transaction);
            if (rows == 0)
            {
                await transaction.RollbackAsync();
                return (null, Array.Empty<InventoryUpsertResult>());
            }

            await transaction.CommitAsync();
            var refreshed = await GetByIdAsync(id, companyId);
            return (refreshed, results);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync();
            }
            catch (Exception rollbackEx)
            {
                // Surface rollback failures so DB issues during rollback are not invisible.
                // No ILogger is wired into the repositories layer, so write to stderr; the
                // original exception is rethrown below so callers still see the root cause.
                Console.Error.WriteLine(
                    $"[TradeInRepository.CompleteWithInventoryUpsertAsync] Rollback failed for trade-in {id} (company {companyId}): {rollbackEx}");
            }
            throw;
        }
    }

    public async Task<TradeIn?> CompleteAsync(
        int id,
        int companyId,
        string paymentType,
        DateTime completedAt,
        IEnumerable<(int ItemId, int InventoryItemId)> acceptedItemLinks)
    {
        await using var connection = await TenantConnection.OpenAsync(_connectionString, companyId);
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var tradeIn = await connection.QueryFirstOrDefaultAsync<TradeIn>(
                $"SELECT {TradeInSelectColumns} FROM trade_in WHERE id = @p_id AND company_id = @p_company_id",
                new { p_id = id, p_company_id = companyId },
                transaction);
            if (tradeIn is null)
            {
                await transaction.RollbackAsync();
                return null;
            }

            foreach (var link in acceptedItemLinks)
            {
                var linkRows = await connection.ExecuteAsync(
                    @"UPDATE trade_in_item SET inventory_item_id = @p_inventory_item_id
                      WHERE id = @p_id AND trade_in_id = @p_trade_in_id",
                    new
                    {
                        p_id = link.ItemId,
                        p_trade_in_id = id,
                        p_inventory_item_id = link.InventoryItemId,
                    },
                    transaction);
                if (linkRows == 0)
                {
                    await transaction.RollbackAsync();
                    return null;
                }
            }

            var rows = await connection.ExecuteAsync(
                @"UPDATE trade_in SET
                    status = 'completed',
                    payment_type = @p_payment_type,
                    completed_at = @p_completed_at,
                    total_accepted_value = (
                        SELECT COALESCE(SUM(accepted_value), 0)
                        FROM trade_in_item
                        WHERE trade_in_id = @p_id AND accepted_value > 0
                    )
                  WHERE id = @p_id AND company_id = @p_company_id AND status = 'draft'",
                new { p_id = id, p_company_id = companyId, p_payment_type = paymentType, p_completed_at = completedAt },
                transaction);
            if (rows == 0)
            {
                await transaction.RollbackAsync();
                return null;
            }

            await transaction.CommitAsync();
            return await GetByIdAsync(id, companyId);
        }
        catch
        {
            try { await transaction.RollbackAsync(); } catch { }
            throw;
        }
    }
}
