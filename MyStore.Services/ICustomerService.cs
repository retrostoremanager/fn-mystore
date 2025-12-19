using MyStore.Models;

namespace MyStore.Services;

public interface ICustomerService
{
    Task<ApiResponse<List<Customer>>> GetAllCustomersAsync(int companyId);
    Task<ApiResponse<Customer>> GetCustomerByIdAsync(int id, int companyId);
    Task<ApiResponse<Customer>> CreateCustomerAsync(CreateCustomerRequest request, int companyId);
    Task<ApiResponse<Customer>> UpdateCustomerAsync(int id, UpdateCustomerRequest request, int companyId);
    Task<ApiResponse<bool>> DeleteCustomerAsync(int id, int companyId);
    Task<ApiResponse<List<Customer>>> SearchCustomersAsync(string searchTerm, int companyId);
}

