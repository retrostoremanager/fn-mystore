using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class LoyaltyService : ILoyaltyService
{
    private readonly ILoyaltyRepository _repository;

    public LoyaltyService(ILoyaltyRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApiResponse<LoyaltySettings>> GetSettingsAsync(int companyId)
    {
        try
        {
            var settings = await _repository.GetSettingsAsync(companyId);
            if (settings is null)
            {
                settings = new LoyaltySettings
                {
                    CompanyId = companyId,
                    PointsPerDollarSpent = 1m,
                    PointsPerDollarTradeIn = 1m,
                    RedemptionRate = 100m,
                    IsEnabled = false,
                };
            }
            return ApiResponse<LoyaltySettings>.SuccessResponse(settings);
        }
        catch (Exception ex)
        {
            return ApiResponse<LoyaltySettings>.ErrorResponse(
                "Failed to retrieve loyalty settings",
                new List<string> { ex.Message });
        }
    }

    public async Task<ApiResponse<LoyaltySettings>> UpdateSettingsAsync(LoyaltySettings settings, int companyId)
    {
        try
        {
            settings.CompanyId = companyId;
            var updated = await _repository.UpsertSettingsAsync(settings);
            return ApiResponse<LoyaltySettings>.SuccessResponse(updated, "Loyalty settings updated successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<LoyaltySettings>.ErrorResponse(
                "Failed to update loyalty settings",
                new List<string> { ex.Message });
        }
    }

    public async Task<ApiResponse<LoyaltyBalanceResponse>> GetBalanceAsync(int companyId, int customerId)
    {
        try
        {
            var balance = await _repository.GetBalanceAsync(companyId, customerId);
            return ApiResponse<LoyaltyBalanceResponse>.SuccessResponse(new LoyaltyBalanceResponse
            {
                CustomerId = customerId,
                Balance = balance,
            });
        }
        catch (Exception ex)
        {
            return ApiResponse<LoyaltyBalanceResponse>.ErrorResponse(
                "Failed to retrieve loyalty balance",
                new List<string> { ex.Message });
        }
    }

    public async Task EarnFromSaleAsync(int companyId, int customerId, decimal saleTotal)
    {
        var settings = await _repository.GetSettingsAsync(companyId);
        if (settings is null || !settings.IsEnabled)
            return;

        var points = (int)Math.Floor(saleTotal * settings.PointsPerDollarSpent);
        if (points <= 0)
            return;

        await _repository.AddTransactionAsync(new LoyaltyTransaction
        {
            CompanyId = companyId,
            CustomerId = customerId,
            Points = points,
            TransactionType = "earn_sale",
        });
    }

    public async Task EarnFromTradeInAsync(int customerId, int companyId, decimal tradeInTotal)
    {
        var settings = await _repository.GetSettingsAsync(companyId);
        if (settings is null || !settings.IsEnabled)
            return;

        var points = (int)Math.Floor(tradeInTotal * settings.PointsPerDollarTradeIn);
        if (points <= 0)
            return;

        await _repository.AddTransactionAsync(new LoyaltyTransaction
        {
            CompanyId = companyId,
            CustomerId = customerId,
            Points = points,
            TransactionType = "earn_tradein",
        });
    }

    public async Task<ApiResponse<RedeemPointsResponse>> RedeemAsync(int companyId, int customerId, int pointsToRedeem)
    {
        try
        {
            if (pointsToRedeem <= 0)
                return ApiResponse<RedeemPointsResponse>.ErrorResponse("Points to redeem must be greater than zero");

            var settings = await _repository.GetSettingsAsync(companyId);
            if (settings is null || !settings.IsEnabled)
                return ApiResponse<RedeemPointsResponse>.ErrorResponse("Loyalty programme is not enabled for this company");

            var balance = await _repository.GetBalanceAsync(companyId, customerId);
            if (pointsToRedeem > balance)
                return ApiResponse<RedeemPointsResponse>.ErrorResponse(
                    $"Insufficient loyalty points. Available: {balance}, Requested: {pointsToRedeem}");

            await _repository.AddTransactionAsync(new LoyaltyTransaction
            {
                CompanyId = companyId,
                CustomerId = customerId,
                Points = -pointsToRedeem,
                TransactionType = "redeem",
            });

            var creditAmount = Math.Round(pointsToRedeem / settings.RedemptionRate, 2, MidpointRounding.AwayFromZero);
            var newBalance = balance - pointsToRedeem;

            return ApiResponse<RedeemPointsResponse>.SuccessResponse(new RedeemPointsResponse
            {
                PointsRedeemed = pointsToRedeem,
                CreditAmount = creditAmount,
                NewBalance = newBalance,
            }, $"Redeemed {pointsToRedeem} points for {creditAmount:C} store credit");
        }
        catch (Exception ex)
        {
            return ApiResponse<RedeemPointsResponse>.ErrorResponse(
                "Failed to redeem loyalty points",
                new List<string> { ex.Message });
        }
    }
}
