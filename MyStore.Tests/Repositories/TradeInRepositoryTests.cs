using FluentAssertions;
using MyStore.Models;
using MyStore.Repositories;
using Xunit;

namespace MyStore.Tests.Repositories;

/// <summary>
/// Tests for <see cref="TradeInRepository"/>.
///
/// The repository instantiates <see cref="Npgsql.NpgsqlConnection"/> directly from a connection
/// string read out of the environment, so the data-access methods are covered by real integration
/// tests (the <c>[IntegrationFact]</c> ones below) that run against an ephemeral Postgres seeded
/// from the <c>retrostoremanager/dbproj-mystore</c> migrations. Those run only in the
/// integration-tests workflow, which exports <c>RUN_DB_INTEGRATION_TESTS</c> and
/// <c>MYSTORE_TEST_DB</c>; in ordinary unit runs they are skipped.
///
/// The plain <c>[Fact]</c> tests cover the connection-string contract without a database.
/// </summary>
[Collection("ConnectionStringEnv")]
public class TradeInRepositoryTests
{
    public TradeInRepositoryTests()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        Environment.SetEnvironmentVariable("PostgresConnectionString", null);
    }

    [Fact]
    public void Constructor_NoConnectionStringEnvVar_Throws()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        Environment.SetEnvironmentVariable("PostgresConnectionString", null);

        Action act = () => _ = new TradeInRepository();

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
            Action act = () => _ = new TradeInRepository();
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
            Action act = () => _ = new TradeInRepository();
            act.Should().NotThrow();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PostgresConnectionString", null);
        }
    }

    [Fact]
    public void Repository_ImplementsITradeInRepository()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            "Host=localhost;Database=test;Username=test;Password=test");

        try
        {
            var repo = new TradeInRepository();
            repo.Should().BeAssignableTo<ITradeInRepository>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        }
    }

    // ---------------------------------------------------------------------
    // Integration tests covering the acceptance criteria for issue #342.
    // Run against an ephemeral Postgres in the integration-tests workflow.
    // ---------------------------------------------------------------------

    private static async Task<(int companyId, int userId)> SeedTenantAsync()
    {
        await using var conn = await IntegrationDb.OpenAsync();
        var companyId = await IntegrationDb.SeedCompanyAsync(conn);
        var userId = await IntegrationDb.SeedUserAsync(conn, companyId);
        return (companyId, userId);
    }

    private static TradeIn NewTradeIn(int companyId, int userId, string status = "draft",
        decimal offered = 10m, string? notes = null) => new()
    {
        CompanyId = companyId,
        CreatedBy = userId,
        Status = status,
        PaymentType = "cash",
        TotalOfferedValue = offered,
        Notes = notes,
    };

    private static TradeInItem NewItem(int tradeInId, string title = "Test Game", string platform = "SNES",
        string condition = "good", decimal offered = 5m, decimal? accepted = null) => new()
    {
        TradeInId = tradeInId,
        GameTitle = title,
        Platform = platform,
        Condition = condition,
        OfferedValue = offered,
        AcceptedValue = accepted,
    };

    [IntegrationFact]
    public async Task GetAllAsync_FiltersByCompanyId()
    {
        IntegrationDb.UseForRepositories();
        var repo = new TradeInRepository();
        var (companyA, userA) = await SeedTenantAsync();
        var (companyB, userB) = await SeedTenantAsync();

        await repo.CreateAsync(NewTradeIn(companyA, userA));
        await repo.CreateAsync(NewTradeIn(companyA, userA));
        await repo.CreateAsync(NewTradeIn(companyB, userB));

        var result = await repo.GetAllAsync(companyA);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(t => t.CompanyId == companyA);
    }

    [IntegrationFact]
    public async Task GetAllAsync_AppliesOptionalStatusAndDateFilters()
    {
        IntegrationDb.UseForRepositories();
        var repo = new TradeInRepository();
        var (companyId, userId) = await SeedTenantAsync();

        await repo.CreateAsync(NewTradeIn(companyId, userId, status: "draft"));
        await repo.CreateAsync(NewTradeIn(companyId, userId, status: "completed"));

        var drafts = await repo.GetAllAsync(companyId, status: "draft");
        drafts.Should().ContainSingle().Which.Status.Should().Be("draft");

        var sinceYesterday = await repo.GetAllAsync(companyId, dateFrom: DateTime.UtcNow.AddDays(-1));
        sinceYesterday.Should().HaveCount(2);

        var fromTomorrow = await repo.GetAllAsync(companyId, dateFrom: DateTime.UtcNow.AddDays(1));
        fromTomorrow.Should().BeEmpty();
    }

    [IntegrationFact]
    public async Task GetByIdAsync_ExistingIdSameCompany_ReturnsTradeInWithItems()
    {
        IntegrationDb.UseForRepositories();
        var repo = new TradeInRepository();
        var (companyId, userId) = await SeedTenantAsync();

        var created = await repo.CreateAsync(NewTradeIn(companyId, userId));
        await repo.AddItemAsync(NewItem(created.Id, title: "Zelda"));
        await repo.AddItemAsync(NewItem(created.Id, title: "Metroid"));

        var fetched = await repo.GetByIdAsync(created.Id, companyId);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
        fetched.Items.Should().HaveCount(2);
        fetched.Items.Should().Contain(i => i.GameTitle == "Zelda");
    }

    [IntegrationFact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new TradeInRepository();
        var (companyId, _) = await SeedTenantAsync();

        (await repo.GetByIdAsync(999999, companyId)).Should().BeNull();
    }

    [IntegrationFact]
    public async Task GetByIdAsync_DifferentCompany_ReturnsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new TradeInRepository();
        var (companyA, userA) = await SeedTenantAsync();
        var (companyB, _) = await SeedTenantAsync();

        var created = await repo.CreateAsync(NewTradeIn(companyA, userA));

        (await repo.GetByIdAsync(created.Id, companyB)).Should().BeNull();
    }

    [IntegrationFact]
    public async Task CreateAsync_InsertsRowAndReturnsGeneratedId()
    {
        IntegrationDb.UseForRepositories();
        var repo = new TradeInRepository();
        var (companyId, userId) = await SeedTenantAsync();

        var created = await repo.CreateAsync(NewTradeIn(companyId, userId, offered: 42m, notes: "hello"));

        created.Id.Should().BeGreaterThan(0);
        created.CompanyId.Should().Be(companyId);
        created.TotalOfferedValue.Should().Be(42m);
        created.Status.Should().Be("draft");
        created.Notes.Should().Be("hello");
        created.Items.Should().BeEmpty();
    }

    [IntegrationFact]
    public async Task AddItemAsync_InsertsChildRowAndReturnsGeneratedId()
    {
        IntegrationDb.UseForRepositories();
        var repo = new TradeInRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var created = await repo.CreateAsync(NewTradeIn(companyId, userId));

        var item = await repo.AddItemAsync(NewItem(created.Id, title: "Halo", platform: "Xbox",
            condition: "excellent", offered: 20m, accepted: 18m));

        item.Id.Should().BeGreaterThan(0);
        item.TradeInId.Should().Be(created.Id);
        item.GameTitle.Should().Be("Halo");
        item.AcceptedValue.Should().Be(18m);
    }

    [IntegrationFact]
    public async Task UpdateItemAsync_ExistingItem_UpdatesFieldsAndReturnsRow()
    {
        IntegrationDb.UseForRepositories();
        var repo = new TradeInRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var created = await repo.CreateAsync(NewTradeIn(companyId, userId));
        var item = await repo.AddItemAsync(NewItem(created.Id));

        item.GameTitle = "Updated Title";
        item.Condition = "poor";
        item.AcceptedValue = 7m;

        var updated = await repo.UpdateItemAsync(item);

        updated.Should().NotBeNull();
        updated!.GameTitle.Should().Be("Updated Title");
        updated.Condition.Should().Be("poor");
        updated.AcceptedValue.Should().Be(7m);
    }

    [IntegrationFact]
    public async Task UpdateItemAsync_MissingItem_ReturnsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new TradeInRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var created = await repo.CreateAsync(NewTradeIn(companyId, userId));

        var ghost = NewItem(created.Id);
        ghost.Id = 999999;

        (await repo.UpdateItemAsync(ghost)).Should().BeNull();
    }

    [IntegrationFact]
    public async Task CompleteAsync_DraftTradeIn_TransitionsToCompletedAndStampsCompletedAt()
    {
        IntegrationDb.UseForRepositories();
        var repo = new TradeInRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var created = await repo.CreateAsync(NewTradeIn(companyId, userId));
        await repo.AddItemAsync(NewItem(created.Id, accepted: 5m));
        await repo.AddItemAsync(NewItem(created.Id, accepted: 3m));

        var completedAt = DateTime.UtcNow;
        var result = await repo.CompleteAsync(created.Id, companyId, "store_credit", completedAt);

        result.Should().NotBeNull();
        result!.Status.Should().Be("completed");
        result.CompletedAt.Should().NotBeNull();
        result.PaymentType.Should().Be("store_credit");
        result.TotalAcceptedValue.Should().Be(8m);
    }

    [IntegrationFact]
    public async Task CompleteAsync_NonDraftTradeIn_ReturnsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new TradeInRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var created = await repo.CreateAsync(NewTradeIn(companyId, userId, status: "completed"));

        (await repo.CompleteAsync(created.Id, companyId, "cash", DateTime.UtcNow)).Should().BeNull();
    }

    [IntegrationFact]
    public async Task CompleteAsync_NotFound_ReturnsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new TradeInRepository();
        var (companyId, _) = await SeedTenantAsync();

        (await repo.CompleteAsync(999999, companyId, "cash", DateTime.UtcNow)).Should().BeNull();
    }

    [IntegrationFact]
    public async Task UpdateAsync_ExistingTradeIn_UpdatesNotesAndStatusFields()
    {
        IntegrationDb.UseForRepositories();
        var repo = new TradeInRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var created = await repo.CreateAsync(NewTradeIn(companyId, userId, notes: "original"));

        created.Notes = "updated notes";
        created.Status = "rejected";
        created.TotalOfferedValue = 99m;

        var updated = await repo.UpdateAsync(created);

        updated.Should().NotBeNull();
        updated!.Notes.Should().Be("updated notes");
        updated.Status.Should().Be("rejected");
        updated.TotalOfferedValue.Should().Be(99m);
    }

    [IntegrationFact]
    public async Task UpdateAsync_NotFound_ReturnsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new TradeInRepository();
        var (companyId, userId) = await SeedTenantAsync();

        var ghost = NewTradeIn(companyId, userId);
        ghost.Id = 999999;

        (await repo.UpdateAsync(ghost)).Should().BeNull();
    }
}
