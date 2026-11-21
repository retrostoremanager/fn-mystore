using FluentAssertions;
using MyStore.Models;
using MyStore.Repositories;
using Xunit;

namespace MyStore.Tests.Repositories;

/// <summary>
/// Tests for <see cref="LoyaltyRepository"/>.
///
/// The repository instantiates <see cref="Npgsql.NpgsqlConnection"/> directly via
/// <c>TenantConnection.OpenAsync</c> using a connection string read out of the environment, so
/// the data-access methods are covered by real integration tests (the <c>[IntegrationFact]</c>
/// ones below) that run against an ephemeral Postgres seeded from the
/// <c>retrostoremanager/dbproj-mystore</c> migrations -- same pattern as
/// <see cref="TradeInRepositoryTests"/>. Those run only in the integration-tests workflow, which
/// exports <c>RUN_DB_INTEGRATION_TESTS</c> and <c>MYSTORE_TEST_DB</c>; in ordinary unit runs they
/// are skipped.
///
/// The plain <c>[Fact]</c> tests cover the connection-string contract without a database.
/// </summary>
[Collection("ConnectionStringEnv")]
public class LoyaltyRepositoryTests
{
    public LoyaltyRepositoryTests()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        Environment.SetEnvironmentVariable("PostgresConnectionString", null);
    }

    [Fact]
    public void Constructor_NoConnectionStringEnvVar_Throws()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        Environment.SetEnvironmentVariable("PostgresConnectionString", null);

        Action act = () => _ = new LoyaltyRepository();

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
            Action act = () => _ = new LoyaltyRepository();
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
            Action act = () => _ = new LoyaltyRepository();
            act.Should().NotThrow();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PostgresConnectionString", null);
        }
    }

    [Fact]
    public void Repository_ImplementsILoyaltyRepository()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            "Host=localhost;Database=test;Username=test;Password=test");

        try
        {
            var repo = new LoyaltyRepository();
            repo.Should().BeAssignableTo<ILoyaltyRepository>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        }
    }

    // ---------------------------------------------------------------------
    // Integration tests covering the acceptance criteria for issue #344.
    // Run against an ephemeral Postgres in the integration-tests workflow.
    // ---------------------------------------------------------------------

    private static async Task<(int companyId, int customerId)> SeedTenantAsync()
    {
        await using var conn = await IntegrationDb.OpenAsync();
        var companyId = await IntegrationDb.SeedCompanyAsync(conn);
        var customerId = await IntegrationDb.SeedCustomerAsync(conn, companyId);
        return (companyId, customerId);
    }

    private static LoyaltyTransaction NewTransaction(
        int companyId, int customerId, int points, string type = "earn_sale",
        int? referenceId = null, string? notes = null) => new()
    {
        CompanyId = companyId,
        CustomerId = customerId,
        Points = points,
        TransactionType = type,
        ReferenceId = referenceId,
        Notes = notes,
    };

    [IntegrationFact]
    public async Task GetBalanceAsync_SumsPointsForCompanyAndCustomer()
    {
        IntegrationDb.UseForRepositories();
        var repo = new LoyaltyRepository();
        var (companyId, customerId) = await SeedTenantAsync();
        var (_, otherCustomerId) = await SeedTenantAsync();

        await repo.AddTransactionAsync(NewTransaction(companyId, customerId, 100, "earn_sale"));
        await repo.AddTransactionAsync(NewTransaction(companyId, customerId, 50, "earn_tradein"));
        await repo.AddTransactionAsync(NewTransaction(companyId, customerId, -30, "redeem"));
        await repo.AddTransactionAsync(NewTransaction(companyId, otherCustomerId, 999, "earn_sale"));

        var balance = await repo.GetBalanceAsync(companyId, customerId);

        balance.Should().Be(120); // 100 + 50 - 30
    }

    [IntegrationFact]
    public async Task GetBalanceAsync_NoTransactions_ReturnsZero()
    {
        IntegrationDb.UseForRepositories();
        var repo = new LoyaltyRepository();
        var (companyId, customerId) = await SeedTenantAsync();

        (await repo.GetBalanceAsync(companyId, customerId)).Should().Be(0);
    }

    [IntegrationFact]
    public async Task GetBalanceAsync_ScopedByCompanyId_DoesNotLeakAcrossTenants()
    {
        IntegrationDb.UseForRepositories();
        var repo = new LoyaltyRepository();
        var (companyA, customerA) = await SeedTenantAsync();
        var (companyB, customerB) = await SeedTenantAsync();

        await repo.AddTransactionAsync(NewTransaction(companyA, customerA, 100));
        await repo.AddTransactionAsync(NewTransaction(companyB, customerB, 500));

        (await repo.GetBalanceAsync(companyA, customerA)).Should().Be(100);
    }

    [IntegrationFact]
    public async Task GetSettingsAsync_ExistingRow_ReturnsLoyaltySettings()
    {
        IntegrationDb.UseForRepositories();
        var repo = new LoyaltyRepository();
        var (companyId, _) = await SeedTenantAsync();
        await repo.UpsertSettingsAsync(new LoyaltySettings
        {
            CompanyId = companyId,
            PointsPerDollarSpent = 2.5m,
            PointsPerDollarTradeIn = 3m,
            RedemptionRate = 200m,
            IsEnabled = true,
        });

        var settings = await repo.GetSettingsAsync(companyId);

        settings.Should().NotBeNull();
        settings!.PointsPerDollarSpent.Should().Be(2.5m);
        settings.PointsPerDollarTradeIn.Should().Be(3m);
        settings.RedemptionRate.Should().Be(200m);
        settings.IsEnabled.Should().BeTrue();
    }

    [IntegrationFact]
    public async Task GetSettingsAsync_NotConfigured_ReturnsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new LoyaltyRepository();
        var (companyId, _) = await SeedTenantAsync();

        (await repo.GetSettingsAsync(companyId)).Should().BeNull();
    }

    [IntegrationFact]
    public async Task GetSettingsAsync_ScopedByCompanyId()
    {
        IntegrationDb.UseForRepositories();
        var repo = new LoyaltyRepository();
        var (companyA, _) = await SeedTenantAsync();
        var (companyB, _) = await SeedTenantAsync();
        await repo.UpsertSettingsAsync(new LoyaltySettings { CompanyId = companyA, IsEnabled = true });

        (await repo.GetSettingsAsync(companyB)).Should().BeNull();
    }

    [IntegrationFact]
    public async Task UpsertSettingsAsync_FirstCall_InsertsNewRow()
    {
        IntegrationDb.UseForRepositories();
        var repo = new LoyaltyRepository();
        var (companyId, _) = await SeedTenantAsync();

        var created = await repo.UpsertSettingsAsync(new LoyaltySettings
        {
            CompanyId = companyId,
            PointsPerDollarSpent = 1.5m,
            PointsPerDollarTradeIn = 2m,
            RedemptionRate = 150m,
            IsEnabled = true,
        });

        created.Id.Should().BeGreaterThan(0);
        created.PointsPerDollarSpent.Should().Be(1.5m);
        created.PointsPerDollarTradeIn.Should().Be(2m);
        created.RedemptionRate.Should().Be(150m);
        created.IsEnabled.Should().BeTrue();
    }

    [IntegrationFact]
    public async Task UpsertSettingsAsync_ExistingRow_UpdatesInPlaceOnCompanyIdConflict()
    {
        IntegrationDb.UseForRepositories();
        var repo = new LoyaltyRepository();
        var (companyId, _) = await SeedTenantAsync();
        var original = await repo.UpsertSettingsAsync(new LoyaltySettings
        {
            CompanyId = companyId,
            PointsPerDollarSpent = 1m,
            IsEnabled = false,
        });

        var updated = await repo.UpsertSettingsAsync(new LoyaltySettings
        {
            CompanyId = companyId,
            PointsPerDollarSpent = 5m,
            PointsPerDollarTradeIn = 5m,
            RedemptionRate = 500m,
            IsEnabled = true,
        });

        updated.Id.Should().Be(original.Id);
        updated.PointsPerDollarSpent.Should().Be(5m);
        updated.IsEnabled.Should().BeTrue();
    }

    [IntegrationFact]
    public async Task UpsertSettingsAsync_DoesNotAffectOtherCompanies()
    {
        IntegrationDb.UseForRepositories();
        var repo = new LoyaltyRepository();
        var (companyA, _) = await SeedTenantAsync();
        var (companyB, _) = await SeedTenantAsync();
        await repo.UpsertSettingsAsync(new LoyaltySettings { CompanyId = companyA, PointsPerDollarSpent = 1m });
        await repo.UpsertSettingsAsync(new LoyaltySettings { CompanyId = companyB, PointsPerDollarSpent = 2m });

        await repo.UpsertSettingsAsync(new LoyaltySettings { CompanyId = companyA, PointsPerDollarSpent = 9m });

        (await repo.GetSettingsAsync(companyB))!.PointsPerDollarSpent.Should().Be(2m);
    }

    [IntegrationFact]
    public async Task AddTransactionAsync_EarnSale_InsertsPositivePointsRow()
    {
        IntegrationDb.UseForRepositories();
        var repo = new LoyaltyRepository();
        var (companyId, customerId) = await SeedTenantAsync();

        var created = await repo.AddTransactionAsync(
            NewTransaction(companyId, customerId, 25, "earn_sale", referenceId: 123, notes: "Sale #123"));

        created.Id.Should().BeGreaterThan(0);
        created.CreatedAt.Should().NotBe(default);
        created.TransactionType.Should().Be("earn_sale");
        created.Points.Should().Be(25);
        created.ReferenceId.Should().Be(123);
        created.Notes.Should().Be("Sale #123");
    }

    [IntegrationFact]
    public async Task AddTransactionAsync_EarnTradeIn_InsertsPositivePointsRow()
    {
        IntegrationDb.UseForRepositories();
        var repo = new LoyaltyRepository();
        var (companyId, customerId) = await SeedTenantAsync();

        var created = await repo.AddTransactionAsync(
            NewTransaction(companyId, customerId, 15, "earn_tradein", referenceId: 456));

        created.TransactionType.Should().Be("earn_tradein");
        created.Points.Should().Be(15);
        created.ReferenceId.Should().Be(456);
    }

    [IntegrationFact]
    public async Task AddTransactionAsync_Redeem_InsertsNegativePointsRow()
    {
        IntegrationDb.UseForRepositories();
        var repo = new LoyaltyRepository();
        var (companyId, customerId) = await SeedTenantAsync();
        await repo.AddTransactionAsync(NewTransaction(companyId, customerId, 100, "earn_sale"));

        var created = await repo.AddTransactionAsync(NewTransaction(companyId, customerId, -40, "redeem"));

        created.TransactionType.Should().Be("redeem");
        created.Points.Should().Be(-40);
        (await repo.GetBalanceAsync(companyId, customerId)).Should().Be(60);
    }

    [IntegrationFact]
    public async Task AddTransactionAsync_NullReferenceAndNotes_PersistedAsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new LoyaltyRepository();
        var (companyId, customerId) = await SeedTenantAsync();

        var created = await repo.AddTransactionAsync(
            NewTransaction(companyId, customerId, 10, "earn_sale", referenceId: null, notes: null));

        created.Id.Should().BeGreaterThan(0);
        created.ReferenceId.Should().BeNull();
        created.Notes.Should().BeNull();
    }

    [IntegrationFact]
    public async Task GetTransactionsAsync_FiltersByCompanyAndCustomer()
    {
        IntegrationDb.UseForRepositories();
        var repo = new LoyaltyRepository();
        var (companyA, customerX) = await SeedTenantAsync();
        int customerY;
        await using (var conn = await IntegrationDb.OpenAsync())
        {
            customerY = await IntegrationDb.SeedCustomerAsync(conn, companyA);
        }
        var (companyB, _) = await SeedTenantAsync();

        await repo.AddTransactionAsync(NewTransaction(companyA, customerX, 10));
        await repo.AddTransactionAsync(NewTransaction(companyA, customerY, 20));
        await repo.AddTransactionAsync(NewTransaction(companyB, customerX, 30));

        var result = await repo.GetTransactionsAsync(companyA, customerX);

        result.Should().ContainSingle().Which.Points.Should().Be(10);
    }

    [IntegrationFact]
    public async Task GetTransactionsAsync_OrdersByCreatedAtDescending()
    {
        IntegrationDb.UseForRepositories();
        var repo = new LoyaltyRepository();
        var (companyId, customerId) = await SeedTenantAsync();

        var first = await repo.AddTransactionAsync(NewTransaction(companyId, customerId, 1));
        var second = await repo.AddTransactionAsync(NewTransaction(companyId, customerId, 2));
        var third = await repo.AddTransactionAsync(NewTransaction(companyId, customerId, 3));

        var result = await repo.GetTransactionsAsync(companyId, customerId);

        result.Select(t => t.Id).Should().Equal(third.Id, second.Id, first.Id);
    }

    [IntegrationFact]
    public async Task GetTransactionsAsync_NoMatches_ReturnsEmptyList()
    {
        IntegrationDb.UseForRepositories();
        var repo = new LoyaltyRepository();
        var (companyId, customerId) = await SeedTenantAsync();

        var result = await repo.GetTransactionsAsync(companyId, customerId);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }
}
