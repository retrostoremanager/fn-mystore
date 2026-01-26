# Email Service Setup and Verification Guide

## Overview

The MyStore application uses **Azure Communication Services Email** to send verification emails during account registration. This service is **optional** for development - the application will work without it, but verification emails won't be sent.

## Configuration Status

### Current Status
- **Email Service**: Optional (gracefully degrades if not configured)
- **Provider**: Azure Communication Services Email
- **Package**: `Azure.Communication.Email` v1.0.1

### Required Environment Variables

Add these to `local.settings.json` (local development) or App Settings (Azure):

```json
{
  "Values": {
    "AzureCommunicationServices__ConnectionString": "endpoint=https://...;accesskey=...",
    "Email__FromEmail": "noreply@yourdomain.com",
    "Email__FromName": "MyStore",
    "VerificationBaseUrl": "https://localhost:5173/verify"
  }
}
```

| Variable | Required | Description | Example |
|----------|----------|-------------|---------|
| `AzureCommunicationServices__ConnectionString` | Yes | Connection string from Azure Communication Services | `endpoint=https://...;accesskey=...` |
| `Email__FromEmail` | Yes | Verified sender email address | `noreply@yourdomain.com` |
| `Email__FromName` | No | Display name for sender | `MyStore` (default) |
| `VerificationBaseUrl` | No | Base URL for verification links | `https://app.mystore.com/verify` (default) |

## Azure Communication Services Setup

### Step 1: Create Azure Communication Services Resource

