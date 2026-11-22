using FluentAssertions;
using MyStore.Models;
using MyStore.Repositories;
using Xunit;

namespace MyStore.Tests.Repositories;

/// <summary>
/// Tests for <see cref="ConsignmentRepository"/>.
///
/// The repository instantiates <see cref="Npgsql.NpgsqlConnection"/> directly from a connection
/// string read out of the environment, so the data-access methods are covered by real integration
/// tests (the <c>[IntegrationFact]</c> ones below) that run against an ephemeral Postgres seeded
/// from the <c>retrostoremanager/dbproj-mystore</c> migrations -- same pattern as
/// <see cref="TradeInRepositoryTests"/>. Those run only in the integration-tests workflow, which
/// exports <c>RUN_DB_INTEGRATION_TESTS</c> and <c>MYSTORE_TEST_DB</c>; in ordinary unit runs they
/// are skipped.
///
/// The plain <c>[Fact]</c> tests cover the connection-string contract without a database.
/// </summary>
[Collection("ConnectionStringEnv")]
public class ConsignmentRepositoryTests
{
    public ConsignmentRepositoryTests()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        Environment.SetEnvironmentVariable("PostgresConnectionString", null);
    }

    [Fact]
    public void Constructor_NoConnectionStringEnvVar_Throws()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        Environment.SetEnvironmentVariable("PostgresConnectionString", null);

        Action act = () => _ = new ConsignmentRepository();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Connection string*");
    }

    [Fact]
    public void Constructor_WithDefaultConnectionEnvVar_DoesNotThrow()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            "Host=localhost;Database=test;Username=test;Password=test");

        try
        {
            Action act = () => _ = new ConsignmentRepository();
            act.Should().NotThrow();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        }
    }

    [Fact]
    public void Constructor_FallsBackToPostgresConnectionStringEnvVar()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        Environment.SetEnvironmentVariable(
            "PostgresConnectionString",
            "Host=localhost;Database=test;Username=test;Password=test");

        try
        {
            Action act = () => _ = new ConsignmentRepository();
            act.Should().NotThrow();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PostgresConnectionString", null);
        }
    }

    [Fact]
    public void Repository_ImplementsIConsignmentRepository()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            "Host=localhost;Database=test;Username=test;Password=test");

        try
        {
            var repo = new ConsignmentRepository();
            repo.Should().BeAssignableTo<IConsignmentRepository>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        }
    }

    // ---------------------------------------------------------------------
    // Integration tests covering the acceptance criteria for issue #343.
    // Run against an ephemeral Postgres in the integration-tests workflow.
    // ---------------------------------------------------------------------

    private static async Task<(int companyId, int customerId)> SeedTenantAsync()
    {
        await using var conn = await IntegrationDb.OpenAsync();
        var companyId = await IntegrationDb.SeedCompanyAsync(conn);
        var customerId = await IntegrationDb.SeedCustomerAsync(conn, companyId);
        return (companyId, customerId);
    }

    private static ConsignmentItem NewItem(
        int companyId, int customerId, string description = "Test Item",
        decimal askingPrice = 20m, decimal splitPercent = 50m, string status = "active") => new()
    {
        CompanyId = companyId,
        CustomerId = customerId,
        Description = description,
        AskingPrice = askingPrice,
        SplitPercent = splitPercent,
        Status = status,
    };

    [IntegrationFact]
    public async Task GetAllAsync_FiltersByCompanyId()
    {
        IntegrationDb.UseForRepositories();
        var repo = new ConsignmentRepository();
        var (companyA, customerA) = await SeedTenantAsync();
        var (companyB, customerB) = await SeedTenantAsync();

        await repo.CreateAsync(NewItem(companyA, customerA));
        await repo.CreateAsync(NewItem(companyA, customerA));
        await repo.CreateAsync(NewItem(companyB, customerB));

        var result = await repo.GetAllAsync(companyA);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(i => i.CompanyId == companyA);
    }

    [IntegrationFact]
    public async Task GetAllAsync_AppliesOptionalStatusFilter()
    {
        IntegrationDb.UseForRepositories();
        var repo = new ConsignmentRepository();
        var (companyId, customerId) = await SeedTenantAsync();

        await repo.CreateAsync(NewItem(companyId, customerId, status: "active"));
        await repo.CreateAsync(NewItem(companyId, customerId, status: "sold"));

        var active = await repo.GetAllAsync(companyId, status: "active");

        active.Should().ContainSingle().Which.Status.Should().Be("active");
    }

    [IntegrationFact]
    public async Task GetByIdAsync_ExistingIdSameCompany_ReturnsConsignmentItem()
    {
        IntegrationDb.UseForRepositories();
        var repo = new ConsignmentRepository();
        var (companyId, customerId) = await SeedTenantAsync();
        var created = await repo.CreateAsync(NewItem(companyId, customerId, description: "Lookup Me"));

        var fetched = await repo.GetByIdAsync(created.Id, companyId);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
        fetched.Description.Should().Be("Lookup Me");
    }

    [IntegrationFact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new ConsignmentRepository();
        var (companyId, _) = await SeedTenantAsync();

        (await repo.GetByIdAsync(999999, companyId)).Should().BeNull();
    }

    [IntegrationFact]
    public async Task GetByIdAsync_DifferentCompany_ReturnsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new ConsignmentRepository();
        var (companyA, customerA) = await SeedTenantAsync();
        var (companyB, _) = await SeedTenantAsync();
        var created = await repo.CreateAsync(NewItem(companyA, customerA));

        (await repo.GetByIdAsync(created.Id, companyB)).Should().BeNull();
    }

    [IntegrationFact]
    public async Task CreateAsync_InsertsRowAndReturnsMappedFields()
    {
        IntegrationDb.UseForRepositories();
        var repo = new ConsignmentRepository();
        var (companyId, customerId) = await SeedTenantAsync();

        var created = await repo.CreateAsync(NewItem(
            companyId, customerId, description: "Rare Cart", askingPrice: 99.99m, splitPercent: 60m));

        created.Id.Should().BeGreaterThan(0);
        created.CreatedAt.Should().NotBe(default);
        created.CompanyId.Should().Be(companyId);
        created.CustomerId.Should().Be(customerId);
        created.Description.Should().Be("Rare Cart");
        created.AskingPrice.Should().Be(99.99m);
        created.SplitPercent.Should().Be(60m);
        created.Status.Should().Be("active");
    }

    [IntegrationFact]
    public async Task UpdateAsync_ExistingItem_UpdatesStatusAndPriceFields()
    {
        IntegrationDb.UseForRepositories();
        var repo = new ConsignmentRepository();
        var (companyId, customerId) = await SeedTenantAsync();
        var created = await repo.CreateAsync(NewItem(companyId, customerId));

        created.Description = "Updated Description";
        created.AskingPrice = 55m;
        created.SplitPercent = 40m;
        created.Status = "returned";

        var updated = await repo.UpdateAsync(created);

        updated.Should().NotBeNull();
        updated!.Description.Should().Be("Updated Description");
        updated.AskingPrice.Should().Be(55m);
        updated.SplitPercent.Should().Be(40m);
        updated.Status.Should().Be("returned");
        updated.UpdatedAt.Should().NotBeNull();
    }

    [IntegrationFact]
    public async Task UpdateAsync_NotFound_ReturnsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new ConsignmentRepository();
        var (companyId, customerId) = await SeedTenantAsync();
        var ghost = NewItem(companyId, customerId);
        ghost.Id = 999999;

        (await repo.UpdateAsync(ghost)).Should().BeNull();
    }

    [IntegrationFact]
    public async Task MarkSoldAsync_ExistingItem_SetsSalePriceAndStatusSold()
    {
        IntegrationDb.UseForRepositories();
        var repo = new ConsignmentRepository();
        var (companyId, customerId) = await SeedTenantAsync();
        var created = await repo.CreateAsync(NewItem(companyId, customerId));

        var sold = await repo.MarkSoldAsync(created.Id, 42.50m, companyId);

        sold.Should().NotBeNull();
        sold!.SalePrice.Should().Be(42.50m);
        sold.Status.Should().Be("sold");
        sold.UpdatedAt.Should().NotBeNull();
    }

    [IntegrationFact]
    public async Task MarkSoldAsync_NotFound_ReturnsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new ConsignmentRepository();
        var (companyId, _) = await SeedTenantAsync();

        (await repo.MarkSoldAsync(999999, 10m, companyId)).Should().BeNull();
    }

    [IntegrationFact]
    public async Task GetPayoutsAsync_FiltersByConsignmentItemId()
    {
        IntegrationDb.UseForRepositories();
        var repo = new ConsignmentRepository();
        var (companyId, customerId) = await SeedTenantAsync();
        var itemA = await repo.CreateAsync(NewItem(companyId, customerId));
        var itemB = await repo.CreateAsync(NewItem(companyId, customerId));

        await repo.CreatePayoutAsync(new ConsignmentPayout { ConsignmentItemId = itemA.Id, Amount = 10m });
        await repo.CreatePayoutAsync(new ConsignmentPayout { ConsignmentItemId = itemA.Id, Amount = 20m });
        await repo.CreatePayoutAsync(new ConsignmentPayout { ConsignmentItemId = itemB.Id, Amount = 99m });

        var payouts = await repo.GetPayoutsAsync(itemA.Id, companyId);

        payouts.Should().HaveCount(2);
        payouts.Should().OnlyContain(p => p.ConsignmentItemId == itemA.Id);
    }

    [IntegrationFact]
    public async Task GetPayoutsAsync_DifferentCompany_ReturnsEmpty()
    {
        IntegrationDb.UseForRepositories();
        var repo = new ConsignmentRepository();
        var (companyA, customerA) = await SeedTenantAsync();
        var (companyB, _) = await SeedTenantAsync();
        var item = await repo.CreateAsync(NewItem(companyA, customerA));
        await repo.CreatePayoutAsync(new ConsignmentPayout { ConsignmentItemId = item.Id, Amount = 10m });

        var payouts = await repo.GetPayoutsAsync(item.Id, companyB);

        payouts.Should().BeEmpty();
    }

    [IntegrationFact]
    public async Task CreatePayoutAsync_InsertsRowAndReturnsGeneratedId()
    {
        IntegrationDb.UseForRepositories();
        var repo = new ConsignmentRepository();
        var (companyId, customerId) = await SeedTenantAsync();
        var item = await repo.CreateAsync(NewItem(companyId, customerId));

        var payout = await repo.CreatePayoutAsync(new ConsignmentPayout
        {
            ConsignmentItemId = item.Id,
            Amount = 15.25m,
            Notes = "Half of sale",
        });

        payout.Id.Should().BeGreaterThan(0);
        payout.PaidAt.Should().NotBe(default);
        payout.ConsignmentItemId.Should().Be(item.Id);
        payout.Amount.Should().Be(15.25m);
        payout.Notes.Should().Be("Half of sale");
    }
}
