using MyStore.Models;

namespace MyStore.Repositories;

public interface ITradeInRepository
{
    Task<List<TradeIn>> GetAllAsync(int companyId, string? status = null, DateTime? dateFrom = null, DateTime? dateTo = null);
    Task<TradeIn?> GetByIdAsync(int id, int companyId);
    Task<TradeIn> CreateAsync(TradeIn tradeIn);
    Task<TradeIn?> UpdateAsync(TradeIn tradeIn);
    Task<TradeInItem> AddItemAsync(TradeInItem item);
    Task<TradeInItem?> UpdateItemAsync(TradeInItem item);
    Task<TradeIn?> CompleteAsync(int id, string paymentType, DateTime completedAt);
}
