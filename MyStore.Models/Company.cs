namespace MyStore.Models;

public class Company
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Active, Suspended, Cancelled
    public DateTime TrialStartDate { get; set; }
    public DateTime TrialEndDate { get; set; }
    public string? VerificationToken { get; set; }
    public DateTime? VerificationTokenExpires { get; set; }
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpires { get; set; }
    public string SubscriptionTier { get; set; } = "Trial"; // Trial, Basic, Premium, Enterprise
    public DateTime CreatedDate { get; set; }
    public DateTime? LastModifiedDate { get; set; }
}

public class RegisterAccountRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string SubscriptionTier { get; set; } = "Trial";
    /// <summary>
    /// Stripe payment method ID from Stripe Elements (pm_xxx). Required at sign-up.
    /// Card is stored but not charged until trial ends.
    /// </summary>
    public string PaymentMethodId { get; set; } = string.Empty;
}

public class RegisterAccountResponse
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime TrialStartDate { get; set; }
    public DateTime TrialEndDate { get; set; }
    public string SubscriptionTier { get; set; } = string.Empty;
}

/// <summary>
/// Company data for trial-to-paid conversion (EPIC-0-006-004).
/// </summary>
public class TrialConversionCandidate
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime TrialStartDate { get; set; }
    public DateTime TrialEndDate { get; set; }
    public string SubscriptionTier { get; set; } = string.Empty;
    public string StripeCustomerId { get; set; } = string.Empty;
    public string DefaultPaymentMethodId { get; set; } = string.Empty;
}

/// <summary>
/// Response for trial status API. Used when displaying trial info on dashboard.
/// </summary>
public class TrialStatusResponse
{
    public bool IsInTrial { get; set; }
    public DateTime TrialStartDate { get; set; }
    public DateTime TrialEndDate { get; set; }
    public int DaysRemaining { get; set; }
    public bool HasPaymentMethod { get; set; }
    public string SubscriptionTier { get; set; } = string.Empty;
}

public class VerifyEmailRequest
{
    public string Token { get; set; } = string.Empty;
}

public class VerifyEmailResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Request model for resending verification email.
/// </summary>
public class ResendVerificationEmailRequest
{
    /// <summary>
    /// The email address of the account requesting verification email resend.
    /// </summary>
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// Request model for login (custom auth).
/// </summary>
public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Response model for login operation.
/// </summary>
public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// Response model for resend verification email operation.
/// </summary>
public class ResendVerificationEmailResponse
{
    /// <summary>
    /// Indicates whether the operation was successful.
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Message describing the result of the operation.
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// The email address the verification email was sent to (or requested for).
    /// </summary>
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// Request model for forgot password (request reset email).
/// </summary>
public class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// Response model for forgot password operation.
/// Always returns generic success message (do not reveal if email exists).
/// </summary>
public class ForgotPasswordResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Request model for reset password (complete reset with token).
/// </summary>
public class ResetPasswordRequest
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>
/// Response model for reset password operation.
/// </summary>
public class ResetPasswordResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
