namespace MyStore.Services;

/// <summary>
/// Service interface for sending emails, particularly verification emails for account registration.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends a verification email to the specified recipient with a verification token.
    /// </summary>
    /// <param name="toEmail">The email address of the recipient.</param>
    /// <param name="verificationToken">The secure verification token to include in the email link.</param>
    /// <param name="companyName">The name of the company/store for personalization.</param>
    /// <returns>An EmailSendResult indicating success or failure of the email send operation.</returns>
    Task<EmailSendResult> SendVerificationEmailAsync(string toEmail, string verificationToken, string companyName);

    /// <summary>
    /// Sends a password reset email to the specified recipient with a reset token.
    /// </summary>
    /// <param name="toEmail">The email address of the recipient.</param>
    /// <param name="resetToken">The secure reset token to include in the email link.</param>
    /// <returns>An EmailSendResult indicating success or failure of the email send operation.</returns>
    Task<EmailSendResult> SendPasswordResetEmailAsync(string toEmail, string resetToken);
}

/// <summary>
/// Result of an email send operation.
/// </summary>
public class EmailSendResult
{
    /// <summary>
    /// Indicates whether the email was sent successfully.
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Error message if the email send failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Message ID from the email service (if available).
    /// </summary>
    public string? MessageId { get; set; }
}
