using MyStore.Models;

namespace MyStore.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private static readonly List<Customer> _customers = new();
    private static int _nextId = 1;

    public Task<List<Customer>> GetAllAsync()
    {
        return Task.FromResult(_customers.ToList());
    }

    public Task<Customer?> GetByIdAsync(int id)
    {
        var customer = _customers.FirstOrDefault(c => c.Id == id);
        return Task.FromResult(customer);
    }

    public Task<Customer?> GetByEmailAsync(string email)
    {
        var customer = _customers.FirstOrDefault(c => c.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
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

    public Task<Customer?> UpdateAsync(int id, Customer customer)
    {
        var existing = _customers.FirstOrDefault(c => c.Id == id);
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

    public Task<bool> DeleteAsync(int id)
    {
        var customer = _customers.FirstOrDefault(c => c.Id == id);
        if (customer == null)
        {
            return Task.FromResult(false);
        }

        _customers.Remove(customer);
        return Task.FromResult(true);
    }

    public Task<List<Customer>> SearchAsync(string searchTerm)
    {
        var term = searchTerm.ToLowerInvariant();
        var results = _customers.Where(c =>
            c.FirstName.ToLowerInvariant().Contains(term) ||
            c.LastName.ToLowerInvariant().Contains(term) ||
            c.Email.ToLowerInvariant().Contains(term) ||
            (c.Phone != null && c.Phone.Contains(term))
        ).ToList();

        return Task.FromResult(results);
    }
}

