using MyStore.Models;

namespace MyStore.Services;

public interface IEmployeeService
{
    Task<ApiResponse<List<Employee>>> GetAllEmployeesAsync(int companyId);
    Task<ApiResponse<Employee>> GetEmployeeByIdAsync(int id, int companyId);
    Task<ApiResponse<Employee>> CreateEmployeeAsync(CreateEmployeeRequest request, int companyId);
    Task<ApiResponse<Employee>> UpdateEmployeeAsync(int id, UpdateEmployeeRequest request, int companyId);
    Task<ApiResponse<bool>> DeleteEmployeeAsync(int id, int companyId);
    Task<ApiResponse<List<Employee>>> SearchEmployeesAsync(string searchTerm, int companyId);
}

