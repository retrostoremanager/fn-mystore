using MyStore.Models;

namespace MyStore.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private static readonly List<Customer> _customers = new();
    private static int _nextId = 1;

    public Task<List<Customer>> GetAllAsync(int companyId)
    {
        var results = _customers.Where(c => c.CompanyId == companyId).ToList();
        return Task.FromResult(results);
    }

    public Task<Customer?> GetByIdAsync(int id, int companyId)
    {
        var customer = _customers.FirstOrDefault(c => c.Id == id && c.CompanyId == companyId);
        return Task.FromResult(customer);
    }

    public Task<Customer?> GetByEmailAsync(string email, int companyId)
    {
        var customer = _customers.FirstOrDefault(c =>
            c.Email != null &&
            c.Email.Equals(email, StringComparison.OrdinalIgnoreCase) &&
            c.CompanyId == companyId);
        return Task.FromResult(customer);
    }

    public Task<Customer> CreateAsync(Customer customer)
    {
        customer.Id = _nextId++;
        customer.CreatedDate = DateTime.UtcNow;
        customer.LastModifiedDate = DateTime.UtcNow;
        _customers.Add(customer);
        return Task.FromResult(customer);
    }

    public Task<Customer?> UpdateAsync(int id, Customer customer, int companyId)
    {
        var existing = _customers.FirstOrDefault(c => c.Id == id && c.CompanyId == companyId);
        if (existing == null)
        {
            return Task.FromResult<Customer?>(null);
        }

        existing.FirstName = customer.FirstName;
        existing.LastName = customer.LastName;
        existing.Email = customer.Email;
        existing.Phone = customer.Phone;
        existing.Address = customer.Address;
        existing.City = customer.City;
        existing.State = customer.State;
        existing.ZipCode = customer.ZipCode;
        existing.LastModifiedDate = DateTime.UtcNow;

        return Task.FromResult<Customer?>(existing);
    }

    public Task<bool> DeleteAsync(int id, int companyId)
    {
        var customer = _customers.FirstOrDefault(c => c.Id == id && c.CompanyId == companyId);
        if (customer == null)
        {
            return Task.FromResult(false);
        }

        _customers.Remove(customer);
        return Task.FromResult(true);
    }

    public Task<List<Customer>> SearchAsync(string searchTerm, int companyId)
    {
        var term = searchTerm.ToLowerInvariant();
        var results = _customers.Where(c =>
            c.CompanyId == companyId &&
            (c.FirstName.ToLowerInvariant().Contains(term) ||
            c.LastName.ToLowerInvariant().Contains(term) ||
            (c.Email != null && c.Email.ToLowerInvariant().Contains(term)) ||
            (c.Phone != null && c.Phone.Contains(term)))
        ).ToList();

        return Task.FromResult(results);
    }
}

