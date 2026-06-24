using MyStore.Models;

namespace MyStore.Repositories;

public sealed record InventoryUpsertRequest(
    int TradeInItemId,
    string GameId,
    string Condition,
    int LocationId,
    decimal? BuyPrice);

public sealed record InventoryUpsertResult(int TradeInItemId, int InventoryItemId);

public interface ITradeInRepository
{
    Task<List<TradeIn>> GetAllAsync(int companyId, string? status = null, DateTime? dateFrom = null, DateTime? dateTo = null);
    Task<TradeIn?> GetByIdAsync(int id, int companyId);
    Task<TradeIn> CreateAsync(TradeIn tradeIn);
    Task<TradeIn?> UpdateAsync(TradeIn tradeIn);
    Task<TradeInItem> AddItemAsync(TradeInItem item);
    Task<TradeInItem?> UpdateItemAsync(TradeInItem item);
    Task<TradeIn?> CompleteAsync(int id, int companyId, string paymentType, DateTime completedAt);
    Task<TradeIn?> CompleteAsync(
        int id,
        int companyId,
        string paymentType,
        DateTime completedAt,
        IEnumerable<(int ItemId, int InventoryItemId)> acceptedItemLinks);

    /// <summary>
    /// Completes a trade-in atomically: inside a single DB transaction, for each accepted item
    /// it locates an existing game_inventory row matching (company_id, game_id, condition) via
    /// SELECT ... FOR UPDATE and increments its quantity, or inserts a new row when none
    /// exists. It then links the trade_in_item rows to the resulting inventory_item ids and
    /// flips the trade-in status to 'completed'. The SELECT ... FOR UPDATE row lock serializes
    /// concurrent completers and avoids the duplicate insert that previously surfaced as
    /// HTTP 500 (issue #376). If any step fails, the whole transaction is rolled back and the
    /// trade-in remains in 'draft' status.
    /// </summary>
    /// <returns>The completed trade-in plus the list of inventory ids per trade_in_item, or null
    /// if the trade-in was not found / not in 'draft' status.</returns>
    Task<(TradeIn? TradeIn, IReadOnlyList<InventoryUpsertResult> Upserts)> CompleteWithInventoryUpsertAsync(
        int id,
        int companyId,
        string paymentType,
        DateTime completedAt,
        IEnumerable<InventoryUpsertRequest> inventoryUpserts);
}