1. Go to [Azure Portal](https://portal.azure.com)
2. Click **"Create a resource"** → Search for **"Communication Services"**
3. Fill in the details:
   - **Subscription**: Your Azure subscription
   - **Resource Group**: Create new or use existing
   - **Resource Name**: `mystore-communication` (or your choice)
   - **Data Location**: Choose your region
4. Click **"Review + Create"** → **"Create"**

### Step 2: Get Connection String

1. Go to your Communication Services resource
2. Navigate to **"Keys"** in the left menu
3. Copy the **"Connection string"** (Primary or Secondary)
4. Add to your `local.settings.json`:
   ```json
   "AzureCommunicationServices__ConnectionString": "endpoint=https://...;accesskey=..."
   ```

### Step 3: Set Up Email (Choose One Option)

#### Option A: Azure Managed Domain (Easiest)

1. In your Communication Services resource, go to **"Email"** → **"Try Email"**
2. Click **"Connect"** to create an Azure Managed Domain
3. This provides a free email domain: `[guid]@[region].azurecomm.net`
4. Use this email in `Email__FromEmail` configuration
5. ⚠️ **Note**: Emails from this domain may be marked as spam

#### Option B: Custom Domain (Recommended for Production)

1. In your Communication Services resource, go to **"Email"** → **"Provision Domains"**
2. Click **"Add Domain"** → **"Custom Domain"**
3. Enter your domain name (e.g., `yourdomain.com`)
4. Follow DNS verification steps to prove domain ownership
5. Once verified, configure sender email (e.g., `noreply@yourdomain.com`)
6. Add this email to `Email__FromEmail` configuration

### Step 4: Update Configuration

Update `fn-mystore/MyStore.Functions/local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "ASPNETCORE_ENVIRONMENT": "Development",
    "ConnectionStrings__DefaultConnection": "your-postgresql-connection-string",
    "AzureCommunicationServices__ConnectionString": "endpoint=https://mystore-communication.unitedstates.communication.azure.com/;accesskey=YOUR_KEY_HERE",
    "Email__FromEmail": "DoNotReply@YOUR_GUID_HERE.azurecomm.net",
    "Email__FromName": "MyStore",
    "VerificationBaseUrl": "http://localhost:5173/verify"
  }
}
```

## Verification Steps

### Quick Status Check

Run this command to check if email service is configured:

```bash
cd fn-mystore
dotnet run --project MyStore.Functions
```

Watch the console output:
- ✅ **If configured**: No warnings about email service
- ⚠️ **If NOT configured**: You'll see: `"Email service not configured. Skipping verification email..."`

### Manual Test: Send Test Email via API

1. **Start the Functions app**:
   ```bash
   cd fn-mystore
   func start
   ```

2. **Send a registration request**:
   ```bash
   curl -X POST http://localhost:7071/api/accounts/register \
     -H "Content-Type: application/json" \
     -d '{
       "email": "test@example.com",
       "password": "TestPass123",
       "companyName": "Test Store",
       "subscriptionTier": "Trial"
     }'
   ```

3. **Check the console logs**:
   - ✅ **Success**: `"Verification email queued successfully for account..."`
   - ⚠️ **Skipped**: `"Email service not configured. Skipping verification email..."`
   - ❌ **Error**: Look for specific error message and check configuration

4. **Check your email** (if configured):
   - Should receive verification email at `test@example.com`
   - Email should have subject: "Verify Your MyStore Account"
   - Email should contain verification button and link

### Automated Test

Run the existing unit tests to verify email service integration:

```bash
cd fn-mystore
dotnet test --filter "FullyQualifiedName~CompanyServiceTests.RegisterAccountAsync_SuccessfulRegistration_SendsVerificationEmail"
```

This test verifies:
- Email service is called during registration
- Correct parameters are passed
- Account creation succeeds regardless of email outcome

## Troubleshooting

### Issue: "Email service not configured"

**Cause**: Missing `AzureCommunicationServices__ConnectionString`

**Solution**: 
1. Verify connection string is in `local.settings.json`
2. Restart the Functions app
3. Check environment variable is set: `echo $env:AzureCommunicationServices__ConnectionString` (PowerShell)

### Issue: "Email__FromEmail environment variable is not configured"

**Cause**: Missing sender email address

**Solution**: 
1. Add `Email__FromEmail` to `local.settings.json`
2. Ensure email matches verified domain in Azure Communication Services
3. Restart the Functions app

### Issue: Emails not being delivered

**Possible Causes**:
1. **Sender email not verified**: Check domain verification in Azure Portal
2. **Email marked as spam**: Use custom domain instead of Azure Managed Domain
3. **Rate limits exceeded**: Check Azure Communication Services quotas
4. **Network/firewall issues**: Check Azure Communication Services is accessible

**Solution**:
1. Check Azure Communication Services resource status in Azure Portal
2. Review "Monitoring" → "Logs" for failed email attempts
3. Test with known good email address (your personal email)
4. Check spam folder

### Issue: Request fails with 401 Unauthorized

**Cause**: Invalid or expired access key in connection string

**Solution**:
1. Go to Azure Portal → Communication Services → Keys
2. Copy new connection string (Primary or Secondary)
3. Update `local.settings.json`
4. Restart Functions app

## Development Mode (Without Email Service)

For local development without Azure Communication Services:

1. **Leave email configuration empty** - the app will work without it
2. **Check logs** for verification tokens:
   ```
   Verification email queued successfully for account 123, email test@example.com
   ```
3. **Manual verification** (future task): Use the token from logs to verify accounts manually

## Production Deployment

For production, use Azure App Settings instead of `local.settings.json`:

1. Go to Azure Portal → Function App → Configuration
2. Add Application Settings:
   - `AzureCommunicationServices__ConnectionString`
   - `Email__FromEmail`
   - `Email__FromName`
   - `VerificationBaseUrl`
3. Save and restart Function App

## Email Template Customization

Email templates are defined in `MyStore.Services/EmailService.cs`:
- `GetHtmlContent()` - HTML email template
- `GetPlainTextContent()` - Plain text fallback

To customize:
1. Edit template methods in `EmailService.cs`
2. Test changes by sending test registration
3. Verify in multiple email clients (Gmail, Outlook, Apple Mail)

## Monitoring

### Azure Portal Monitoring

1. Go to Communication Services resource → Monitoring → Metrics
2. View metrics:
   - **Email Messages Sent**
   - **Email Messages Delivered**
   - **Email Messages Failed**

### Application Logs

All email operations are logged:
- **Success**: `LogInformation("Verification email queued successfully...")`
- **Failure**: `LogWarning("Failed to send verification email...")`
- **Error**: `LogError("Exception occurred while sending verification email...")`

## Cost Considerations

### Free Tier Limits
- **Azure Communication Services Email**: 
  - Free: 500 emails/month
  - After: $0.015 per email

### Recommendations
- Use Azure Managed Domain for development (free)
- Use Custom Domain for production (better deliverability)
- Monitor usage in Azure Portal to avoid unexpected costs

## Next Steps

After email service is configured and verified:
1. ✅ **EPIC-0-001-004**: Email Verification Service Integration (DONE)
2. 🔄 **EPIC-0-001-005**: Email Verification Link Handler (NEXT)
   - Create endpoint to handle verification link clicks
   - Validate tokens and activate accounts
   - Handle expired/invalid tokens

## Support

For issues with:
- **Azure Communication Services**: [Azure Support](https://azure.microsoft.com/support/)
- **Email deliverability**: Check spam settings, domain verification
- **Application errors**: Review Function App logs and Application Insights
