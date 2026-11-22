using FluentAssertions;
using MyStore.Models;
using MyStore.Repositories;
using Xunit;

namespace MyStore.Tests.Repositories;

/// <summary>
/// Tests for <see cref="PromotionRepository"/>.
///
/// The repository instantiates <see cref="Npgsql.NpgsqlConnection"/> directly via
/// <c>TenantConnection.OpenAsync</c> using a connection string read out of the environment, so the
/// data-access methods are covered by real integration tests (the <c>[IntegrationFact]</c> ones
/// below) that run against an ephemeral Postgres seeded from the
/// <c>retrostoremanager/dbproj-mystore</c> migrations -- same pattern as
/// <see cref="TradeInRepositoryTests"/>. Those run only in the integration-tests workflow, which
/// exports <c>RUN_DB_INTEGRATION_TESTS</c> and <c>MYSTORE_TEST_DB</c>; in ordinary unit runs they
/// are skipped.
///
/// The plain <c>[Fact]</c> tests cover the connection-string contract without a database.
/// </summary>
[Collection("ConnectionStringEnv")]
public class PromotionRepositoryTests
{
    public PromotionRepositoryTests()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        Environment.SetEnvironmentVariable("PostgresConnectionString", null);
    }

    [Fact]
    public void Constructor_NoConnectionStringEnvVar_Throws()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        Environment.SetEnvironmentVariable("PostgresConnectionString", null);

        Action act = () => _ = new PromotionRepository();

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
            Action act = () => _ = new PromotionRepository();
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
            Action act = () => _ = new PromotionRepository();
            act.Should().NotThrow();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PostgresConnectionString", null);
        }
    }

    [Fact]
    public void Repository_ImplementsIPromotionRepository()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            "Host=localhost;Database=test;Username=test;Password=test");

        try
        {
            var repo = new PromotionRepository();
            repo.Should().BeAssignableTo<IPromotionRepository>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        }
    }

    // ---------------------------------------------------------------------
    // Integration tests covering the acceptance criteria for issue #345.
    // Run against an ephemeral Postgres in the integration-tests workflow.
    // ---------------------------------------------------------------------

    private static async Task<(int companyId, int userId)> SeedTenantAsync()
    {
        await using var conn = await IntegrationDb.OpenAsync();
        var companyId = await IntegrationDb.SeedCompanyAsync(conn);
        var userId = await IntegrationDb.SeedUserAsync(conn, companyId);
        return (companyId, userId);
    }

    // Defaults to type=percentage with discount_percent set, since promotion's
    // chk_promotion_type_columns CHECK constraint (067_promotion_schema_retry.sql) requires
    // discount_percent for 'percentage' and buy_quantity+get_quantity for 'bxgy'.
    private static Promotion NewPromotion(
        int companyId, int userId, string name = "Test Promotion", string type = "percentage",
        decimal? discountPercent = 10m, int? buyQuantity = null, int? getQuantity = null,
        string scope = "store_wide", string? scopeValue = null,
        DateTime? startDate = null, DateTime? endDate = null, bool isActive = true) => new()
    {
        CompanyId = companyId,
        Name = name,
        Type = type,
        DiscountPercent = discountPercent,
        BuyQuantity = buyQuantity,
        GetQuantity = getQuantity,
        Scope = scope,
        ScopeValue = scopeValue,
        StartDate = startDate ?? DateTime.UtcNow.AddDays(-1),
        EndDate = endDate,
        IsActive = isActive,
        CreatedBy = userId,
    };

    [IntegrationFact]
    public async Task GetAllAsync_ReturnsOnlyRowsForGivenCompanyId()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyA, userA) = await SeedTenantAsync();
        var (companyB, userB) = await SeedTenantAsync();

        await repo.CreateAsync(NewPromotion(companyA, userA));
        await repo.CreateAsync(NewPromotion(companyA, userA));
        await repo.CreateAsync(NewPromotion(companyB, userB));

        var result = await repo.GetAllAsync(companyA);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(p => p.CompanyId == companyA);
    }

    [IntegrationFact]
    public async Task GetAllAsync_OrdersByCreatedAtDescending()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyId, userId) = await SeedTenantAsync();

        var first = await repo.CreateAsync(NewPromotion(companyId, userId, name: "First"));
        var second = await repo.CreateAsync(NewPromotion(companyId, userId, name: "Second"));
        var third = await repo.CreateAsync(NewPromotion(companyId, userId, name: "Third"));

        var result = await repo.GetAllAsync(companyId);

        result.Select(p => p.Id).Should().Equal(third.Id, second.Id, first.Id);
    }

    [IntegrationFact]
    public async Task GetAllAsync_NoRows_ReturnsEmptyList()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyId, _) = await SeedTenantAsync();

        var result = await repo.GetAllAsync(companyId);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [IntegrationFact]
    public async Task GetActiveAsync_IncludesPromotionsActiveOnDate()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var queryDate = DateTime.UtcNow;
        await repo.CreateAsync(NewPromotion(companyId, userId,
            startDate: queryDate.AddDays(-5), endDate: queryDate.AddDays(5), isActive: true));

        var result = await repo.GetActiveAsync(companyId, queryDate);

        result.Should().ContainSingle();
    }

    [IntegrationFact]
    public async Task GetActiveAsync_ExcludesInactivePromotions()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var queryDate = DateTime.UtcNow;
        await repo.CreateAsync(NewPromotion(companyId, userId,
            startDate: queryDate.AddDays(-5), endDate: queryDate.AddDays(5), isActive: false));

        var result = await repo.GetActiveAsync(companyId, queryDate);

        result.Should().BeEmpty();
    }

    [IntegrationFact]
    public async Task GetActiveAsync_ExcludesPromotionsNotYetStarted()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var queryDate = DateTime.UtcNow;
        await repo.CreateAsync(NewPromotion(companyId, userId,
            startDate: queryDate.AddDays(5), endDate: queryDate.AddDays(10), isActive: true));

        var result = await repo.GetActiveAsync(companyId, queryDate);

        result.Should().BeEmpty();
    }

    [IntegrationFact]
    public async Task GetActiveAsync_ExcludesPromotionsAlreadyEnded()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var queryDate = DateTime.UtcNow;
        await repo.CreateAsync(NewPromotion(companyId, userId,
            startDate: queryDate.AddDays(-10), endDate: queryDate.AddDays(-5), isActive: true));

        var result = await repo.GetActiveAsync(companyId, queryDate);

        result.Should().BeEmpty();
    }

    [IntegrationFact]
    public async Task GetActiveAsync_IncludesPromotionsWithNullEndDate()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var queryDate = DateTime.UtcNow;
        await repo.CreateAsync(NewPromotion(companyId, userId,
            startDate: queryDate.AddDays(-5), endDate: null, isActive: true));

        var result = await repo.GetActiveAsync(companyId, queryDate);

        result.Should().ContainSingle();
    }

    [IntegrationFact]
    public async Task GetActiveAsync_IncludesPromotionsStartingToday()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var queryDate = DateTime.UtcNow;
        await repo.CreateAsync(NewPromotion(companyId, userId,
            startDate: queryDate, endDate: queryDate.AddDays(5), isActive: true));

        var result = await repo.GetActiveAsync(companyId, queryDate);

        result.Should().ContainSingle();
    }

    [IntegrationFact]
    public async Task GetActiveAsync_IncludesPromotionsEndingToday()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var queryDate = DateTime.UtcNow;
        await repo.CreateAsync(NewPromotion(companyId, userId,
            startDate: queryDate.AddDays(-5), endDate: queryDate, isActive: true));

        var result = await repo.GetActiveAsync(companyId, queryDate);

        result.Should().ContainSingle();
    }

    [IntegrationFact]
    public async Task GetActiveAsync_ScopedByCompanyId_DoesNotLeakAcrossTenants()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyA, userA) = await SeedTenantAsync();
        var (companyB, userB) = await SeedTenantAsync();
        var queryDate = DateTime.UtcNow;
        await repo.CreateAsync(NewPromotion(companyA, userA,
            startDate: queryDate.AddDays(-1), endDate: queryDate.AddDays(1), isActive: true));
        await repo.CreateAsync(NewPromotion(companyB, userB,
            startDate: queryDate.AddDays(-1), endDate: queryDate.AddDays(1), isActive: true));

        var result = await repo.GetActiveAsync(companyA, queryDate);

        result.Should().ContainSingle().Which.CompanyId.Should().Be(companyA);
    }

    [IntegrationFact]
    public async Task CreateAsync_PersistsAllFields_AndReturnsRowWithId()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var startDate = DateTime.UtcNow.AddDays(-1);
        var endDate = DateTime.UtcNow.AddDays(30);

        var created = await repo.CreateAsync(NewPromotion(
            companyId, userId, name: "BOGO Deal", type: "bxgy", discountPercent: null,
            buyQuantity: 2, getQuantity: 1, scope: "category", scopeValue: "Trading Cards",
            startDate: startDate, endDate: endDate, isActive: true));

        created.Id.Should().BeGreaterThan(0);
        created.CreatedAt.Should().NotBe(default);
        created.CompanyId.Should().Be(companyId);
        created.Name.Should().Be("BOGO Deal");
        created.Type.Should().Be("bxgy");
        created.BuyQuantity.Should().Be(2);
        created.GetQuantity.Should().Be(1);
        created.Scope.Should().Be("category");
        created.ScopeValue.Should().Be("Trading Cards");
        created.IsActive.Should().BeTrue();
        created.CreatedBy.Should().Be(userId);
    }

    [IntegrationFact]
    public async Task CreateAsync_NullableFields_PersistedAsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyId, userId) = await SeedTenantAsync();

        var created = await repo.CreateAsync(NewPromotion(
            companyId, userId, type: "percentage", discountPercent: 15m,
            buyQuantity: null, getQuantity: null, scopeValue: null, endDate: null));

        created.BuyQuantity.Should().BeNull();
        created.GetQuantity.Should().BeNull();
        created.ScopeValue.Should().BeNull();
        created.EndDate.Should().BeNull();
    }

    [IntegrationFact]
    public async Task GetByIdAsync_ExistingRow_ReturnsPromotion()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var created = await repo.CreateAsync(NewPromotion(companyId, userId, name: "Lookup Me"));

        var fetched = await repo.GetByIdAsync(created.Id, companyId);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
        fetched.Name.Should().Be("Lookup Me");
    }

    [IntegrationFact]
    public async Task GetByIdAsync_WrongCompanyId_ReturnsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyA, userA) = await SeedTenantAsync();
        var (companyB, _) = await SeedTenantAsync();
        var created = await repo.CreateAsync(NewPromotion(companyA, userA));

        (await repo.GetByIdAsync(created.Id, companyB)).Should().BeNull();
    }

    [IntegrationFact]
    public async Task UpdateAsync_PersistsIsActiveToggle()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var created = await repo.CreateAsync(NewPromotion(companyId, userId, isActive: true));

        created.IsActive = false;
        var updated = await repo.UpdateAsync(created);

        updated.Should().NotBeNull();
        updated!.IsActive.Should().BeFalse();
    }

    [IntegrationFact]
    public async Task UpdateAsync_PersistsAllMutableFields()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var created = await repo.CreateAsync(NewPromotion(companyId, userId, name: "Original"));

        created.Name = "Changed";
        created.Type = "bxgy";
        created.DiscountPercent = null;
        created.BuyQuantity = 3;
        created.GetQuantity = 1;
        created.Scope = "item";
        created.ScopeValue = "42";
        created.StartDate = DateTime.UtcNow.AddDays(-2);
        created.EndDate = DateTime.UtcNow.AddDays(60);
        created.IsActive = false;

        var updated = await repo.UpdateAsync(created);

        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Changed");
        updated.Type.Should().Be("bxgy");
        updated.BuyQuantity.Should().Be(3);
        updated.GetQuantity.Should().Be(1);
        updated.Scope.Should().Be("item");
        updated.ScopeValue.Should().Be("42");
        updated.IsActive.Should().BeFalse();
    }

    [IntegrationFact]
    public async Task UpdateAsync_NonExistentId_ReturnsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var ghost = NewPromotion(companyId, userId);
        ghost.Id = 999999;

        (await repo.UpdateAsync(ghost)).Should().BeNull();
    }

    [IntegrationFact]
    public async Task UpdateAsync_WrongCompanyId_DoesNotMutateRow_ReturnsNull()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyA, userA) = await SeedTenantAsync();
        var (companyB, _) = await SeedTenantAsync();
        var created = await repo.CreateAsync(NewPromotion(companyA, userA, name: "Untouched"));

        var spoofed = NewPromotion(companyB, userA, name: "Hacked");
        spoofed.Id = created.Id;

        (await repo.UpdateAsync(spoofed)).Should().BeNull();

        var stillOriginal = await repo.GetByIdAsync(created.Id, companyA);
        stillOriginal!.Name.Should().Be("Untouched");
    }

    [IntegrationFact]
    public async Task DeleteAsync_ExistingRow_RemovesRowAndReturnsTrue()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyId, userId) = await SeedTenantAsync();
        var created = await repo.CreateAsync(NewPromotion(companyId, userId));

        var deleted = await repo.DeleteAsync(created.Id, companyId);

        deleted.Should().BeTrue();
        (await repo.GetByIdAsync(created.Id, companyId)).Should().BeNull();
    }

    [IntegrationFact]
    public async Task DeleteAsync_NonExistentRow_ReturnsFalse()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyId, _) = await SeedTenantAsync();

        (await repo.DeleteAsync(999999, companyId)).Should().BeFalse();
    }

    [IntegrationFact]
    public async Task DeleteAsync_WrongCompanyId_DoesNotRemoveRow_ReturnsFalse()
    {
        IntegrationDb.UseForRepositories();
        var repo = new PromotionRepository();
        var (companyA, userA) = await SeedTenantAsync();
        var (companyB, _) = await SeedTenantAsync();
        var created = await repo.CreateAsync(NewPromotion(companyA, userA));

        var deleted = await repo.DeleteAsync(created.Id, companyB);

        deleted.Should().BeFalse();
        (await repo.GetByIdAsync(created.Id, companyA)).Should().NotBeNull();
    }
}
