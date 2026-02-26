using MyStore.Models;

namespace MyStore.Repositories;

public interface ICompanyRepository
{
    Task<Company?> GetByIdAsync(int id);
    Task<Company?> GetByEmailAsync(string email);
    Task<Company?> GetByVerificationTokenAsync(string token);
    Task<Company?> GetByPasswordResetTokenAsync(string token);
    Task<Company> CreateAsync(Company company);
    Task<Company?> UpdateAsync(int id, Company company);
    Task<bool> DeleteAsync(int id);
}
