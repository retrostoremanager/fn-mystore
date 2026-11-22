using FluentAssertions;
using MyStore.Models;
using MyStore.Repositories;
using Xunit;

namespace MyStore.Tests.Repositories;

/// <summary>
/// Tests for <see cref="CompanyRepository"/>.
///
/// The repository calls Postgres stored functions (company_get_by_id, company_create, etc.)
/// directly via a connection string read from the environment, so the data-access methods are
/// covered by real integration tests (the <c>[IntegrationFact]</c> ones below) that run against
/// an ephemeral Postgres seeded from the <c>retrostoremanager/dbproj-mystore</c> migrations --
/// same pattern as <see cref="TradeInRepositoryTests"/>. Those run only in the integration-tests
/// workflow, which exports <c>RUN_DB_INTEGRATION_TESTS</c> and <c>MYSTORE_TEST_DB</c>; in ordinary
/// unit runs they are skipped.
/// </summary>
[Collection("ConnectionStringEnv")]
public class CompanyRepositoryTests
{
    private static Company NewCompany(string email, string status = "Active", string? companyName = null) => new()
    {
        Email = email,
        Status = status,
        TrialStartDate = DateTime.UtcNow,
        TrialEndDate = DateTime.UtcNow.AddDays(14),
        SubscriptionTier = "Trial",
        CreatedDate = DateTime.UtcNow,
        CompanyName = companyName,
    };

    [IntegrationFact]
    public async Task GetByEmailAsync_ExistingEmail_ReturnsCompany()
    {
        IntegrationDb.UseForRepositories();
        var repo = new CompanyRepository();
        var email = $"get-by-email-{Guid.NewGuid():N}@example.test";
        var created = await repo.CreateAsync(NewCompany(email));

        var fetched = await repo.GetByEmailAsync(email);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
        fetched.Email.Should().Be(email);
    }

    [IntegrationFact]
    public async Task GetByEmailAsync_NonExistentEmail_ReturnsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new CompanyRepository();

        (await repo.GetByEmailAsync($"no-such-company-{Guid.NewGuid():N}@example.test")).Should().BeNull();
    }

    [IntegrationFact]
    public async Task CreateAsync_ValidCompany_ReturnsCompanyWithId()
    {
        IntegrationDb.UseForRepositories();
        var repo = new CompanyRepository();
        var email = $"create-{Guid.NewGuid():N}@example.test";

        var created = await repo.CreateAsync(NewCompany(email, companyName: "Acme Co"));

        created.Id.Should().BeGreaterThan(0);
        created.Email.Should().Be(email);
        created.SubscriptionTier.Should().Be("Trial");

        // company_create auto-generates a slug server-side (from company_name, deduplicated) --
        // confirm a row with the expected shape actually landed, not just that CreateAsync
        // returned without throwing.
        var fetched = await repo.GetByIdAsync(created.Id);
        fetched.Should().NotBeNull();
        fetched!.Slug.Should().NotBeNullOrEmpty();
    }

    [IntegrationFact]
    public async Task GetByIdAsync_ExistingId_ReturnsCompany()
    {
        IntegrationDb.UseForRepositories();
        var repo = new CompanyRepository();
        var created = await repo.CreateAsync(NewCompany($"get-by-id-{Guid.NewGuid():N}@example.test"));

        var fetched = await repo.GetByIdAsync(created.Id);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
    }

    [IntegrationFact]
    public async Task GetByIdAsync_NonExistentId_ReturnsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new CompanyRepository();

        (await repo.GetByIdAsync(999999)).Should().BeNull();
    }

    [IntegrationFact]
    public async Task GetByVerificationTokenAsync_ValidToken_ReturnsCompany()
    {
        IntegrationDb.UseForRepositories();
        var repo = new CompanyRepository();
        var company = NewCompany($"verify-{Guid.NewGuid():N}@example.test");
        company.VerificationToken = Guid.NewGuid().ToString("N");
        company.VerificationTokenExpires = DateTime.UtcNow.AddHours(1);
        var created = await repo.CreateAsync(company);

        var fetched = await repo.GetByVerificationTokenAsync(company.VerificationToken);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
    }

    [IntegrationFact]
    public async Task GetByVerificationTokenAsync_InvalidToken_ReturnsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new CompanyRepository();

        (await repo.GetByVerificationTokenAsync(Guid.NewGuid().ToString("N"))).Should().BeNull();
    }

    [IntegrationFact]
    public async Task UpdateAsync_ExistingCompany_ReturnsUpdatedCompany()
    {
        IntegrationDb.UseForRepositories();
        var repo = new CompanyRepository();
        var created = await repo.CreateAsync(NewCompany($"update-{Guid.NewGuid():N}@example.test"));

        created.Status = "Suspended";
        var updated = await repo.UpdateAsync(created.Id, created);

        updated.Should().NotBeNull();
        updated!.Id.Should().Be(created.Id);

        var fetched = await repo.GetByIdAsync(created.Id);
        fetched!.Status.Should().Be("Suspended");
    }

    [IntegrationFact]
    public async Task UpdateAsync_NonExistentId_ReturnsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new CompanyRepository();
        var ghost = NewCompany($"ghost-{Guid.NewGuid():N}@example.test");

        (await repo.UpdateAsync(999999, ghost)).Should().BeNull();
    }
}
