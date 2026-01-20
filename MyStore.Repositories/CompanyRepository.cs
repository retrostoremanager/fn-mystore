using Dapper;
using Npgsql;
using MyStore.Models;

namespace MyStore.Repositories;

public class CompanyRepository : ICompanyRepository
{
    private readonly string _connectionString;

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
            "SELECT * FROM company_get_by_id(@id)",
            new { id });
    }

    public async Task<Company?> GetByEmailAsync(string email)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<Company>(
            "SELECT * FROM company_get_by_email(@email)",
            new { email });
    }

    public async Task<Company?> GetByVerificationTokenAsync(string token)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<Company>(
            "SELECT * FROM company_get_by_verification_token(@token)",
            new { token });
    }

    public async Task<Company> CreateAsync(Company company)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var id = await connection.QuerySingleAsync<int>(
            "SELECT company_create(@email, @status, @trialstartdate, @trialenddate, @verificationtoken, @verificationtokenexpires, @subscriptiontier, @createddate, @lastmodifieddate)",
            new
            {
                email = company.Email,
                status = company.Status,
                trialstartdate = company.TrialStartDate,
                trialenddate = company.TrialEndDate,
                verificationtoken = company.VerificationToken,
                verificationtokenexpires = company.VerificationTokenExpires,
                subscriptiontier = company.SubscriptionTier,
                createddate = company.CreatedDate,
                lastmodifieddate = company.LastModifiedDate
            });

        company.Id = id;
        return company;
    }

    public async Task<Company?> UpdateAsync(int id, Company company)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var rowsAffected = await connection.QuerySingleAsync<int>(
            "SELECT company_update(@id, @email, @status, @trialstartdate, @trialenddate, @verificationtoken, @verificationtokenexpires, @subscriptiontier, @lastmodifieddate)",
            new
            {
                id,
                email = company.Email,
                status = company.Status,
                trialstartdate = company.TrialStartDate,
                trialenddate = company.TrialEndDate,
                verificationtoken = company.VerificationToken,
                verificationtokenexpires = company.VerificationTokenExpires,
                subscriptiontier = company.SubscriptionTier,
                lastmodifieddate = company.LastModifiedDate
            });

        if (rowsAffected == 0)
        {
            return null;
        }

        company.Id = id;
        return company;
    }
}
