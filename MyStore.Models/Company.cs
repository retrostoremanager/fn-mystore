namespace MyStore.Models;

public class Company
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending, Active, Suspended, Cancelled
    public DateTime TrialStartDate { get; set; }
    public DateTime TrialEndDate { get; set; }
    public string? VerificationToken { get; set; }
    public DateTime? VerificationTokenExpires { get; set; }
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
