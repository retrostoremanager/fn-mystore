namespace MyStore.Models;

public class Company
{
    public int Id { get; set; }
    public string? CompanyName { get; set; }
    /// <summary>
    /// URL-friendly identifier for path-based login (e.g. /acme/login). Unique across companies.
    /// </summary>
    public string? Slug { get; set; }
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

/// <summary>
/// Company profile for display and edit. Company has name, address, phone; locations have their own.
/// </summary>
public class CompanyProfile
{
    public int Id { get; set; }
    public string? CompanyName { get; set; }
    public string? CompanyAddress { get; set; }
    public string? CompanyAddress2 { get; set; }
    public string? CompanyCity { get; set; }
    public string? CompanyState { get; set; }
    public string? CompanyZipCode { get; set; }
    public string? CompanyPhone { get; set; }
    public string? Locale { get; set; }
    public string? LogoUrl { get; set; }
}

/// <summary>
/// Request to update company profile. All fields optional for partial update.
/// </summary>
public class CompanyProfileUpdateRequest
{
    public string? CompanyName { get; set; }
    public string? CompanyAddress { get; set; }
    public string? CompanyAddress2 { get; set; }
    public string? CompanyCity { get; set; }
    public string? CompanyState { get; set; }
    public string? CompanyZipCode { get; set; }
    public string? CompanyPhone { get; set; }
    public string? Locale { get; set; }
    public string? LogoUrl { get; set; }
}

/// <summary>
/// Request to upload company logo (base64-encoded file). EPIC-0-007-004.
/// </summary>
public class LogoUploadRequest
{
    public string File { get; set; } = string.Empty; // base64
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
}

/// <summary>
/// Response for GET company/profile - profile plus locations.
/// </summary>
public class CompanyProfileResponse
{
    public CompanyProfile Profile { get; set; } = null!;
    public IEnumerable<Location> Locations { get; set; } = Array.Empty<Location>();
}

/// <summary>
/// Request to create a location.
/// </summary>
public class LocationCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? Phone { get; set; }
    public string? Timezone { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>
/// Request to update a location.
/// </summary>
public class LocationUpdateRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? Phone { get; set; }
    public string? Timezone { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>
/// Response for location deletion info (EPIC-1-003).
/// </summary>
public class LocationDeletionInfoResponse
{
    public bool HasInventory { get; set; }
    public int InventoryCount { get; set; }
    public List<Location> OtherLocations { get; set; } = new();
}

/// <summary>
/// Location for multi-location support (EPIC-0-007).
/// </summary>
public class Location
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? Phone { get; set; }
    public string? Timezone { get; set; }
    public bool IsPrimary { get; set; }
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
    /// <summary>
    /// Company slug for path-based login URL (e.g. /acme/login).
    /// </summary>
    public string? Slug { get; set; }
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
    /// <summary>
    /// True when trial expired, no payment method, and account not suspended. User must add payment to continue (EPIC-0-006-005).
    /// </summary>
    public bool AccessRestricted { get; set; }
    /// <summary>
    /// True when account is suspended (trial expired 7+ days ago with no payment). All access blocked (EPIC-0-006-005).
    /// </summary>
    public bool AccessSuspended { get; set; }
}

/// <summary>
/// Unified subscription status response for GET /billing/subscription.
/// </summary>
public class SubscriptionStatusResponse
{
    public string Tier { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsInTrial { get; set; }
    public DateTime? TrialEndDate { get; set; }
    public int DaysRemainingInTrial { get; set; }
    public string? BillingCycle { get; set; }
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public bool HasPaymentMethod { get; set; }
}

/// <summary>
/// Consolidated subscription detail response for GET /billing/subscription (Issue #225).
/// Combines Stripe subscription data with upcoming invoice info.
/// </summary>
public class SubscriptionDetailResponse
{
    public string? PlanName { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public DateTime? TrialStart { get; set; }
    public DateTime? TrialEnd { get; set; }
    public decimal? NextInvoiceAmount { get; set; }
    public string? Currency { get; set; }
}

/// <summary>
/// Tax settings for a company (Issues #163, #288).
/// </summary>
public class TaxSettingsResponse
{
    public bool TaxEnabled { get; set; }
    public decimal TaxRate { get; set; }
    public string TaxLabel { get; set; } = "Sales Tax";
}

/// <summary>
/// Request to update company tax settings (Issues #163, #288).
/// </summary>
public class TaxSettingsRequest
{
    public bool TaxEnabled { get; set; }
    public decimal TaxRate { get; set; }
    public string TaxLabel { get; set; } = "Sales Tax";
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
/// Slug is required for path-based login - scopes auth to the specified company.
/// </summary>
public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    /// <summary>
    /// Company slug from URL (e.g. "acme" from /acme/login). Required for path-based login.
    /// </summary>
    public string? Slug { get; set; }
}

/// <summary>
/// Minimal company info for login page display (validate slug exists, show company name).
/// </summary>
public class CompanyBySlugResponse
{
    public int Id { get; set; }
    public string? CompanyName { get; set; }
    public string Slug { get; set; } = string.Empty;
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
