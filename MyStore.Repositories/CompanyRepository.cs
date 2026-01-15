using Dapper;
using Microsoft.Data.SqlClient;
using MyStore.Models;

namespace MyStore.Repositories;

public class CompanyRepository : ICompanyRepository
{
    private readonly string _connectionString;

    public CompanyRepository()
    {
        // Get connection string from environment variable (standard for Azure Functions)
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString") 
            ?? throw new InvalidOperationException("SqlConnectionString environment variable is not set");
    }

    public async Task<Company?> GetByIdAsync(int id)
    {
        await using var connection = new SqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<Company>(
            "spCompany_GetById",
            new { Id = id },
            commandType: System.Data.CommandType.StoredProcedure);
    }

    public async Task<Company?> GetByEmailAsync(string email)
    {
        await using var connection = new SqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<Company>(
            "spCompany_GetByEmail",
            new { Email = email },
            commandType: System.Data.CommandType.StoredProcedure);
    }

    public async Task<Company?> GetByVerificationTokenAsync(string token)
    {
        await using var connection = new SqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<Company>(
            "spCompany_GetByVerificationToken",
            new { Token = token },
            commandType: System.Data.CommandType.StoredProcedure);
    }

    public async Task<Company> CreateAsync(Company company)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("@Email", company.Email);
        parameters.Add("@Status", company.Status);
        parameters.Add("@TrialStartDate", company.TrialStartDate);
        parameters.Add("@TrialEndDate", company.TrialEndDate);
        parameters.Add("@VerificationToken", company.VerificationToken);
        parameters.Add("@VerificationTokenExpires", company.VerificationTokenExpires);
        parameters.Add("@SubscriptionTier", company.SubscriptionTier);
        parameters.Add("@CreatedDate", company.CreatedDate);
        parameters.Add("@LastModifiedDate", company.LastModifiedDate);
        parameters.Add("@Id", dbType: System.Data.DbType.Int32, direction: System.Data.ParameterDirection.Output);

        await connection.ExecuteAsync(
            "spCompany_Create",
            parameters,
            commandType: System.Data.CommandType.StoredProcedure);

        company.Id = parameters.Get<int>("@Id");
        return company;
    }

    public async Task<Company?> UpdateAsync(int id, Company company)
    {
        await using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.QuerySingleAsync<int>(
            "spCompany_Update",
            new
            {
                Id = id,
                company.Email,
                company.Status,
                company.TrialStartDate,
                company.TrialEndDate,
                company.VerificationToken,
                company.VerificationTokenExpires,
                company.SubscriptionTier,
                company.LastModifiedDate
            },
            commandType: System.Data.CommandType.StoredProcedure);

        if (rowsAffected == 0)
        {
            return null;
        }

        company.Id = id;
        return company;
    }
}
