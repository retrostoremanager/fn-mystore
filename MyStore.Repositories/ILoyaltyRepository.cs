using MyStore.Models;

namespace MyStore.Repositories;

public interface ILoyaltyRepository
{
    Task<LoyaltySettings?> GetSettingsAsync(int companyId);
    Task<LoyaltySettings> UpsertSettingsAsync(LoyaltySettings settings);
    Task<int> GetBalanceAsync(int companyId, int customerId);
    Task<LoyaltyTransaction> AddTransactionAsync(LoyaltyTransaction transaction);
    Task<List<LoyaltyTransaction>> GetTransactionsAsync(int companyId, int customerId);
}
