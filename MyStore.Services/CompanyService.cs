using System.Security.Cryptography;
using System.Text;
using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class CompanyService : ICompanyService
{
    private readonly ICompanyRepository _repository;

    public CompanyService(ICompanyRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApiResponse<RegisterAccountResponse>> RegisterAccountAsync(RegisterAccountRequest request)
    {
        try
        {
            // Validation
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return ApiResponse<RegisterAccountResponse>.ErrorResponse("Email is required");
            }

            // Validate email format (basic validation)
            if (!IsValidEmail(request.Email))
            {
                return ApiResponse<RegisterAccountResponse>.ErrorResponse("Invalid email format");
            }

            // Check if email already exists
            var existing = await _repository.GetByEmailAsync(request.Email);
            if (existing != null)
            {
                return ApiResponse<RegisterAccountResponse>.ErrorResponse("An account with this email already exists");
            }

            // Generate secure verification token
            var verificationToken = GenerateSecureToken();
            var tokenExpires = DateTime.UtcNow.AddHours(24);

            // Calculate trial period (30 days)
            var trialStartDate = DateTime.UtcNow;
            var trialEndDate = trialStartDate.AddDays(30);

            // Create company record
            var company = new Company
            {
                Email = request.Email.Trim().ToLowerInvariant(),
                Status = "Pending",
                TrialStartDate = trialStartDate,
                TrialEndDate = trialEndDate,
                VerificationToken = verificationToken,
                VerificationTokenExpires = tokenExpires,
                SubscriptionTier = request.SubscriptionTier ?? "Trial",
                CreatedDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow
            };

            // Use transaction for data consistency (repository handles this)
            var created = await _repository.CreateAsync(company);

            // Create response
            var response = new RegisterAccountResponse
            {
                Id = created.Id,
                Email = created.Email,
                Status = created.Status,
                TrialStartDate = created.TrialStartDate,
                TrialEndDate = created.TrialEndDate,
                SubscriptionTier = created.SubscriptionTier
            };

            return ApiResponse<RegisterAccountResponse>.SuccessResponse(response, "Account registered successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<RegisterAccountResponse>.ErrorResponse(
                "Failed to register account",
                new List<string> { ex.Message }
            );
        }
    }

    private static string GenerateSecureToken()
    {
        // Generate a secure, random token using cryptographic random number generator
        // Token will be 32 bytes (256 bits) encoded as base64, resulting in ~44 characters
        using var rng = RandomNumberGenerator.Create();
        var tokenBytes = new byte[32];
        rng.GetBytes(tokenBytes);
        return Convert.ToBase64String(tokenBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}
