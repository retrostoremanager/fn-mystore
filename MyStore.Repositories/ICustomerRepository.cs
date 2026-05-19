using MyStore.Models;

namespace MyStore.Repositories;

public interface ICustomerRepository
{
    Task<List<Customer>> GetAllAsync(int companyId);
    Task<Customer?> GetByIdAsync(int id);
    Task<Customer?> GetByEmailAsync(string email, int companyId);
    Task<Customer> CreateAsync(Customer customer);
    Task<Customer?> UpdateAsync(int id, Customer customer, int companyId);
    Task<bool> DeleteAsync(int id, int companyId);
    Task<List<Customer>> SearchAsync(string searchTerm, int companyId);
}

