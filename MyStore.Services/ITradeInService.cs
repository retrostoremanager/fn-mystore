using MyStore.Models;

namespace MyStore.Services;

public interface ITradeInService
{
    Task<ApiResponse<TradeIn>> CreateDraftAsync(TradeIn tradeIn, int companyId);
    Task<ApiResponse<TradeInItem>> AddItemAsync(TradeInItem item, int tradeInId, int companyId);
    Task<ApiResponse<TradeInItem>> UpdateItemAsync(TradeInItem item, int companyId);
    Task<ApiResponse<TradeIn>> CompleteAsync(int id, int companyId, string paymentType);
    Task<ApiResponse<TradeIn>> RejectAsync(int id, int companyId);
    Task<ApiResponse<List<TradeIn>>> GetAllAsync(int companyId, string? status = null, DateTime? dateFrom = null, DateTime? dateTo = null);
    Task<ApiResponse<TradeIn>> GetByIdAsync(int id, int companyId);
}
