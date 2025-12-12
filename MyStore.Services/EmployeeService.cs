using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class EmployeeService : IEmployeeService
{
    private readonly IEmployeeRepository _repository;

    public EmployeeService(IEmployeeRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApiResponse<List<Employee>>> GetAllEmployeesAsync()
    {
        try
        {
            var employees = await _repository.GetAllAsync();
            return ApiResponse<List<Employee>>.SuccessResponse(employees);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<Employee>>.ErrorResponse(
                "Failed to retrieve employees",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<Employee>> GetEmployeeByIdAsync(int id)
    {
        try
        {
            var employee = await _repository.GetByIdAsync(id);
            if (employee == null)
            {
                return ApiResponse<Employee>.ErrorResponse($"Employee with ID {id} not found");
            }

            return ApiResponse<Employee>.SuccessResponse(employee);
        }
        catch (Exception ex)
        {
            return ApiResponse<Employee>.ErrorResponse(
                "Failed to retrieve employee",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<Employee>> CreateEmployeeAsync(CreateEmployeeRequest request)
    {
        try
        {
            // Validation
            if (string.IsNullOrWhiteSpace(request.FirstName))
            {
                return ApiResponse<Employee>.ErrorResponse("First name is required");
            }

            if (string.IsNullOrWhiteSpace(request.LastName))
            {
                return ApiResponse<Employee>.ErrorResponse("Last name is required");
            }

            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return ApiResponse<Employee>.ErrorResponse("Email is required");
            }

            if (string.IsNullOrWhiteSpace(request.Role))
            {
                return ApiResponse<Employee>.ErrorResponse("Role is required");
            }

            // Check if email already exists
            var existing = await _repository.GetByEmailAsync(request.Email);
            if (existing != null)
            {
                return ApiResponse<Employee>.ErrorResponse("An employee with this email already exists");
            }

            var employee = new Employee
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Phone = request.Phone,
                Role = request.Role,
                HireDate = request.HireDate,
                IsActive = true
            };

            var created = await _repository.CreateAsync(employee);
            return ApiResponse<Employee>.SuccessResponse(created, "Employee created successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<Employee>.ErrorResponse(
                "Failed to create employee",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<Employee>> UpdateEmployeeAsync(int id, UpdateEmployeeRequest request)
    {
        try
        {
            var existing = await _repository.GetByIdAsync(id);
            if (existing == null)
            {
                return ApiResponse<Employee>.ErrorResponse($"Employee with ID {id} not found");
            }

            // Update only provided fields
            if (!string.IsNullOrWhiteSpace(request.FirstName))
            {
                existing.FirstName = request.FirstName;
            }

            if (!string.IsNullOrWhiteSpace(request.LastName))
            {
                existing.LastName = request.LastName;
            }

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                // Check if email is being changed and if new email already exists
                if (existing.Email != request.Email)
                {
                    var emailExists = await _repository.GetByEmailAsync(request.Email);
                    if (emailExists != null && emailExists.Id != id)
                    {
                        return ApiResponse<Employee>.ErrorResponse("An employee with this email already exists");
                    }
                }
                existing.Email = request.Email;
            }

            if (request.Phone != null)
            {
                existing.Phone = request.Phone;
            }

            if (!string.IsNullOrWhiteSpace(request.Role))
            {
                existing.Role = request.Role;
            }

            if (request.IsActive.HasValue)
            {
                existing.IsActive = request.IsActive.Value;
            }

            var updated = await _repository.UpdateAsync(id, existing);
            if (updated == null)
            {
                return ApiResponse<Employee>.ErrorResponse($"Failed to update employee with ID {id}");
            }

            return ApiResponse<Employee>.SuccessResponse(updated, "Employee updated successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<Employee>.ErrorResponse(
                "Failed to update employee",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<bool>> DeleteEmployeeAsync(int id)
    {
        try
        {
            var result = await _repository.DeleteAsync(id);
            if (!result)
            {
                return ApiResponse<bool>.ErrorResponse($"Employee with ID {id} not found");
            }

            return ApiResponse<bool>.SuccessResponse(true, "Employee deleted successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<bool>.ErrorResponse(
                "Failed to delete employee",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<List<Employee>>> SearchEmployeesAsync(string searchTerm)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return await GetAllEmployeesAsync();
            }

            var results = await _repository.SearchAsync(searchTerm);
            return ApiResponse<List<Employee>>.SuccessResponse(results);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<Employee>>.ErrorResponse(
                "Failed to search employees",
                new List<string> { ex.Message }
            );
        }
    }
}

