using MyStore.Models;

namespace MyStore.Services;

public interface IUserService
{
    Task<ApiResponse<List<User>>> GetAllUsersAsync(int companyId);
    Task<ApiResponse<User>> GetUserByIdAsync(int id, int companyId);
    Task<ApiResponse<User>> CreateUserAsync(CreateUserRequest request, int companyId);
    Task<ApiResponse<SetPasswordFromInviteResponse>> SetPasswordFromInviteAsync(SetPasswordFromInviteRequest request);
    Task<ApiResponse<User>> UpdateUserAsync(int id, UpdateUserRequest request, int companyId);
    Task<ApiResponse<bool>> DeleteUserAsync(int id, int companyId);
    Task<ApiResponse<List<User>>> SearchUsersAsync(string searchTerm, int companyId);
}
