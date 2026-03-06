using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;

    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<ApiResponse<List<User>>> GetAllUsersAsync(int companyId)
    {
        try
        {
            var users = await _userRepository.GetByCompanyIdAsync(companyId);
            return ApiResponse<List<User>>.SuccessResponse(users);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<User>>.ErrorResponse("Failed to retrieve users", new List<string> { ex.Message });
        }
    }

    public async Task<ApiResponse<User>> GetUserByIdAsync(int id, int companyId)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(id, companyId);
            if (user == null)
                return ApiResponse<User>.ErrorResponse($"User with ID {id} not found");
            return ApiResponse<User>.SuccessResponse(user);
        }
        catch (Exception ex)
        {
            return ApiResponse<User>.ErrorResponse("Failed to retrieve user", new List<string> { ex.Message });
        }
    }

    public async Task<ApiResponse<User>> CreateUserAsync(CreateUserRequest request, int companyId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.FirstName))
                return ApiResponse<User>.ErrorResponse("First name is required");
            if (string.IsNullOrWhiteSpace(request.LastName))
                return ApiResponse<User>.ErrorResponse("Last name is required");
            if (string.IsNullOrWhiteSpace(request.Email))
                return ApiResponse<User>.ErrorResponse("Email is required");

            var existing = await _userRepository.GetByEmailAsync(request.Email, companyId);
            if (existing != null)
                return ApiResponse<User>.ErrorResponse("A user with this email already exists");

            var user = new User
            {
                CompanyId = companyId,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Phone = request.Phone,
                UserType = "employee",
                Status = "active"
            };

            var created = await _userRepository.CreateAsync(user);
            if (request.RoleIds.Count > 0)
                await _userRepository.AssignRolesAsync(created.Id, request.RoleIds);

            created.Roles = (await _userRepository.GetRoleNamesForUserAsync(created.Id)).ToList();
            return ApiResponse<User>.SuccessResponse(created, "User created successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<User>.ErrorResponse("Failed to create user", new List<string> { ex.Message });
        }
    }

    public async Task<ApiResponse<User>> UpdateUserAsync(int id, UpdateUserRequest request, int companyId)
    {
        try
        {
            var existing = await _userRepository.GetByIdAsync(id, companyId);
            if (existing == null)
                return ApiResponse<User>.ErrorResponse($"User with ID {id} not found");

            if (!string.IsNullOrWhiteSpace(request.FirstName))
                existing.FirstName = request.FirstName;
            if (!string.IsNullOrWhiteSpace(request.LastName))
                existing.LastName = request.LastName;
            if (request.Email != null)
            {
                var emailExists = await _userRepository.GetByEmailAsync(request.Email, companyId);
                if (emailExists != null && emailExists.Id != id)
                    return ApiResponse<User>.ErrorResponse("A user with this email already exists");
                existing.Email = request.Email;
            }
            if (request.Phone != null)
                existing.Phone = request.Phone;
            if (request.IsActive.HasValue)
                existing.Status = request.IsActive.Value ? "active" : "removed";

            var updated = await _userRepository.UpdateAsync(id, existing, companyId);
            if (updated == null)
                return ApiResponse<User>.ErrorResponse($"Failed to update user with ID {id}");

            if (request.RoleIds != null)
                await _userRepository.AssignRolesAsync(id, request.RoleIds);

            updated.Roles = (await _userRepository.GetRoleNamesForUserAsync(updated.Id)).ToList();
            return ApiResponse<User>.SuccessResponse(updated, "User updated successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<User>.ErrorResponse("Failed to update user", new List<string> { ex.Message });
        }
    }

    public async Task<ApiResponse<bool>> DeleteUserAsync(int id, int companyId)
    {
        try
        {
            var result = await _userRepository.DeleteAsync(id, companyId);
            if (!result)
                return ApiResponse<bool>.ErrorResponse($"User with ID {id} not found");
            return ApiResponse<bool>.SuccessResponse(true, "User removed successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<bool>.ErrorResponse("Failed to remove user", new List<string> { ex.Message });
        }
    }

    public async Task<ApiResponse<List<User>>> SearchUsersAsync(string searchTerm, int companyId)
    {
        try
        {
            var users = await _userRepository.GetByCompanyIdAsync(companyId);
            if (string.IsNullOrWhiteSpace(searchTerm))
                return ApiResponse<List<User>>.SuccessResponse(users);

            var term = searchTerm.ToLowerInvariant();
            var filtered = users.Where(u =>
                u.FirstName.ToLowerInvariant().Contains(term) ||
                u.LastName.ToLowerInvariant().Contains(term) ||
                u.Email.ToLowerInvariant().Contains(term) ||
                u.Roles.Any(r => r.ToLowerInvariant().Contains(term))
            ).ToList();
            return ApiResponse<List<User>>.SuccessResponse(filtered);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<User>>.ErrorResponse("Failed to search users", new List<string> { ex.Message });
        }
    }
}
