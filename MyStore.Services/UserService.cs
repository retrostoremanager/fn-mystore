using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmailService _emailService;

    public UserService(IUserRepository userRepository, ICompanyRepository companyRepository, IEmailService emailService)
    {
        _userRepository = userRepository;
        _companyRepository = companyRepository;
        _emailService = emailService;
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

            var inviteToken = GenerateSecureToken();
            var tokenExpires = DateTime.UtcNow.AddDays(7);

            var user = new User
            {
                CompanyId = companyId,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Phone = request.Phone,
                UserType = "employee",
                Status = "pending_invitation",
                PasswordInviteToken = inviteToken,
                PasswordInviteTokenExpires = tokenExpires
            };

            var created = await _userRepository.CreateAsync(user);
            if (request.RoleIds.Count > 0)
                await _userRepository.AssignRolesAsync(created.Id, request.RoleIds);

            created.Roles = (await _userRepository.GetRoleNamesForUserAsync(created.Id)).ToList();

            // Send invite email asynchronously (fire-and-forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    var company = await _companyRepository.GetByIdAsync(companyId);
                    var companyName = company?.CompanyName ?? "Your Company";
                    await _emailService.SendUserInviteEmailAsync(
                        created.Email,
                        inviteToken,
                        companyName,
                        created.FirstName);
                }
                catch
                {
                    // Don't fail user creation if email fails - can resend later
                }
            });

            return ApiResponse<User>.SuccessResponse(created, "User created successfully. An email has been sent to set up their password.");
        }
        catch (Exception ex)
        {
            return ApiResponse<User>.ErrorResponse("Failed to create user", new List<string> { ex.Message });
        }
    }

    public async Task<ApiResponse<SetPasswordFromInviteResponse>> SetPasswordFromInviteAsync(SetPasswordFromInviteRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Token))
                return ApiResponse<SetPasswordFromInviteResponse>.ErrorResponse("Invalid or expired invite link.");
            if (string.IsNullOrWhiteSpace(request.Password))
                return ApiResponse<SetPasswordFromInviteResponse>.ErrorResponse("Password is required.");

            var passwordErrors = ValidatePassword(request.Password);
            if (passwordErrors.Count > 0)
                return ApiResponse<SetPasswordFromInviteResponse>.ErrorResponse(passwordErrors.First(), passwordErrors);

            var user = await _userRepository.GetByInviteTokenAsync(request.Token);
            if (user == null)
                return ApiResponse<SetPasswordFromInviteResponse>.ErrorResponse("Invalid or expired invite link. Please contact your administrator for a new invitation.");

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, BCrypt.Net.BCrypt.GenerateSalt(12));
            var updated = await _userRepository.UpdatePasswordFromInviteAsync(user.Id, passwordHash);
            if (!updated)
                return ApiResponse<SetPasswordFromInviteResponse>.ErrorResponse("Unable to set password. The invite may have expired.");

            var company = await _companyRepository.GetByIdAsync(user.CompanyId);
            var slug = company?.Slug ?? string.Empty;

            return ApiResponse<SetPasswordFromInviteResponse>.SuccessResponse(
                new SetPasswordFromInviteResponse
                {
                    Success = true,
                    Slug = slug,
                    Message = "Password set successfully. You can now sign in."
                },
                "Password set successfully.");
        }
        catch (Exception ex)
        {
            return ApiResponse<SetPasswordFromInviteResponse>.ErrorResponse("An error occurred while setting your password.", new List<string> { ex.Message });
        }
    }

    private static List<string> ValidatePassword(string password)
    {
        var errors = new List<string>();
        if (password.Length < 8)
            errors.Add("Password must be at least 8 characters");
        if (!Regex.IsMatch(password, @"[a-z]"))
            errors.Add("Password must contain at least one lowercase letter");
        if (!Regex.IsMatch(password, @"[A-Z]"))
            errors.Add("Password must contain at least one uppercase letter");
        if (!Regex.IsMatch(password, @"\d"))
            errors.Add("Password must contain at least one number");
        return errors;
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

    private static string GenerateSecureToken()
    {
        using var rng = RandomNumberGenerator.Create();
        var tokenBytes = new byte[32];
        rng.GetBytes(tokenBytes);
        return Convert.ToBase64String(tokenBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }
}
