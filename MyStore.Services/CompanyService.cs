using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
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
            // Collect all validation errors
            var fieldErrors = new Dictionary<string, List<string>>();

            // Validate Email
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                AddFieldError(fieldErrors, "email", "Email is required");
            }
            else
            {
                var trimmedEmail = request.Email.Trim();
                if (!IsValidEmail(trimmedEmail))
                {
                    AddFieldError(fieldErrors, "email", "Please enter a valid email address");
                }
            }

            // Validate Password
            if (string.IsNullOrWhiteSpace(request.Password))
            {
                AddFieldError(fieldErrors, "password", "Password is required");
            }
            else
            {
                var passwordErrors = ValidatePassword(request.Password);
                if (passwordErrors.Count > 0)
                {
                    fieldErrors["password"] = passwordErrors;
                }
            }

            // Validate Company Name
            if (string.IsNullOrWhiteSpace(request.CompanyName))
            {
                AddFieldError(fieldErrors, "companyName", "Company name is required");
            }
            else
            {
                var trimmedCompanyName = request.CompanyName.Trim();
                if (trimmedCompanyName.Length < 2)
                {
                    AddFieldError(fieldErrors, "companyName", "Company name must be at least 2 characters");
                }
                else if (trimmedCompanyName.Length > 100)
                {
                    AddFieldError(fieldErrors, "companyName", "Company name must be 100 characters or less");
                }
                else if (!IsValidCompanyName(trimmedCompanyName))
                {
                    AddFieldError(fieldErrors, "companyName", "Company name can only contain letters, numbers, and spaces");
                }
            }

            // Validate Subscription Tier
            if (string.IsNullOrWhiteSpace(request.SubscriptionTier))
            {
                AddFieldError(fieldErrors, "subscriptionTier", "Please select a subscription tier");
            }

            // If there are validation errors, return them
            if (fieldErrors.Count > 0)
            {
                return ApiResponse<RegisterAccountResponse>.ValidationErrorResponse(
                    "Please correct the errors below and try again.",
                    fieldErrors
                );
            }

            // Check if email already exists (only if email is valid)
            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                var existing = await _repository.GetByEmailAsync(request.Email.Trim().ToLowerInvariant());
                if (existing != null)
                {
                    AddFieldError(fieldErrors, "email", "This email is already registered");
                    return ApiResponse<RegisterAccountResponse>.ValidationErrorResponse(
                        "This email is already registered",
                        fieldErrors
                    );
                }
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

            // Note: Password is not stored here - it will be handled by Azure AD B2C
            // CompanyName is not stored in Company table yet (may need to be added in future)

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

    /// <summary>
    /// Validates email format using RFC 5322 compliant regex pattern.
    /// This is more comprehensive than MailAddress validation.
    /// </summary>
    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        // RFC 5322 compliant email regex pattern
        // This pattern matches the standard email format specification
        var emailPattern = @"^[a-zA-Z0-9.!#$%&'*+\/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$";
        
        return Regex.IsMatch(email, emailPattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Validates password strength according to business rules:
    /// - Minimum 8 characters
    /// - At least 1 uppercase letter
    /// - At least 1 lowercase letter
    /// - At least 1 number
    /// </summary>
    private static List<string> ValidatePassword(string password)
    {
        var errors = new List<string>();

        if (password.Length < 8)
        {
            errors.Add("Password must be at least 8 characters");
        }

        var hasLowercase = Regex.IsMatch(password, @"[a-z]");
        var hasUppercase = Regex.IsMatch(password, @"[A-Z]");
        var hasNumber = Regex.IsMatch(password, @"\d");

        var missingRequirements = new List<string>();
        if (!hasLowercase) missingRequirements.Add("lowercase letter");
        if (!hasUppercase) missingRequirements.Add("uppercase letter");
        if (!hasNumber) missingRequirements.Add("number");

        if (missingRequirements.Count > 0)
        {
            errors.Add($"Password must contain at least one {string.Join(", ", missingRequirements)}");
        }

        return errors;
    }

    /// <summary>
    /// Validates company name format:
    /// - 2-100 characters
    /// - Alphanumeric and spaces only
    /// </summary>
    private static bool IsValidCompanyName(string companyName)
    {
        if (string.IsNullOrWhiteSpace(companyName))
            return false;

        // Alphanumeric and spaces only
        var companyNamePattern = @"^[a-zA-Z0-9\s]+$";
        return Regex.IsMatch(companyName, companyNamePattern);
    }

    /// <summary>
    /// Helper method to add field errors to the dictionary.
    /// </summary>
    private static void AddFieldError(Dictionary<string, List<string>> fieldErrors, string field, string error)
    {
        if (!fieldErrors.ContainsKey(field))
        {
            fieldErrors[field] = new List<string>();
        }
        fieldErrors[field].Add(error);
    }
}
