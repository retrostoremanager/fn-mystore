using FluentAssertions;
using MyStore.Models;
using MyStore.Repositories;
using Xunit;

namespace MyStore.Tests.Repositories;

/// <summary>
/// Tests for <see cref="InventoryRepository"/>.
///
/// The repository instantiates <see cref="Npgsql.NpgsqlConnection"/> directly from a connection
/// string read out of the environment, so the data-access methods are covered by real integration
/// tests (the <c>[IntegrationFact]</c> ones below) that run against an ephemeral Postgres seeded
/// from the <c>retrostoremanager/dbproj-mystore</c> migrations -- same pattern as
/// <see cref="CompanyRepositoryTests"/>. Those run only in the integration-tests workflow, which
/// exports <c>RUN_DB_INTEGRATION_TESTS</c> and <c>MYSTORE_TEST_DB</c>; in ordinary unit runs they
/// are skipped.
///
/// These specifically guard against a regression where the hand-written SQL referenced columns
/// (price, cost, created_date) that don't exist on game_inventory -- the real columns are
/// sell_price, buy_price, added_date. A mocked unit test can't catch a wrong column name; only a
/// real round trip against the actual schema can.
/// </summary>
[Collection("ConnectionStringEnv")]
public class InventoryRepositoryTests
{
    private static InventoryItem NewItem(int companyId, int locationId, string? gameId = null) => new()
    {
        CompanyId = companyId,
        LocationId = locationId,
        Quantity = 3,
        SellPrice = 59.99m,
        BuyPrice = 25.00m,
        Condition = "Good",
        Game = gameId == null ? null : new Game { Id = gameId },
        Notes = "integration test item",
    };

    [IntegrationFact]
    public async Task CreateAsync_ThenGetByIdAsync_RoundTripsSellAndBuyPrice()
    {
        IntegrationDb.UseForRepositories();
        await using var conn = await IntegrationDb.OpenAsync();
        var companyId = await IntegrationDb.SeedCompanyAsync(conn);
        var locationId = await IntegrationDb.SeedLocationAsync(conn, companyId);
        var gameId = await IntegrationDb.SeedGameAsync(conn);

        var repo = new InventoryRepository();
        var created = await repo.CreateAsync(NewItem(companyId, locationId, gameId));

        created.Id.Should().BeGreaterThan(0);

        var fetched = await repo.GetByIdAsync(created.Id, companyId);

        fetched.Should().NotBeNull();
        fetched!.SellPrice.Should().Be(59.99m);
        fetched.BuyPrice.Should().Be(25.00m);
        fetched.Quantity.Should().Be(3);
        fetched.Condition.Should().Be("Good");
        fetched.LocationId.Should().Be(locationId);
    }

    [IntegrationFact]
    public async Task GetAllAsync_ReturnsCreatedItemWithPrices()
    {
        IntegrationDb.UseForRepositories();
        await using var conn = await IntegrationDb.OpenAsync();
        var companyId = await IntegrationDb.SeedCompanyAsync(conn);
        var locationId = await IntegrationDb.SeedLocationAsync(conn, companyId);

        var repo = new InventoryRepository();
        var created = await repo.CreateAsync(NewItem(companyId, locationId));

        var all = await repo.GetAllAsync(companyId);

        var match = all.Should().ContainSingle(i => i.Id == created.Id).Subject;
        match.SellPrice.Should().Be(59.99m);
        match.BuyPrice.Should().Be(25.00m);
    }

    [IntegrationFact]
    public async Task UpdateAsync_ChangesSellAndBuyPrice()
    {
        IntegrationDb.UseForRepositories();
        await using var conn = await IntegrationDb.OpenAsync();
        var companyId = await IntegrationDb.SeedCompanyAsync(conn);
        var locationId = await IntegrationDb.SeedLocationAsync(conn, companyId);

        var repo = new InventoryRepository();
        var created = await repo.CreateAsync(NewItem(companyId, locationId));

        var update = NewItem(companyId, locationId);
        update.SellPrice = 74.99m;
        update.BuyPrice = 30.00m;
        var updated = await repo.UpdateAsync(created.Id, update, companyId);

        updated.Should().NotBeNull();
        updated!.SellPrice.Should().Be(74.99m);
        updated.BuyPrice.Should().Be(30.00m);
    }

    [IntegrationFact]
    public async Task SearchAsync_ByGameTitle_ReturnsItemWithPrices()
    {
        IntegrationDb.UseForRepositories();
        await using var conn = await IntegrationDb.OpenAsync();
        var companyId = await IntegrationDb.SeedCompanyAsync(conn);
        var locationId = await IntegrationDb.SeedLocationAsync(conn, companyId);
        var gameId = await IntegrationDb.SeedGameAsync(conn);

        var repo = new InventoryRepository();
        var created = await repo.CreateAsync(NewItem(companyId, locationId, gameId));

        var results = await repo.SearchAsync("Test Game", companyId);

        var match = results.Should().ContainSingle(i => i.Id == created.Id).Subject;
        match.SellPrice.Should().Be(59.99m);
        match.BuyPrice.Should().Be(25.00m);
    }

    [IntegrationFact]
    public async Task DeleteAsync_ExistingItem_RemovesIt()
    {
        IntegrationDb.UseForRepositories();
        await using var conn = await IntegrationDb.OpenAsync();
        var companyId = await IntegrationDb.SeedCompanyAsync(conn);
        var locationId = await IntegrationDb.SeedLocationAsync(conn, companyId);

        var repo = new InventoryRepository();
        var created = await repo.CreateAsync(NewItem(companyId, locationId));

        (await repo.DeleteAsync(created.Id, companyId)).Should().BeTrue();
        (await repo.GetByIdAsync(created.Id, companyId)).Should().BeNull();
    }
}
