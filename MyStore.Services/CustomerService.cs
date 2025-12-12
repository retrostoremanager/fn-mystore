using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _repository;

    public CustomerService(ICustomerRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApiResponse<List<Customer>>> GetAllCustomersAsync()
    {
        try
        {
            var customers = await _repository.GetAllAsync();
            return ApiResponse<List<Customer>>.SuccessResponse(customers);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<Customer>>.ErrorResponse(
                "Failed to retrieve customers",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<Customer>> GetCustomerByIdAsync(int id)
    {
        try
        {
            var customer = await _repository.GetByIdAsync(id);
            if (customer == null)
            {
                return ApiResponse<Customer>.ErrorResponse($"Customer with ID {id} not found");
            }

            return ApiResponse<Customer>.SuccessResponse(customer);
        }
        catch (Exception ex)
        {
            return ApiResponse<Customer>.ErrorResponse(
                "Failed to retrieve customer",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<Customer>> CreateCustomerAsync(CreateCustomerRequest request)
    {
        try
        {
            // Validation
            if (string.IsNullOrWhiteSpace(request.FirstName))
            {
                return ApiResponse<Customer>.ErrorResponse("First name is required");
            }

            if (string.IsNullOrWhiteSpace(request.LastName))
            {
                return ApiResponse<Customer>.ErrorResponse("Last name is required");
            }

            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return ApiResponse<Customer>.ErrorResponse("Email is required");
            }

            // Check if email already exists
            var existing = await _repository.GetByEmailAsync(request.Email);
            if (existing != null)
            {
                return ApiResponse<Customer>.ErrorResponse("A customer with this email already exists");
            }

            var customer = new Customer
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Phone = request.Phone,
                Address = request.Address,
                City = request.City,
                State = request.State,
                ZipCode = request.ZipCode
            };

            var created = await _repository.CreateAsync(customer);
            return ApiResponse<Customer>.SuccessResponse(created, "Customer created successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<Customer>.ErrorResponse(
                "Failed to create customer",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<Customer>> UpdateCustomerAsync(int id, UpdateCustomerRequest request)
    {
        try
        {
            var existing = await _repository.GetByIdAsync(id);
            if (existing == null)
            {
                return ApiResponse<Customer>.ErrorResponse($"Customer with ID {id} not found");
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
                        return ApiResponse<Customer>.ErrorResponse("A customer with this email already exists");
                    }
                }
                existing.Email = request.Email;
            }

            if (request.Phone != null)
            {
                existing.Phone = request.Phone;
            }

            if (request.Address != null)
            {
                existing.Address = request.Address;
            }

            if (request.City != null)
            {
                existing.City = request.City;
            }

            if (request.State != null)
            {
                existing.State = request.State;
            }

            if (request.ZipCode != null)
            {
                existing.ZipCode = request.ZipCode;
            }

            var updated = await _repository.UpdateAsync(id, existing);
            if (updated == null)
            {
                return ApiResponse<Customer>.ErrorResponse($"Failed to update customer with ID {id}");
            }

            return ApiResponse<Customer>.SuccessResponse(updated, "Customer updated successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<Customer>.ErrorResponse(
                "Failed to update customer",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<bool>> DeleteCustomerAsync(int id)
    {
        try
        {
            var result = await _repository.DeleteAsync(id);
            if (!result)
            {
                return ApiResponse<bool>.ErrorResponse($"Customer with ID {id} not found");
            }

            return ApiResponse<bool>.SuccessResponse(true, "Customer deleted successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<bool>.ErrorResponse(
                "Failed to delete customer",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<List<Customer>>> SearchCustomersAsync(string searchTerm)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return await GetAllCustomersAsync();
            }

            var results = await _repository.SearchAsync(searchTerm);
            return ApiResponse<List<Customer>>.SuccessResponse(results);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<Customer>>.ErrorResponse(
                "Failed to search customers",
                new List<string> { ex.Message }
            );
        }
    }
}

