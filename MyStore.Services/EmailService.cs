using System.Text;
using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyStore.Models;

namespace MyStore.Services;

/// <summary>
/// Email service implementation using Azure Communication Services Email.
/// Sends verification emails with HTML and plain text content, including retry logic for reliability.
/// </summary>
/// <remarks>
/// Configuration required:
/// - AzureCommunicationServices__ConnectionString: Connection string from Azure Communication Services resource
/// - Email__FromEmail: Verified email address (must be from verified domain)
/// - Email__FromName: Display name for sender
/// - VerificationBaseUrl: Base URL for verification links (e.g., https://app.mystore.com/verify)
/// </remarks>
public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;
    private readonly EmailClient _emailClient;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly string _verificationBaseUrl;
    private readonly string _passwordResetBaseUrl;
    private readonly string _userInviteBaseUrl;
    private readonly string _billingBaseUrl;

    /// <summary>
    /// Initializes a new instance of the EmailService.
    /// </summary>
    /// <param name="configuration">Configuration (app settings, Key Vault refs).</param>
    /// <param name="logger">Logger for email operations.</param>
    /// <exception cref="InvalidOperationException">Thrown when required configuration is missing.</exception>
    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _logger = logger;

        // Prefer IConfiguration (Azure app settings / Key Vault) then env vars (local.settings.json)
        // Reject unresolved Key Vault refs (isolated worker may not resolve them)
        static string? GetConfig(IConfiguration config, string key)
        {
            var val = config[key] ?? Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(val)) return null;
            if (val.Contains("@Microsoft.KeyVault", StringComparison.OrdinalIgnoreCase) || val.TrimStart().StartsWith("@"))
                return null;
            return val;
        }

        var connectionString = GetConfig(configuration, "AzureCommunicationServices__ConnectionString");

        if (string.IsNullOrEmpty(connectionString))
        {
            _logger.LogWarning("AzureCommunicationServices__ConnectionString is not configured. Email functionality will be disabled.");
            _emailClient = null!;
            _fromEmail = string.Empty;
            _fromName = "MyStore";
            _verificationBaseUrl = "https://app.mystore.com/verify";
            _passwordResetBaseUrl = "https://app.mystore.com/reset-password";
            _userInviteBaseUrl = "https://app.mystore.com/set-password";
            _billingBaseUrl = "https://app.mystore.com/dashboard/billing";
            return;
        }

        _emailClient = new EmailClient(connectionString);

        _fromEmail = GetConfig(configuration, "Email__FromEmail")
            ?? throw new InvalidOperationException("Email__FromEmail is not configured. This should be your verified domain email address (e.g., noreply@yourdomain.com)");

        _fromName = GetConfig(configuration, "Email__FromName") ?? "MyStore";

        _verificationBaseUrl = GetConfig(configuration, "VerificationBaseUrl") ?? "https://app.mystore.com/verify";
        _passwordResetBaseUrl = GetConfig(configuration, "PasswordResetBaseUrl") ?? "https://app.mystore.com/reset-password";

        // Derive from VerificationBaseUrl when not explicitly set (e.g. https://dev.retrostoremanager.com/verify -> .../set-password)
        var appBase = _verificationBaseUrl.TrimEnd('/').Replace("/verify", "", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(appBase)) appBase = "https://app.mystore.com";
        _userInviteBaseUrl = GetConfig(configuration, "UserInviteBaseUrl") ?? $"{appBase.TrimEnd('/')}/set-password";

        _billingBaseUrl = GetConfig(configuration, "BillingBaseUrl") ?? "https://app.mystore.com/dashboard/billing";
    }

    /// <summary>
    /// Sends a password reset email to the specified recipient with a reset token.
    /// Reset links expire after 1 hour and are single-use.
    /// </summary>
    public async Task<EmailSendResult> SendPasswordResetEmailAsync(string toEmail, string resetToken)
    {
        if (_emailClient == null)
        {
            _logger.LogWarning("Email service not configured. Skipping password reset email for {Email}", toEmail);
            return new EmailSendResult { Success = true, ErrorMessage = "Email service not configured" };
        }

        try
        {
            var resetUrl = $"{_passwordResetBaseUrl}?token={Uri.EscapeDataString(resetToken)}";
            var emailContent = new EmailContent("Reset Your MyStore Password")
            {
                PlainText = GetPasswordResetPlainText(resetUrl),
                Html = GetPasswordResetHtml(resetUrl)
            };

            var emailMessage = new EmailMessage(
                senderAddress: _fromEmail,
                recipientAddress: toEmail,
                content: emailContent
            );

            var success = await SendWithRetryAsync(emailMessage);
            if (success)
            {
                _logger.LogInformation("Password reset email sent successfully to {Email}", toEmail);
                return new EmailSendResult { Success = true };
            }

            _logger.LogError("Failed to send password reset email to {Email} after retries", toEmail);
            return new EmailSendResult { Success = false, ErrorMessage = "Failed to send email after retries" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while sending password reset email to {Email}", toEmail);
            return new EmailSendResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Sends a trial expiration reminder email (7, 3, or 1 day before trial ends).
    /// </summary>
    public async Task<EmailSendResult> SendTrialExpirationEmailAsync(string toEmail, int daysRemaining)
    {
        if (_emailClient == null)
        {
            _logger.LogWarning("Email service not configured. Skipping trial expiration email for {Email}", toEmail);
            return new EmailSendResult { Success = true, ErrorMessage = "Email service not configured" };
        }

        if (daysRemaining is not 7 and not 3 and not 1)
        {
            _logger.LogWarning("Invalid daysRemaining {Days} for trial expiration email. Must be 7, 3, or 1.", daysRemaining);
            return new EmailSendResult { Success = false, ErrorMessage = "daysRemaining must be 7, 3, or 1" };
        }

        try
        {
            var emailContent = new EmailContent(GetTrialExpirationSubject(daysRemaining))
            {
                PlainText = GetTrialExpirationPlainText(daysRemaining),
                Html = GetTrialExpirationHtml(daysRemaining)
            };

            var emailMessage = new EmailMessage(
                senderAddress: _fromEmail,
                recipientAddress: toEmail,
                content: emailContent
            );

            var success = await SendWithRetryAsync(emailMessage);
            if (success)
            {
                _logger.LogInformation("Trial expiration ({Days}d) email sent successfully to {Email}", daysRemaining, toEmail);
                return new EmailSendResult { Success = true };
            }

            _logger.LogError("Failed to send trial expiration email to {Email} after retries", toEmail);
            return new EmailSendResult { Success = false, ErrorMessage = "Failed to send email after retries" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while sending trial expiration email to {Email}", toEmail);
            return new EmailSendResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private static string GetTrialExpirationSubject(int daysRemaining) =>
        daysRemaining switch
        {
            7 => "Your MyStore trial expires in 7 days",
            3 => "Your MyStore trial expires in 3 days",
            1 => "Your MyStore trial expires tomorrow",
            _ => "Your MyStore trial is ending soon"
        };

    private string GetTrialExpirationPlainText(int daysRemaining)
    {
        var daysText = daysRemaining == 1 ? "tomorrow" : $"{daysRemaining} days";
        var sb = new StringBuilder();
        sb.AppendLine($"Your MyStore free trial will expire in {daysText}.");
        sb.AppendLine();
        sb.AppendLine("To continue using MyStore without interruption, please add a payment method before your trial ends.");
        sb.AppendLine();
        sb.AppendLine($"Add payment method: {_billingBaseUrl}");
        sb.AppendLine();
        sb.AppendLine("If you have any questions, please contact our support team.");
        sb.AppendLine();
        sb.AppendLine("Best regards,");
        sb.AppendLine("The MyStore Team");
        return sb.ToString();
    }

    private string GetTrialExpirationHtml(int daysRemaining)
    {
        var daysText = daysRemaining == 1 ? "tomorrow" : $"{daysRemaining} days";
        return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Trial Expiring Soon</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px; }}
        .container {{ background-color: #fff; border-radius: 8px; padding: 40px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .button {{ display: inline-block; padding: 14px 32px; background-color: #3498db; color: #fff !important; text-decoration: none; border-radius: 5px; font-weight: bold; }}
        .warning {{ background-color: #fff3cd; border-left: 4px solid #ffc107; padding: 12px; margin: 20px 0; border-radius: 4px; }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>Your Trial Is Ending Soon</h1>
        <p>Your MyStore free trial will expire in {daysText}.</p>
        <p>To continue using MyStore without interruption, please add a payment method before your trial ends.</p>
        <p><a href=""{_billingBaseUrl}"" class=""button"">Add Payment Method</a></p>
        <div class=""warning"">
            <strong>Important:</strong> Without a payment method on file, your access will be restricted when the trial ends.
        </div>
        <p>Best regards,<br>The MyStore Team</p>
    </div>
</body>
</html>";
    }

    private static string GetPasswordResetPlainText(string resetUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You requested a password reset for your MyStore account.");
        sb.AppendLine();
        sb.AppendLine("Click the link below to reset your password:");
        sb.AppendLine();
        sb.AppendLine(resetUrl);
        sb.AppendLine();
        sb.AppendLine("This link will expire in 1 hour and can only be used once.");
        sb.AppendLine();
        sb.AppendLine("If you did not request a password reset, please ignore this email.");
        sb.AppendLine();
        sb.AppendLine("Best regards,");
        sb.AppendLine("The MyStore Team");
        return sb.ToString();
    }

    private static string GetPasswordResetHtml(string resetUrl)
    {
        return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Reset Your MyStore Password</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px; }}
        .container {{ background-color: #fff; border-radius: 8px; padding: 40px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .button {{ display: inline-block; padding: 14px 32px; background-color: #3498db; color: #fff !important; text-decoration: none; border-radius: 5px; font-weight: bold; }}
        .warning {{ background-color: #fff3cd; border-left: 4px solid #ffc107; padding: 12px; margin: 20px 0; border-radius: 4px; }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>Reset Your Password</h1>
        <p>You requested a password reset for your MyStore account.</p>
        <p><a href=""{resetUrl}"" class=""button"">Reset Password</a></p>
        <p>Or copy and paste this link: <a href=""{resetUrl}"">{resetUrl}</a></p>
        <div class=""warning"">
            <strong>Important:</strong> This link expires in 1 hour and can only be used once. If you did not request this, please ignore this email.
        </div>
        <p>Best regards,<br>The MyStore Team</p>
    </div>
</body>
</html>";
    }

    /// <summary>
    /// Sends a verification email to the specified recipient with a verification token.
    /// Includes both HTML and plain text content, and implements retry logic with exponential backoff.
    /// </summary>
    /// <param name="toEmail">The email address of the recipient.</param>
    /// <param name="verificationToken">The secure verification token to include in the email link.</param>
    /// <param name="companyName">The name of the company/store for personalization.</param>
    /// <returns>An EmailSendResult indicating success or failure of the email send operation.</returns>
    public async Task<EmailSendResult> SendVerificationEmailAsync(string toEmail, string verificationToken, string companyName)
    {
        // If email service is not configured, return success (account creation still proceeds)
        if (_emailClient == null)
        {
            _logger.LogWarning("Email service not configured. Skipping verification email for {Email}", toEmail);
            return new EmailSendResult
            {
                Success = true,
                ErrorMessage = "Email service not configured"
            };
        }

        try
        {
            // Build verification URL
            var verificationUrl = $"{_verificationBaseUrl}?token={Uri.EscapeDataString(verificationToken)}";
            
            // Create email content
            var emailContent = new EmailContent("Verify Your MyStore Account")
            {
                PlainText = GetPlainTextContent(companyName, verificationUrl),
                Html = GetHtmlContent(companyName, verificationUrl)
            };
            
            var emailMessage = new EmailMessage(
                senderAddress: _fromEmail,
                recipientAddress: toEmail,
                content: emailContent
            );
            
            // Send email with retry logic
            var success = await SendWithRetryAsync(emailMessage);
            
            if (success)
            {
                _logger.LogInformation(
                    "Verification email sent successfully to {Email}",
                    toEmail
                );
                
                return new EmailSendResult
                {
                    Success = true
                };
            }
            else
            {
                _logger.LogError(
                    "Failed to send verification email to {Email} after retries",
                    toEmail
                );
                
                return new EmailSendResult
                {
                    Success = false,
                    ErrorMessage = "Failed to send email after retries"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while sending verification email to {Email}", toEmail);
            return new EmailSendResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Sends an invite email to a new user (employee) with a link to set their password.
    /// </summary>
    public async Task<EmailSendResult> SendUserInviteEmailAsync(string toEmail, string inviteToken, string companyName, string firstName)
    {
        if (_emailClient == null)
        {
            _logger.LogWarning("Email service not configured. Skipping user invite email for {Email}", toEmail);
            return new EmailSendResult { Success = true, ErrorMessage = "Email service not configured" };
        }

        try
        {
            var inviteUrl = $"{_userInviteBaseUrl}?token={Uri.EscapeDataString(inviteToken)}";
            var emailContent = new EmailContent("Set Up Your Retro Store Manager Password")
            {
                PlainText = GetUserInvitePlainText(firstName, companyName, inviteUrl),
                Html = GetUserInviteHtml(firstName, companyName, inviteUrl)
            };

            var emailMessage = new EmailMessage(
                senderAddress: _fromEmail,
                recipientAddress: toEmail,
                content: emailContent
            );

            var success = await SendWithRetryAsync(emailMessage);
            if (success)
            {
                _logger.LogInformation("User invite email sent successfully to {Email}", toEmail);
                return new EmailSendResult { Success = true };
            }

            _logger.LogError("Failed to send user invite email to {Email} after retries", toEmail);
            return new EmailSendResult { Success = false, ErrorMessage = "Failed to send email after retries" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while sending user invite email to {Email}", toEmail);
            return new EmailSendResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <inheritdoc />
    public async Task<EmailSendResult> SendCustomerPortalInviteEmailAsync(string toEmail, string inviteToken, string companyName, string firstName)
    {
        if (_emailClient == null)
        {
            _logger.LogWarning("Email service not configured. Skipping customer portal invite for {Email}", toEmail);
            return new EmailSendResult { Success = true, ErrorMessage = "Email service not configured" };
        }

        try
        {
            var inviteUrl = $"{_userInviteBaseUrl}?token={Uri.EscapeDataString(inviteToken)}";
            var emailContent = new EmailContent("Set Up Your Customer Account")
            {
                PlainText = GetCustomerPortalInvitePlainText(firstName, companyName, inviteUrl),
                Html = GetCustomerPortalInviteHtml(firstName, companyName, inviteUrl)
            };

            var emailMessage = new EmailMessage(
                senderAddress: _fromEmail,
                recipientAddress: toEmail,
                content: emailContent
            );

            var success = await SendWithRetryAsync(emailMessage);
            if (success)
            {
                _logger.LogInformation("Customer portal invite email sent successfully to {Email}", toEmail);
                return new EmailSendResult { Success = true };
            }

            _logger.LogError("Failed to send customer portal invite email to {Email} after retries", toEmail);
            return new EmailSendResult { Success = false, ErrorMessage = "Failed to send email after retries" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while sending customer portal invite to {Email}", toEmail);
            return new EmailSendResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private static string GetCustomerPortalInvitePlainText(string firstName, string companyName, string inviteUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Hello {firstName},");
        sb.AppendLine();
        sb.AppendLine($"{companyName} has invited you to create a password for your customer account on Retro Store Manager.");
        sb.AppendLine();
        sb.AppendLine("Use the link below to choose your password (expires in 7 days):");
        sb.AppendLine();
        sb.AppendLine(inviteUrl);
        sb.AppendLine();
        sb.AppendLine("After setting your password, sign in from your store’s customer link using this same email.");
        sb.AppendLine();
        sb.AppendLine("If you did not expect this, you can ignore this email.");
        sb.AppendLine();
        sb.AppendLine("Best regards,");
        sb.AppendLine("The Retro Store Manager Team");
        return sb.ToString();
    }

    private static string GetCustomerPortalInviteHtml(string firstName, string companyName, string inviteUrl)
    {
        return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Your customer account</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px; }}
        .container {{ background-color: #fff; border-radius: 8px; padding: 40px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .button {{ display: inline-block; padding: 14px 32px; background-color: #16a085; color: #fff !important; text-decoration: none; border-radius: 5px; font-weight: bold; }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>Your customer account</h1>
        <p>Hello {firstName},</p>
        <p><strong>{companyName}</strong> invited you to create a password for your customer account on Retro Store Manager.</p>
        <p><a href=""{inviteUrl}"" class=""button"">Choose your password</a></p>
        <p>Or copy this link: <a href=""{inviteUrl}"">{inviteUrl}</a></p>
        <p><strong>This link expires in 7 days.</strong> Afterward, sign in from your store’s customer page using this email.</p>
        <p>If you did not expect this, you can ignore this email.</p>
        <p>Best regards,<br>The Retro Store Manager Team</p>
    </div>
</body>
</html>";
    }

    private static string GetUserInvitePlainText(string firstName, string companyName, string inviteUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Hello {firstName},");
        sb.AppendLine();
        sb.AppendLine($"You've been invited to join {companyName} on Retro Store Manager.");
        sb.AppendLine();
        sb.AppendLine("Click the link below to set up your password and activate your account:");
        sb.AppendLine();
        sb.AppendLine(inviteUrl);
        sb.AppendLine();
        sb.AppendLine("This link will expire in 7 days.");
        sb.AppendLine();
        sb.AppendLine("If you did not expect this invitation, please ignore this email.");
        sb.AppendLine();
        sb.AppendLine("Best regards,");
        sb.AppendLine("The Retro Store Manager Team");
        return sb.ToString();
    }

    private static string GetUserInviteHtml(string firstName, string companyName, string inviteUrl)
    {
        return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Set Up Your Password</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px; }}
        .container {{ background-color: #fff; border-radius: 8px; padding: 40px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .button {{ display: inline-block; padding: 14px 32px; background-color: #3498db; color: #fff !important; text-decoration: none; border-radius: 5px; font-weight: bold; }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>You're Invited!</h1>
        <p>Hello {firstName},</p>
        <p>You've been invited to join <strong>{companyName}</strong> on Retro Store Manager.</p>
        <p>Click the button below to set up your password and activate your account:</p>
        <p><a href=""{inviteUrl}"" class=""button"">Set Up Password</a></p>
        <p>Or copy and paste this link: <a href=""{inviteUrl}"">{inviteUrl}</a></p>
        <p><strong>This link expires in 7 days.</strong></p>
        <p>If you did not expect this invitation, please ignore this email.</p>
        <p>Best regards,<br>The Retro Store Manager Team</p>
    </div>
</body>
</html>";
    }

    /// <inheritdoc />
    public async Task<EmailSendResult> SendReceiptEmailAsync(string toEmail, ReceiptResponse receipt)
    {
        if (_emailClient == null)
        {
            _logger.LogWarning("Email service not configured. Skipping receipt email for {Email}", toEmail);
            return new EmailSendResult { Success = true, ErrorMessage = "Email service not configured" };
        }

        try
        {
            var subject = $"Your Receipt #{receipt.ReceiptNumber} from {receipt.StoreName}";
            var emailContent = new EmailContent(subject)
            {
                PlainText = GetReceiptPlainText(receipt),
                Html = GetReceiptHtml(receipt)
            };

            var emailMessage = new EmailMessage(
                senderAddress: _fromEmail,
                recipientAddress: toEmail,
                content: emailContent
            );

            var success = await SendWithRetryAsync(emailMessage);
            if (success)
            {
                _logger.LogInformation("Receipt email sent successfully to {Email} for receipt {ReceiptNumber}", toEmail, receipt.ReceiptNumber);
                return new EmailSendResult { Success = true };
            }

            _logger.LogError("Failed to send receipt email to {Email} after retries", toEmail);
            return new EmailSendResult { Success = false, ErrorMessage = "Failed to send email after retries" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while sending receipt email to {Email}", toEmail);
            return new EmailSendResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private static string GetReceiptPlainText(ReceiptResponse receipt)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Receipt #{receipt.ReceiptNumber}");
        sb.AppendLine($"Date: {receipt.Date:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine(receipt.StoreName);
        if (!string.IsNullOrWhiteSpace(receipt.StoreAddress)) sb.AppendLine(receipt.StoreAddress);
        if (!string.IsNullOrWhiteSpace(receipt.StorePhone)) sb.AppendLine(receipt.StorePhone);
        sb.AppendLine();
        sb.AppendLine("Items:");
        foreach (var item in receipt.Items)
        {
            sb.AppendLine($"  {item.Name} x{item.Qty}  @{item.UnitPrice:C}  = {item.LineTotal:C}");
        }
        sb.AppendLine();
        sb.AppendLine($"Subtotal: {receipt.Subtotal:C}");
        if (receipt.TaxRate > 0)
        {
            var label = string.IsNullOrWhiteSpace(receipt.TaxLabel) ? "Tax" : receipt.TaxLabel;
            sb.AppendLine($"{label} ({receipt.TaxRate:P2}): {receipt.TaxAmount:C}");
        }
        sb.AppendLine($"Total: {receipt.Total:C}");
        sb.AppendLine($"Payment: {receipt.PaymentMethod}");
        if (!string.IsNullOrWhiteSpace(receipt.EmployeeName))
            sb.AppendLine($"Served by: {receipt.EmployeeName}");
        return sb.ToString();
    }

    private static string GetReceiptHtml(ReceiptResponse receipt)
    {
        var sb = new StringBuilder();
        sb.Append($@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Receipt #{receipt.ReceiptNumber}</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px; }}
        .container {{ background-color: #fff; border-radius: 8px; padding: 40px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        table {{ width: 100%; border-collapse: collapse; margin: 16px 0; }}
        th {{ text-align: left; border-bottom: 2px solid #333; padding: 6px 4px; }}
        td {{ padding: 6px 4px; border-bottom: 1px solid #eee; }}
        .totals td {{ border: none; }}
        .total-row {{ font-weight: bold; font-size: 1.1em; }}
        .store-info {{ margin-bottom: 24px; }}
        .receipt-meta {{ color: #555; margin-bottom: 16px; }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>Receipt #{receipt.ReceiptNumber}</h1>
        <div class=""receipt-meta"">Date: {receipt.Date:yyyy-MM-dd HH:mm}</div>
        <div class=""store-info"">
            <strong>{receipt.StoreName}</strong>");

        if (!string.IsNullOrWhiteSpace(receipt.StoreAddress))
            sb.Append($"<br>{receipt.StoreAddress}");
        if (!string.IsNullOrWhiteSpace(receipt.StorePhone))
            sb.Append($"<br>{receipt.StorePhone}");

        sb.Append(@"
        </div>
        <table>
            <thead>
                <tr>
                    <th>Item</th>
                    <th>Qty</th>
                    <th>Unit Price</th>
                    <th>Line Total</th>
                </tr>
            </thead>
            <tbody>");

        foreach (var item in receipt.Items)
        {
            sb.Append($@"
                <tr>
                    <td>{System.Net.WebUtility.HtmlEncode(item.Name)}</td>
                    <td>{item.Qty}</td>
                    <td>{item.UnitPrice:C}</td>
                    <td>{item.LineTotal:C}</td>
                </tr>");
        }

        sb.Append($@"
            </tbody>
        </table>
        <table class=""totals"">
            <tr><td>Subtotal</td><td align=""right"">{receipt.Subtotal:C}</td></tr>");

        if (receipt.TaxRate > 0)
        {
            var label = string.IsNullOrWhiteSpace(receipt.TaxLabel) ? "Tax" : receipt.TaxLabel;
            sb.Append($@"<tr><td>{System.Net.WebUtility.HtmlEncode(label)} ({receipt.TaxRate:P2})</td><td align=""right"">{receipt.TaxAmount:C}</td></tr>");
        }

        sb.Append($@"
            <tr class=""total-row""><td>Total</td><td align=""right"">{receipt.Total:C}</td></tr>
            <tr><td>Payment</td><td align=""right"">{System.Net.WebUtility.HtmlEncode(receipt.PaymentMethod)}</td></tr>");

        if (!string.IsNullOrWhiteSpace(receipt.EmployeeName))
            sb.Append($@"<tr><td>Served by</td><td align=""right"">{System.Net.WebUtility.HtmlEncode(receipt.EmployeeName)}</td></tr>");

        sb.Append(@"
        </table>
        <p>Thank you for your purchase!</p>
    </div>
</body>
</html>");

        return sb.ToString();
    }

    /// <summary>
    /// Sends an email with retry logic using exponential backoff.
    /// Retries up to 3 times for transient errors (5xx status codes or 429 rate limiting).
    /// </summary>
    /// <param name="emailMessage">The email message to send.</param>
    /// <returns>True if the email was sent successfully, false otherwise.</returns>
    private async Task<bool> SendWithRetryAsync(EmailMessage emailMessage)
    {
        const int maxRetries = 3;
        const int baseDelayMs = 1000; // 1 second
        
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                EmailSendOperation emailSendOperation = await _emailClient.SendAsync(
                    WaitUntil.Started,
                    emailMessage
                );
                
                // Check if the operation completed successfully
                // Note: We use WaitUntil.Started to get the operation immediately,
                // but in production you might want to poll for completion
                if (emailSendOperation.HasValue)
                {
                    return true; // Success
                }
                
                // If we get here, the operation started but we should check status
                // For simplicity, we'll consider it successful if no exception was thrown
                return true;
            }
            catch (RequestFailedException ex)
            {
                // Check if it's a retryable error (5xx or 429)
                var isRetryable = ex.Status >= 500 || ex.Status == 429;
                
                if (!isRetryable || attempt == maxRetries - 1)
                {
                    _logger.LogError(ex, "Email send failed with non-retryable error or max retries reached. Status: {Status}", ex.Status);
                    return false;
                }
                
                // Calculate exponential backoff delay
                var delayMs = baseDelayMs * (int)Math.Pow(2, attempt);
                _logger.LogWarning(
                    ex,
                    "Email send failed (attempt {Attempt}/{MaxRetries}). Retrying in {DelayMs}ms. Status: {Status}",
                    attempt + 1,
                    maxRetries,
                    delayMs,
                    ex.Status
                );
                
                await Task.Delay(delayMs);
            }
            catch (Exception ex)
            {
                if (attempt == maxRetries - 1)
                {
                    _logger.LogError(ex, "Email send failed after {MaxRetries} attempts", maxRetries);
                    return false;
                }
                
                var delayMs = baseDelayMs * (int)Math.Pow(2, attempt);
                _logger.LogWarning(
                    ex,
                    "Email send exception (attempt {Attempt}/{MaxRetries}). Retrying in {DelayMs}ms",
                    attempt + 1,
                    maxRetries,
                    delayMs
                );
                
                await Task.Delay(delayMs);
            }
        }
        
        return false;
    }

    private static string GetPlainTextContent(string companyName, string verificationUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Welcome to MyStore, {companyName}!");
        sb.AppendLine();
        sb.AppendLine("Thank you for creating an account. To complete your registration and activate your account, please verify your email address by clicking the link below:");
        sb.AppendLine();
        sb.AppendLine(verificationUrl);
        sb.AppendLine();
        sb.AppendLine("This verification link will expire in 24 hours.");
        sb.AppendLine();
        sb.AppendLine("If you did not create an account with MyStore, please ignore this email.");
        sb.AppendLine();
        sb.AppendLine("Best regards,");
        sb.AppendLine("The MyStore Team");
        
        return sb.ToString();
    }

    private static string GetHtmlContent(string companyName, string verificationUrl)
    {
        return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Verify Your MyStore Account</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            line-height: 1.6;
            color: #333;
            max-width: 600px;
            margin: 0 auto;
            padding: 20px;
            background-color: #f4f4f4;
        }}
        .container {{
            background-color: #ffffff;
            border-radius: 8px;
            padding: 40px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }}
        .header {{
            text-align: center;
            margin-bottom: 30px;
        }}
        .header h1 {{
            color: #2c3e50;
            margin: 0;
            font-size: 28px;
        }}
        .content {{
            margin-bottom: 30px;
        }}
        .button-container {{
            text-align: center;
            margin: 30px 0;
        }}
        .button {{
            display: inline-block;
            padding: 14px 32px;
            background-color: #3498db;
            color: #ffffff !important;
            text-decoration: none;
            border-radius: 5px;
            font-weight: bold;
            font-size: 16px;
        }}
        .button:hover {{
            background-color: #2980b9;
        }}
        .link {{
            color: #3498db;
            word-break: break-all;
        }}
        .footer {{
            margin-top: 30px;
            padding-top: 20px;
            border-top: 1px solid #e0e0e0;
            font-size: 12px;
            color: #7f8c8d;
            text-align: center;
        }}
        .warning {{
            background-color: #fff3cd;
            border-left: 4px solid #ffc107;
            padding: 12px;
            margin: 20px 0;
            border-radius: 4px;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>Welcome to MyStore!</h1>
        </div>
        <div class=""content"">
            <p>Hello {companyName},</p>
            <p>Thank you for creating an account with MyStore. To complete your registration and activate your account, please verify your email address by clicking the button below:</p>
            
            <div class=""button-container"">
                <a href=""{verificationUrl}"" class=""button"">Verify Email Address</a>
            </div>
            
            <p>Or copy and paste this link into your browser:</p>
            <p><a href=""{verificationUrl}"" class=""link"">{verificationUrl}</a></p>
            
            <div class=""warning"">
                <strong>Important:</strong> This verification link will expire in 24 hours. If you did not create an account with MyStore, please ignore this email.
            </div>
        </div>
        <div class=""footer"">
            <p>Best regards,<br>The MyStore Team</p>
        </div>
    </div>
</body>
</html>";
    }
}
