using MyStore.Models;

namespace MyStore.Services;

public interface IEmployeeService
{
    Task<ApiResponse<List<Employee>>> GetAllEmployeesAsync();
    Task<ApiResponse<Employee>> GetEmployeeByIdAsync(int id);
    Task<ApiResponse<Employee>> CreateEmployeeAsync(CreateEmployeeRequest request);
    Task<ApiResponse<Employee>> UpdateEmployeeAsync(int id, UpdateEmployeeRequest request);
    Task<ApiResponse<bool>> DeleteEmployeeAsync(int id);
    Task<ApiResponse<List<Employee>>> SearchEmployeesAsync(string searchTerm);
}

