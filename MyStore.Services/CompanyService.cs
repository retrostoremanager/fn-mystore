using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class CompanyService : ICompanyService
{
    private readonly ICompanyRepository _repository;
    private readonly IEmailService _emailService;
    private readonly IPaymentService _paymentService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CompanyService> _logger;

    // Rate limiting: Track resend attempts per email address
    private static readonly Dictionary<string, List<DateTime>> _resendAttempts = new();
    // Rate limiting: Track forgot password attempts per email address
    private static readonly Dictionary<string, List<DateTime>> _forgotPasswordAttempts = new();
    private static readonly object _rateLimitLock = new();
    private const int MaxResendAttemptsPerHour = 3;
    private const int MaxForgotPasswordAttemptsPerHour = 3;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromHours(1);

    public CompanyService(ICompanyRepository repository, IEmailService emailService, IPaymentService paymentService, IConfiguration configuration, ILogger<CompanyService> logger)
    {
        _repository = repository;
        _emailService = emailService;
        _paymentService = paymentService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ApiResponse<CompanyBySlugResponse?>> GetCompanyBySlugAsync(string slug)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return ApiResponse<CompanyBySlugResponse?>.ErrorResponse("Company slug is required.");
            }

            var company = await _repository.GetBySlugAsync(slug.Trim().ToLowerInvariant());
            if (company == null)
            {
                return ApiResponse<CompanyBySlugResponse?>.ErrorResponse("Company not found.");
            }

            var response = new CompanyBySlugResponse
            {
                Id = company.Id,
                CompanyName = company.CompanyName,
                Slug = company.Slug ?? slug
            };

            return ApiResponse<CompanyBySlugResponse?>.SuccessResponse(response, "Company found.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting company by slug {Slug}", slug);
            return ApiResponse<CompanyBySlugResponse?>.ErrorResponse("An error occurred while looking up the company.");
        }
    }

    public async Task<ApiResponse<LoginResponse>> LoginAsync(LoginRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return ApiResponse<LoginResponse>.ErrorResponse("Email and password are required.");
            }

            if (string.IsNullOrWhiteSpace(request.Slug))
            {
                return ApiResponse<LoginResponse>.ErrorResponse("Company slug is required. Use your company login link (e.g. /c/your-company/login).");
            }

            // Path-based login: get company by slug first, then verify credentials for that company
            var company = await _repository.GetBySlugAsync(request.Slug.Trim().ToLowerInvariant());
            if (company == null)
            {
                return ApiResponse<LoginResponse>.ErrorResponse("Invalid email or password.");
            }

            // Verify email matches this company (owner login)
            var trimmedEmail = request.Email.Trim().ToLowerInvariant();
            if (!string.Equals(company.Email, trimmedEmail, StringComparison.OrdinalIgnoreCase))
            {
                return ApiResponse<LoginResponse>.ErrorResponse("Invalid email or password.");
            }

            if (string.IsNullOrEmpty(company.PasswordHash))
            {
                return ApiResponse<LoginResponse>.ErrorResponse("Account was created before password sign-in was enabled. Please sign up again or use password reset.");
            }

            if (!BCrypt.Net.BCrypt.Verify(request.Password, company.PasswordHash))
            {
                return ApiResponse<LoginResponse>.ErrorResponse("Invalid email or password.");
            }

            if (!string.Equals(company.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                return ApiResponse<LoginResponse>.ErrorResponse("Please verify your email before signing in.");
            }

            var token = GenerateJwtToken(company.Id, company.Email);
            var response = new LoginResponse
            {
                Token = token,
                CompanyId = company.Id,
                Email = company.Email
            };

            return ApiResponse<LoginResponse>.SuccessResponse(response, "Login successful.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for email {Email}", request.Email);
            var message = "An error occurred during sign-in. Please try again.";
            // Include error detail in dev for debugging
            var env = _configuration["ASPNETCORE_ENVIRONMENT"] ?? "";
            if (env.StartsWith("dev", StringComparison.OrdinalIgnoreCase) || env.Equals("Development", StringComparison.OrdinalIgnoreCase))
            {
                message += $" ({ex.GetType().Name}: {ex.Message})";
            }
            return ApiResponse<LoginResponse>.ErrorResponse(message);
        }
    }

    private string GenerateJwtToken(int companyId, string email)
    {
        var secretKey = _configuration["JwtAuthentication__SecretKey"]
            ?? _configuration["JwtAuthentication:SecretKey"]
            ?? _configuration["JwtSecret"]
            ?? throw new InvalidOperationException("JwtAuthentication__SecretKey or JwtSecret must be configured for login.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("extension_CompanyId", companyId.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim(JwtRegisteredClaimNames.Sub, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: "MyStore",
            audience: "MyStore",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
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

            // Validate Payment Method (required at sign-up)
            if (string.IsNullOrWhiteSpace(request.PaymentMethodId))
            {
                AddFieldError(fieldErrors, "paymentMethod", "Payment method is required. Please enter your card details.");
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

            // Hash password for storage
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, BCrypt.Net.BCrypt.GenerateSalt(12));

            // Create company record
            var company = new Company
            {
                Email = request.Email.Trim().ToLowerInvariant(),
                CompanyName = request.CompanyName?.Trim(),
                PasswordHash = passwordHash,
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

            // Store payment method with Stripe (card required at sign-up, not charged until trial ends)
            var paymentResult = await _paymentService.StorePaymentMethodAsync(created.Id, new StorePaymentMethodRequest
            {
                PaymentMethodId = request.PaymentMethodId!
            });
            if (!paymentResult.Success)
            {
                _logger.LogWarning("Payment method storage failed for new account {AccountId}: {Error}. Rolling back account creation.", created.Id, paymentResult.Message);
                // Attempt to delete the created company (best-effort rollback)
                try
                {
                    await _repository.DeleteAsync(created.Id);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, "Failed to rollback account creation for {AccountId}", created.Id);
                }
                AddFieldError(fieldErrors, "paymentMethod", paymentResult.Message ?? "Payment method could not be processed. Please check your card details and try again.");
                return ApiResponse<RegisterAccountResponse>.ValidationErrorResponse(
                    paymentResult.Message ?? "Payment method could not be processed. Please check your card details and try again.",
                    fieldErrors
                );
            }

            // Send verification email asynchronously (fire-and-forget pattern with error handling)
            // Note: Email sending failure does not fail account creation - email can be resent later
            _ = Task.Run(async () =>
            {
                try
                {
                    var emailResult = await _emailService.SendVerificationEmailAsync(
                        created.Email,
                        verificationToken,
                        request.CompanyName ?? "Store Owner"
                    );

                    if (emailResult.Success)
                    {
                        _logger.LogInformation(
                            "Verification email queued successfully for account {AccountId}, email {Email}",
                            created.Id,
                            created.Email
                        );
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Failed to send verification email for account {AccountId}, email {Email}. Error: {Error}",
                            created.Id,
                            created.Email,
                            emailResult.ErrorMessage
                        );
                        // Note: Email sending failure doesn't fail account creation
                        // The email can be resent later via resend verification endpoint
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Exception occurred while sending verification email for account {AccountId}, email {Email}",
                        created.Id,
                        created.Email
                    );
                    // Don't throw - account creation succeeded, email can be resent later
                }
            });

            // Fetch created company to get slug (generated by DB)
            var createdWithSlug = await _repository.GetByIdAsync(created.Id);

            // Create response
            var response = new RegisterAccountResponse
            {
                Id = created.Id,
                Email = created.Email,
                Status = created.Status,
                TrialStartDate = created.TrialStartDate,
                TrialEndDate = created.TrialEndDate,
                SubscriptionTier = created.SubscriptionTier,
                Slug = createdWithSlug?.Slug
            };

            return ApiResponse<RegisterAccountResponse>.SuccessResponse(response, "Account registered successfully. Please check your email to verify your account.");
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

    public async Task<ApiResponse<VerifyEmailResponse>> VerifyEmailAsync(string token)
    {
        try
        {
            // Validate token parameter
            if (string.IsNullOrWhiteSpace(token))
            {
                return ApiResponse<VerifyEmailResponse>.ErrorResponse(
                    "Invalid verification link. The token is missing.",
                    new List<string> { "Token is required" }
                );
            }

            // Get company by verification token
            var company = await _repository.GetByVerificationTokenAsync(token);

            if (company == null)
            {
                _logger.LogWarning("Verification attempt with invalid token: {Token}", token);
                return ApiResponse<VerifyEmailResponse>.ErrorResponse(
                    "Invalid verification link. The token may have been used already or does not exist.",
                    new List<string> { "Invalid token" }
                );
            }

            // Check if account is already verified
            if (company.Status == "Active")
            {
                _logger.LogInformation("Account {AccountId} is already verified", company.Id);
                return ApiResponse<VerifyEmailResponse>.SuccessResponse(
                    new VerifyEmailResponse
                    {
                        Success = true,
                        Message = "Your account is already verified. You can log in now.",
                        Email = company.Email,
                        Status = company.Status
                    },
                    "Your account is already verified."
                );
            }

            // Check if token has expired
            if (company.VerificationTokenExpires.HasValue && 
                company.VerificationTokenExpires.Value < DateTime.UtcNow)
            {
                _logger.LogWarning(
                    "Verification token expired for account {AccountId}, email {Email}. Expired at {ExpiryTime}",
                    company.Id,
                    company.Email,
                    company.VerificationTokenExpires.Value
                );
                // Include email in response for expired tokens so user can resend verification
                return ApiResponse<VerifyEmailResponse>.ErrorResponse(
                    "Your verification link has expired. Please request a new verification email.",
                    new List<string> { "Token expired" },
                    new VerifyEmailResponse
                    {
                        Success = false,
                        Message = "Your verification link has expired. Please request a new verification email.",
                        Email = company.Email,
                        Status = company.Status
                    }
                );
            }

            // Update company status to Active and clear verification token
            company.Status = "Active";
            company.VerificationToken = null;
            company.VerificationTokenExpires = null;
            company.LastModifiedDate = DateTime.UtcNow;

            var updated = await _repository.UpdateAsync(company.Id, company);

            if (updated == null)
            {
                _logger.LogError("Failed to update company {AccountId} during email verification", company.Id);
                return ApiResponse<VerifyEmailResponse>.ErrorResponse(
                    "An error occurred while verifying your account. Please try again later.",
                    new List<string> { "Update failed" }
                );
            }

            _logger.LogInformation(
                "Account {AccountId}, email {Email} successfully verified and activated",
                company.Id,
                company.Email
            );

            return ApiResponse<VerifyEmailResponse>.SuccessResponse(
                new VerifyEmailResponse
                {
                    Success = true,
                    Message = "Your email has been verified successfully! You can now log in to your account.",
                    Email = company.Email,
                    Status = "Active"
                },
                "Email verified successfully."
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying email with token: {Token}", token);
            return ApiResponse<VerifyEmailResponse>.ErrorResponse(
                "An unexpected error occurred while verifying your email. Please try again later.",
                new List<string> { ex.Message }
            );
        }
    }

    /// <summary>
    /// Resends a verification email to an unverified account.
    /// Implements rate limiting (max 3 requests per hour per email) and generates a new verification token.
    /// </summary>
    /// <param name="request">The resend verification email request containing the email address.</param>
    /// <returns>
    /// An ApiResponse containing the result. Returns success even for non-existent emails (security best practice).
    /// Returns error if account is already verified, rate limit exceeded, or validation fails.
    /// </returns>
    /// <remarks>
    /// This method:
    /// - Validates the email format
    /// - Checks if account exists and is unverified
    /// - Enforces rate limiting (max 3 requests per hour per email)
    /// - Invalidates the old verification token
    /// - Generates a new secure token with 24-hour expiration
    /// - Sends verification email asynchronously (fire-and-forget pattern)
    /// - Does not reveal if email exists for security reasons
    /// </remarks>
    public async Task<ApiResponse<ResendVerificationEmailResponse>> ResendVerificationEmailAsync(ResendVerificationEmailRequest request)
    {
        try
        {
            // Validate email parameter
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return ApiResponse<ResendVerificationEmailResponse>.ErrorResponse(
                    "Email is required",
                    new List<string> { "Email parameter is required" }
                );
            }

            var email = request.Email.Trim().ToLowerInvariant();

            // Validate email format
            if (!IsValidEmail(email))
            {
                return ApiResponse<ResendVerificationEmailResponse>.ErrorResponse(
                    "Please enter a valid email address",
                    new List<string> { "Invalid email format" }
                );
            }

            // Get company by email
            var company = await _repository.GetByEmailAsync(email);

            if (company == null)
            {
                // Don't reveal if email exists or not for security reasons
                _logger.LogWarning("Resend verification requested for non-existent email: {Email}", email);
                return ApiResponse<ResendVerificationEmailResponse>.SuccessResponse(
                    new ResendVerificationEmailResponse
                    {
                        Success = true,
                        Message = "If an account exists with this email, a verification email has been sent.",
                        Email = email
                    },
                    "If an account exists with this email, a verification email has been sent."
                );
            }

            // Check if account is already verified
            if (company.Status == "Active")
            {
                _logger.LogInformation("Resend verification requested for already verified account {AccountId}, email {Email}", company.Id, email);
                return ApiResponse<ResendVerificationEmailResponse>.ErrorResponse(
                    "This account is already verified. You can log in now.",
                    new List<string> { "Account already verified" }
                );
            }

            // Rate limiting: Enforce max 3 resend requests per hour per email address
            // Uses in-memory dictionary to track attempts with automatic cleanup of expired entries
            // Rate limit window resets 1 hour after the first request in the window
            lock (_rateLimitLock)
            {
                var now = DateTime.UtcNow;
                
                // Clean up old attempts (older than 1 hour) to prevent memory growth
                if (_resendAttempts.ContainsKey(email))
                {
                    _resendAttempts[email].RemoveAll(timestamp => now - timestamp > RateLimitWindow);
                    
                    // Remove empty lists to keep dictionary clean
                    if (_resendAttempts[email].Count == 0)
                    {
                        _resendAttempts.Remove(email);
                    }
                }

                // Check if rate limit exceeded (max 3 requests per hour)
                if (_resendAttempts.ContainsKey(email) && _resendAttempts[email].Count >= MaxResendAttemptsPerHour)
                {
                    // Calculate time until rate limit resets (1 hour from oldest attempt)
                    var oldestAttempt = _resendAttempts[email].Min();
                    var timeUntilReset = RateLimitWindow - (now - oldestAttempt);
                    var minutesUntilReset = (int)Math.Ceiling(timeUntilReset.TotalMinutes);
                    
                    _logger.LogWarning(
                        "Rate limit exceeded for resend verification: {Email}. Attempts: {Attempts}, Reset in: {Minutes} minutes",
                        email,
                        _resendAttempts[email].Count,
                        minutesUntilReset
                    );

                    return ApiResponse<ResendVerificationEmailResponse>.ErrorResponse(
                        $"Too many resend requests. Please wait {minutesUntilReset} minute(s) before requesting another verification email.",
                        new List<string> { $"Rate limit exceeded. Maximum {MaxResendAttemptsPerHour} requests per hour allowed." }
                    );
                }

                // Record this attempt timestamp for rate limiting
                if (!_resendAttempts.ContainsKey(email))
                {
                    _resendAttempts[email] = new List<DateTime>();
                }
                _resendAttempts[email].Add(now);
            }

            // Invalidate old token and generate new token
            var newVerificationToken = GenerateSecureToken();
            var tokenExpires = DateTime.UtcNow.AddHours(24);

            // Update company with new token
            company.VerificationToken = newVerificationToken;
            company.VerificationTokenExpires = tokenExpires;
            company.LastModifiedDate = DateTime.UtcNow;

            var updated = await _repository.UpdateAsync(company.Id, company);

            if (updated == null)
            {
                _logger.LogError("Failed to update company {AccountId} during resend verification", company.Id);
                return ApiResponse<ResendVerificationEmailResponse>.ErrorResponse(
                    "An error occurred while processing your request. Please try again later.",
                    new List<string> { "Update failed" }
                );
            }

            // Send verification email asynchronously (fire-and-forget pattern with error handling)
            _ = Task.Run(async () =>
            {
                try
                {
                    // Note: CompanyName is not stored in Company table, so we'll use a default
                    var emailResult = await _emailService.SendVerificationEmailAsync(
                        updated.Email,
                        newVerificationToken,
                        "Store Owner" // Default since CompanyName is not stored
                    );

                    if (emailResult.Success)
                    {
                        _logger.LogInformation(
                            "Resend verification email queued successfully for account {AccountId}, email {Email}",
                            updated.Id,
                            updated.Email
                        );
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Failed to send resend verification email for account {AccountId}, email {Email}. Error: {Error}",
                            updated.Id,
                            updated.Email,
                            emailResult.ErrorMessage
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Exception occurred while sending resend verification email for account {AccountId}, email {Email}",
                        updated.Id,
                        updated.Email
                    );
                }
            });

            _logger.LogInformation(
                "Resend verification email requested successfully for account {AccountId}, email {Email}",
                company.Id,
                email
            );

            return ApiResponse<ResendVerificationEmailResponse>.SuccessResponse(
                new ResendVerificationEmailResponse
                {
                    Success = true,
                    Message = "A new verification email has been sent. Please check your inbox.",
                    Email = email
                },
                "Verification email sent successfully."
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resending verification email for email: {Email}", request.Email);
            return ApiResponse<ResendVerificationEmailResponse>.ErrorResponse(
                "An unexpected error occurred while processing your request. Please try again later.",
                new List<string> { ex.Message }
            );
        }
    }

    /// <summary>
    /// Processes a forgot password request. Sends reset email if account exists.
    /// Always returns generic success message (do not reveal if email exists).
    /// Implements rate limiting (max 3 requests per hour per email).
    /// </summary>
    public async Task<ApiResponse<ForgotPasswordResponse>> ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return ApiResponse<ForgotPasswordResponse>.ErrorResponse(
                    "Email is required",
                    new List<string> { "Email parameter is required" }
                );
            }

            var email = request.Email.Trim().ToLowerInvariant();
            if (!IsValidEmail(email))
            {
                return ApiResponse<ForgotPasswordResponse>.ErrorResponse(
                    "Please enter a valid email address",
                    new List<string> { "Invalid email format" }
                );
            }

            var company = await _repository.GetByEmailAsync(email);
            if (company == null)
            {
                _logger.LogWarning("Forgot password requested for non-existent email: {Email}", email);
                return ApiResponse<ForgotPasswordResponse>.SuccessResponse(
                    new ForgotPasswordResponse { Success = true, Message = "If an account exists with this email, a password reset link has been sent." },
                    "If an account exists with this email, a password reset link has been sent."
                );
            }

            if (string.IsNullOrEmpty(company.PasswordHash))
            {
                return ApiResponse<ForgotPasswordResponse>.SuccessResponse(
                    new ForgotPasswordResponse { Success = true, Message = "If an account exists with this email, a password reset link has been sent." },
                    "If an account exists with this email, a password reset link has been sent."
                );
            }

            lock (_rateLimitLock)
            {
                var now = DateTime.UtcNow;
                if (_forgotPasswordAttempts.ContainsKey(email))
                {
                    _forgotPasswordAttempts[email].RemoveAll(t => now - t > RateLimitWindow);
                    if (_forgotPasswordAttempts[email].Count == 0)
                        _forgotPasswordAttempts.Remove(email);
                }

                if (_forgotPasswordAttempts.ContainsKey(email) && _forgotPasswordAttempts[email].Count >= MaxForgotPasswordAttemptsPerHour)
                {
                    var oldest = _forgotPasswordAttempts[email].Min();
                    var minutesUntilReset = (int)Math.Ceiling((RateLimitWindow - (now - oldest)).TotalMinutes);
                    return ApiResponse<ForgotPasswordResponse>.ErrorResponse(
                        $"Too many reset requests. Please wait {minutesUntilReset} minute(s) before trying again.",
                        new List<string> { $"Rate limit exceeded. Maximum {MaxForgotPasswordAttemptsPerHour} requests per hour." }
                    );
                }

                if (!_forgotPasswordAttempts.ContainsKey(email))
                    _forgotPasswordAttempts[email] = new List<DateTime>();
                _forgotPasswordAttempts[email].Add(now);
            }

            var resetToken = GenerateSecureToken();
            var tokenExpires = DateTime.UtcNow.AddHours(1);

            company.PasswordResetToken = resetToken;
            company.PasswordResetTokenExpires = tokenExpires;
            company.LastModifiedDate = DateTime.UtcNow;

            var updated = await _repository.UpdateAsync(company.Id, company);
            if (updated == null)
            {
                _logger.LogError("Failed to update company {AccountId} during forgot password", company.Id);
                return ApiResponse<ForgotPasswordResponse>.ErrorResponse(
                    "An error occurred. Please try again later.",
                    new List<string> { "Update failed" }
                );
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var emailResult = await _emailService.SendPasswordResetEmailAsync(updated.Email, resetToken);
                    if (emailResult.Success)
                        _logger.LogInformation("Password reset email sent for account {AccountId}, email {Email}", updated.Id, updated.Email);
                    else
                        _logger.LogWarning("Failed to send password reset email for {Email}: {Error}", updated.Email, emailResult.ErrorMessage);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception sending password reset email for {Email}", updated.Email);
                }
            });

            return ApiResponse<ForgotPasswordResponse>.SuccessResponse(
                new ForgotPasswordResponse { Success = true, Message = "If an account exists with this email, a password reset link has been sent." },
                "If an account exists with this email, a password reset link has been sent."
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing forgot password for {Email}", request.Email);
            return ApiResponse<ForgotPasswordResponse>.ErrorResponse(
                "An error occurred. Please try again later.",
                new List<string> { ex.Message }
            );
        }
    }

    /// <summary>
    /// Completes password reset with token and new password.
    /// Token must be valid, not expired, and single-use.
    /// </summary>
    public async Task<ApiResponse<ResetPasswordResponse>> ResetPasswordAsync(ResetPasswordRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return ApiResponse<ResetPasswordResponse>.ErrorResponse(
                    "Invalid reset link. The token is missing.",
                    new List<string> { "Token is required" }
                );
            }

            var passwordErrors = ValidatePassword(request.NewPassword);
            if (passwordErrors.Count > 0)
            {
                var fieldErrors = new Dictionary<string, List<string>> { ["newPassword"] = passwordErrors };
                return ApiResponse<ResetPasswordResponse>.ValidationErrorResponse(
                    "Please correct the errors below.",
                    fieldErrors
                );
            }

            var company = await _repository.GetByPasswordResetTokenAsync(request.Token);
            if (company == null)
            {
                _logger.LogWarning("Reset password attempt with invalid token");
                return ApiResponse<ResetPasswordResponse>.ErrorResponse(
                    "Invalid or expired reset link. Please request a new password reset.",
                    new List<string> { "Invalid token" }
                );
            }

            if (!company.PasswordResetTokenExpires.HasValue || company.PasswordResetTokenExpires.Value < DateTime.UtcNow)
            {
                _logger.LogWarning("Reset password attempt with expired token for {Email}", company.Email);
                return ApiResponse<ResetPasswordResponse>.ErrorResponse(
                    "Your reset link has expired. Please request a new password reset.",
                    new List<string> { "Token expired" },
                    new ResetPasswordResponse { Success = false, Message = "Your reset link has expired. Please request a new password reset." }
                );
            }

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, BCrypt.Net.BCrypt.GenerateSalt(12));
            company.PasswordHash = passwordHash;
            company.PasswordResetToken = null;
            company.PasswordResetTokenExpires = null;
            company.LastModifiedDate = DateTime.UtcNow;

            var updated = await _repository.UpdateAsync(company.Id, company);
            if (updated == null)
            {
                _logger.LogError("Failed to update password for company {AccountId}", company.Id);
                return ApiResponse<ResetPasswordResponse>.ErrorResponse(
                    "An error occurred. Please try again later.",
                    new List<string> { "Update failed" }
                );
            }

            _logger.LogInformation("Password reset completed for account {AccountId}, email {Email}", company.Id, company.Email);
            return ApiResponse<ResetPasswordResponse>.SuccessResponse(
                new ResetPasswordResponse { Success = true, Message = "Your password has been reset successfully. You can now sign in." },
                "Your password has been reset successfully. You can now sign in."
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting password");
            return ApiResponse<ResetPasswordResponse>.ErrorResponse(
                "An unexpected error occurred. Please try again later.",
                new List<string> { ex.Message }
            );
        }
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
