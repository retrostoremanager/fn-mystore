using MyStore.Models;

namespace MyStore.Services;

public interface ILoyaltyService
{
    Task<ApiResponse<LoyaltySettings>> GetSettingsAsync(int companyId);
    Task<ApiResponse<LoyaltySettings>> UpdateSettingsAsync(LoyaltySettings settings, int companyId);
    Task<ApiResponse<LoyaltyBalanceResponse>> GetBalanceAsync(int companyId, int customerId);
    Task EarnFromSaleAsync(int companyId, int customerId, decimal saleTotal);
    Task EarnFromTradeInAsync(int customerId, int companyId, decimal tradeInTotal);
    Task<ApiResponse<RedeemPointsResponse>> RedeemAsync(int companyId, int customerId, int pointsToRedeem);
}
