using MyStore.Models;

namespace MyStore.Repositories;

public class EmployeeRepository : IEmployeeRepository
{
    private static readonly List<Employee> _employees = new();
    private static int _nextId = 1;

    public Task<List<Employee>> GetAllAsync(int companyId)
    {
        var results = _employees.Where(e => e.CompanyId == companyId).ToList();
        return Task.FromResult(results);
    }

    public Task<Employee?> GetByIdAsync(int id, int companyId)
    {
        var employee = _employees.FirstOrDefault(e => e.Id == id && e.CompanyId == companyId);
        return Task.FromResult(employee);
    }

    public Task<Employee?> GetByEmailAsync(string email, int companyId)
    {
        var employee = _employees.FirstOrDefault(e => e.Email.Equals(email, StringComparison.OrdinalIgnoreCase) && e.CompanyId == companyId);
        return Task.FromResult(employee);
    }

    public Task<Employee> CreateAsync(Employee employee)
    {
        employee.Id = _nextId++;
        employee.CreatedDate = DateTime.UtcNow;
        employee.LastModifiedDate = DateTime.UtcNow;
        _employees.Add(employee);
        return Task.FromResult(employee);
    }

    public Task<Employee?> UpdateAsync(int id, Employee employee, int companyId)
    {
        var existing = _employees.FirstOrDefault(e => e.Id == id && e.CompanyId == companyId);
        if (existing == null)
        {
            return Task.FromResult<Employee?>(null);
        }

        existing.FirstName = employee.FirstName;
        existing.LastName = employee.LastName;
        existing.Email = employee.Email;
        existing.Phone = employee.Phone;
        existing.Role = employee.Role;
        existing.IsActive = employee.IsActive;
        existing.LastModifiedDate = DateTime.UtcNow;

        return Task.FromResult<Employee?>(existing);
    }

    public Task<bool> DeleteAsync(int id, int companyId)
    {
        var employee = _employees.FirstOrDefault(e => e.Id == id && e.CompanyId == companyId);
        if (employee == null)
        {
            return Task.FromResult(false);
        }

        _employees.Remove(employee);
        return Task.FromResult(true);
    }

    public Task<List<Employee>> SearchAsync(string searchTerm, int companyId)
    {
        var term = searchTerm.ToLowerInvariant();
        var results = _employees.Where(e =>
            e.CompanyId == companyId &&
            (e.FirstName.ToLowerInvariant().Contains(term) ||
            e.LastName.ToLowerInvariant().Contains(term) ||
            e.Email.ToLowerInvariant().Contains(term) ||
            e.Role.ToLowerInvariant().Contains(term))
        ).ToList();

        return Task.FromResult(results);
    }
}

