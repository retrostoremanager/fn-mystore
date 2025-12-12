using MyStore.Models;

namespace MyStore.Services;

public interface ICustomerService
{
    Task<ApiResponse<List<Customer>>> GetAllCustomersAsync();
    Task<ApiResponse<Customer>> GetCustomerByIdAsync(int id);
    Task<ApiResponse<Customer>> CreateCustomerAsync(CreateCustomerRequest request);
    Task<ApiResponse<Customer>> UpdateCustomerAsync(int id, UpdateCustomerRequest request);
    Task<ApiResponse<bool>> DeleteCustomerAsync(int id);
    Task<ApiResponse<List<Customer>>> SearchCustomersAsync(string searchTerm);
}

