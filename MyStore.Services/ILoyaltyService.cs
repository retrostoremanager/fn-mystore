using MyStore.Models;

namespace MyStore.Services;

public interface ILoyaltyService
{
    Task<ApiResponse<LoyaltySettings>> GetSettingsAsync(int companyId);
    Task<ApiResponse<LoyaltySettings>> UpdateSettingsAsync(LoyaltySettings settings, int companyId);
    Task<ApiResponse<LoyaltyBalanceResponse>> GetBalanceAsync(int customerId, int companyId);
    Task EarnFromSaleAsync(int customerId, int companyId, decimal saleTotal, int? referenceId = null);
    Task EarnFromTradeInAsync(int customerId, int companyId, decimal tradeInTotal, int? referenceId = null);
    Task<ApiResponse<RedeemPointsResponse>> RedeemAsync(int customerId, int companyId, int points);
}
