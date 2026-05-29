namespace MyStore.Services;

public interface ILoyaltyService
{
    Task EarnFromTradeInAsync(int customerId, int companyId, decimal amount);
}
