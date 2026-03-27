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

    public async Task<ApiResponse<List<Customer>>> GetAllCustomersAsync(int companyId)
    {
        try
        {
            var customers = await _repository.GetAllAsync(companyId);
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

    public async Task<ApiResponse<Customer>> GetCustomerByIdAsync(int id, int companyId)
    {
        try
        {
            var customer = await _repository.GetByIdAsync(id, companyId);
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

    public async Task<ApiResponse<Customer>> CreateCustomerAsync(CreateCustomerRequest request, int companyId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.FirstName))
            {
                return ApiResponse<Customer>.ErrorResponse("Name is required");
            }

            var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();
            var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

            if (email == null && phone == null)
            {
                return ApiResponse<Customer>.ErrorResponse("Email or phone is required");
            }

            if (email != null)
            {
                var existing = await _repository.GetByEmailAsync(email, companyId);
                if (existing != null)
                {
                    return ApiResponse<Customer>.ErrorResponse("A customer with this email already exists");
                }
            }

            var customer = new Customer
            {
                CompanyId = companyId,
                FirstName = request.FirstName.Trim(),
                LastName = string.IsNullOrWhiteSpace(request.LastName) ? string.Empty : request.LastName.Trim(),
                Email = email,
                Phone = phone,
                Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
                City = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim(),
                State = string.IsNullOrWhiteSpace(request.State) ? null : request.State.Trim(),
                ZipCode = string.IsNullOrWhiteSpace(request.ZipCode) ? null : request.ZipCode.Trim()
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

    public async Task<ApiResponse<Customer>> UpdateCustomerAsync(int id, UpdateCustomerRequest request, int companyId)
    {
        try
        {
            var existing = await _repository.GetByIdAsync(id, companyId);
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
                var normalized = request.Email.Trim().ToLowerInvariant();
                if (!string.Equals(existing.Email, normalized, StringComparison.Ordinal))
                {
                    var emailExists = await _repository.GetByEmailAsync(normalized, companyId);
                    if (emailExists != null && emailExists.Id != id)
                    {
                        return ApiResponse<Customer>.ErrorResponse("A customer with this email already exists");
                    }
                }
                existing.Email = normalized;
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

            var updated = await _repository.UpdateAsync(id, existing, companyId);
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

    public async Task<ApiResponse<bool>> DeleteCustomerAsync(int id, int companyId)
    {
        try
        {
            var result = await _repository.DeleteAsync(id, companyId);
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

    public async Task<ApiResponse<List<Customer>>> SearchCustomersAsync(string searchTerm, int companyId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return await GetAllCustomersAsync(companyId);
            }

            var results = await _repository.SearchAsync(searchTerm, companyId);
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

