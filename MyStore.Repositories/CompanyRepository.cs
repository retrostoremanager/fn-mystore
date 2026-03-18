using Dapper;
using Npgsql;
using MyStore.Models;

namespace MyStore.Repositories;

public class CompanyRepository : ICompanyRepository
{
    private readonly string _connectionString;

    static CompanyRepository()
    {
        // Configure Dapper to map snake_case database columns to PascalCase properties
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public CompanyRepository()
    {
        // Get connection string from environment variable (standard for Azure Functions)
        // Try ConnectionStrings__DefaultConnection first (Azure Functions standard), then PostgresConnectionString
        _connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") 
            ?? Environment.GetEnvironmentVariable("PostgresConnectionString")
            ?? throw new InvalidOperationException("Connection string environment variable is not set");
    }

    public async Task<Company?> GetByIdAsync(int id)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<Company>(
            "SELECT * FROM company_get_by_id(@p_id)",
            new { p_id = id });
    }

    public async Task<Company?> GetByEmailAsync(string email)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<Company>(
            "SELECT * FROM company_get_by_email(@p_email)",
            new { p_email = email });
    }

    public async Task<Company?> GetBySlugAsync(string slug)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<Company>(
            "SELECT * FROM company_get_by_slug(@p_slug)",
            new { p_slug = slug });
    }

    public async Task<IEnumerable<Company>> GetExpiringTrialsAsync(int daysRemaining)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryAsync<Company>(
            "SELECT * FROM company_get_expiring_trials(@p_days)",
            new { p_days = daysRemaining });
    }

    public async Task MarkTrialNotificationSentAsync(int companyId, int daysRemaining)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(
            "SELECT company_mark_trial_notification_sent(@p_id, @p_days)",
            new { p_id = companyId, p_days = daysRemaining });
    }

    public async Task<IEnumerable<TrialConversionCandidate>> GetExpiredTrialsForConversionAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryAsync<TrialConversionCandidate>(
            "SELECT * FROM company_get_expired_trials_for_conversion()");
    }

    public async Task UpdateSubscriptionTierAsync(int companyId, string subscriptionTier)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(
            "SELECT company_update_subscription_tier(@p_id, @p_subscription_tier)",
            new { p_id = companyId, p_subscription_tier = subscriptionTier });
    }

    public async Task<IEnumerable<Company>> GetExpiredTrialsForSuspensionAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryAsync<Company>(
            "SELECT * FROM company_get_expired_trials_for_suspension()");
    }

    public async Task UpdateStatusAsync(int companyId, string status)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(
            "SELECT company_update_status(@p_id, @p_status)",
            new { p_id = companyId, p_status = status });
    }

    public async Task<Company?> GetByVerificationTokenAsync(string token)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<Company>(
            "SELECT * FROM company_get_by_verification_token(@p_token)",
            new { p_token = token });
    }

    public async Task<Company?> GetByPasswordResetTokenAsync(string token)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<Company>(
            "SELECT * FROM company_get_by_password_reset_token(@p_token)",
            new { p_token = token });
    }

    public async Task<Company> CreateAsync(Company company)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var id = await connection.QuerySingleAsync<int>(
            "SELECT company_create(@p_email, @p_status, @p_trial_start_date, @p_trial_end_date, @p_subscription_tier, @p_created_date, @p_verification_token, @p_verification_token_expires::timestamptz, @p_last_modified_date::timestamptz, @p_password_hash, @p_company_name, @p_slug)",
            new
            {
                p_email = company.Email,
                p_status = company.Status,
                p_trial_start_date = company.TrialStartDate,
                p_trial_end_date = company.TrialEndDate,
                p_subscription_tier = company.SubscriptionTier,
                p_created_date = company.CreatedDate,
                p_verification_token = company.VerificationToken,
                p_verification_token_expires = company.VerificationTokenExpires,
                p_last_modified_date = company.LastModifiedDate,
                p_password_hash = company.PasswordHash,
                p_company_name = company.CompanyName,
                p_slug = (string?)null
            });

        company.Id = id;
        return company;
    }

    public async Task<Company?> UpdateAsync(int id, Company company)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var rowsAffected = await connection.QuerySingleAsync<int>(
            "SELECT company_update(@p_id, @p_email, @p_status, @p_trial_start_date, @p_trial_end_date, @p_subscription_tier, @p_verification_token, @p_verification_token_expires::timestamptz, @p_password_hash, @p_password_reset_token, @p_password_reset_token_expires::timestamptz, @p_last_modified_date::timestamptz)",
            new
            {
                p_id = id,
                p_email = company.Email,
                p_status = company.Status,
                p_trial_start_date = company.TrialStartDate,
                p_trial_end_date = company.TrialEndDate,
                p_verification_token = company.VerificationToken,
                p_verification_token_expires = company.VerificationTokenExpires,
                p_password_hash = company.PasswordHash,
                p_password_reset_token = company.PasswordResetToken,
                p_password_reset_token_expires = company.PasswordResetTokenExpires,
                p_subscription_tier = company.SubscriptionTier,
                p_last_modified_date = company.LastModifiedDate
            });

        if (rowsAffected == 0)
        {
            return null;
        }

        company.Id = id;
        return company;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var rowsDeleted = await connection.QuerySingleAsync<int>(
            "SELECT company_delete(@p_id)",
            new { p_id = id });
        return rowsDeleted > 0;
    }

    public async Task<CompanyProfile?> GetProfileAsync(int companyId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<CompanyProfile>(
            "SELECT * FROM company_get_profile(@p_id)",
            new { p_id = companyId });
    }

    public async Task UpdateProfileAsync(int companyId, CompanyProfileUpdateRequest request)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(
            "SELECT company_update_profile(@p_id, @p_company_name, @p_company_address, @p_company_city, @p_company_state, @p_company_zip_code, @p_company_phone, @p_locale, @p_logo_url)",
            new
            {
                p_id = companyId,
                p_company_name = request.CompanyName,
                p_company_address = request.CompanyAddress,
                p_company_city = request.CompanyCity,
                p_company_state = request.CompanyState,
                p_company_zip_code = request.CompanyZipCode,
                p_company_phone = request.CompanyPhone,
                p_locale = request.Locale,
                p_logo_url = request.LogoUrl
            });
    }
}
