using MyStore.Models;

namespace MyStore.Repositories;

public interface IEmployeeRepository
{
    Task<List<Employee>> GetAllAsync(int companyId);
    Task<Employee?> GetByIdAsync(int id, int companyId);
    Task<Employee?> GetByEmailAsync(string email, int companyId);
    Task<Employee> CreateAsync(Employee employee);
    Task<Employee?> UpdateAsync(int id, Employee employee, int companyId);
    Task<bool> DeleteAsync(int id, int companyId);
    Task<List<Employee>> SearchAsync(string searchTerm, int companyId);
}

