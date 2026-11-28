using FluentAssertions;
using MyStore.Models;
using MyStore.Repositories;
using Xunit;

namespace MyStore.Tests.Repositories;

/// <summary>
/// Tests for <see cref="SalesRepository"/>.
///
/// The repository instantiates <see cref="Npgsql.NpgsqlConnection"/> directly from a connection
/// string read out of the environment, so the data-access methods are covered by real integration
/// tests (the <c>[IntegrationFact]</c> ones below) that run against an ephemeral Postgres seeded
/// from the <c>retrostoremanager/dbproj-mystore</c> migrations -- same pattern as
/// <see cref="CompanyRepositoryTests"/>. Those run only in the integration-tests workflow, which
/// exports <c>RUN_DB_INTEGRATION_TESTS</c> and <c>MYSTORE_TEST_DB</c>; in ordinary unit runs they
/// are skipped.
///
/// These specifically guard against a regression where CreateAsync's INSERTs referenced
/// sale.discount_total, sale_item.discount_amount/promotion_id/promotion_name, and a
/// sale_promotion table that never existed in the schema -- checkout always 500'd. A mocked
/// unit test can't catch a missing column/table; only a real round trip against the actual
/// schema can.
/// </summary>
[Collection("ConnectionStringEnv")]
public class SalesRepositoryTests
{
    [IntegrationFact]
    public async Task CreateAsync_WithAppliedPromotion_PersistsSaleItemsAndPromotionBreakdown()
    {
        IntegrationDb.UseForRepositories();
        await using var conn = await IntegrationDb.OpenAsync();
        var companyId = await IntegrationDb.SeedCompanyAsync(conn);
        var customerId = await IntegrationDb.SeedCustomerAsync(conn, companyId);
        var userId = await IntegrationDb.SeedUserAsync(conn, companyId);
        var locationId = await IntegrationDb.SeedLocationAsync(conn, companyId);
        var inventoryId = await IntegrationDb.SeedInventoryItemAsync(conn, companyId, locationId);
        var promotionId = await IntegrationDb.SeedPromotionAsync(conn, companyId, userId);

        var repo = new SalesRepository();
        var sale = new Sale
        {
            CompanyId = companyId,
            CustomerId = customerId,
            UserId = userId,
            Subtotal = 19.99m,
            Tax = 1.65m,
            TaxAmount = 1.65m,
            Total = 19.80m,
            DiscountTotal = 2.00m,
            PaymentMethod = "Cash",
            Notes = "integration test sale",
            Items =
            {
                new SaleItem
                {
                    InventoryItemId = inventoryId,
                    Quantity = 1,
                    UnitPrice = 19.99m,
                    TotalPrice = 19.99m,
                    DiscountAmount = 2.00m,
                    PromotionId = promotionId,
                    PromotionName = "10% Off",
                },
            },
            AppliedPromotions =
            {
                new AppliedPromotion { PromotionId = promotionId, PromotionName = "10% Off", DiscountAmount = 2.00m },
            },
        };

        var created = await repo.CreateAsync(sale);

        created.Id.Should().BeGreaterThan(0);

        var fetched = await repo.GetByIdAsync(created.Id, companyId);

        fetched.Should().NotBeNull();
        fetched!.DiscountTotal.Should().Be(2.00m);
        fetched.Items.Should().ContainSingle();
        fetched.Items[0].DiscountAmount.Should().Be(2.00m);
        fetched.Items[0].PromotionId.Should().Be(promotionId);
        fetched.AppliedPromotions.Should().ContainSingle(p => p.PromotionId == promotionId && p.DiscountAmount == 2.00m);
    }

    [IntegrationFact]
    public async Task CreateAsync_NoPromotions_RoundTripsZeroDiscountTotal()
    {
        IntegrationDb.UseForRepositories();
        await using var conn = await IntegrationDb.OpenAsync();
        var companyId = await IntegrationDb.SeedCompanyAsync(conn);
        var customerId = await IntegrationDb.SeedCustomerAsync(conn, companyId);
        var locationId = await IntegrationDb.SeedLocationAsync(conn, companyId);
        var inventoryId = await IntegrationDb.SeedInventoryItemAsync(conn, companyId, locationId);

        var repo = new SalesRepository();
        var sale = new Sale
        {
            CompanyId = companyId,
            CustomerId = customerId,
            Subtotal = 19.99m,
            Tax = 0m,
            Total = 19.99m,
            PaymentMethod = "Cash",
            Items = { new SaleItem { InventoryItemId = inventoryId, Quantity = 1, UnitPrice = 19.99m, TotalPrice = 19.99m } },
        };

        var created = await repo.CreateAsync(sale);
        var fetched = await repo.GetByIdAsync(created.Id, companyId);

        fetched.Should().NotBeNull();
        fetched!.DiscountTotal.Should().Be(0m);
        fetched.AppliedPromotions.Should().BeEmpty();
    }

    [IntegrationFact]
    public async Task DeleteAsync_ExistingSale_RemovesSaleAndItems()
    {
        IntegrationDb.UseForRepositories();
        await using var conn = await IntegrationDb.OpenAsync();
        var companyId = await IntegrationDb.SeedCompanyAsync(conn);
        var customerId = await IntegrationDb.SeedCustomerAsync(conn, companyId);
        var locationId = await IntegrationDb.SeedLocationAsync(conn, companyId);
        var inventoryId = await IntegrationDb.SeedInventoryItemAsync(conn, companyId, locationId);

        var repo = new SalesRepository();
        var created = await repo.CreateAsync(new Sale
        {
            CompanyId = companyId,
            CustomerId = customerId,
            Subtotal = 19.99m,
            Total = 19.99m,
            PaymentMethod = "Cash",
            Items = { new SaleItem { InventoryItemId = inventoryId, Quantity = 1, UnitPrice = 19.99m, TotalPrice = 19.99m } },
        });

        (await repo.DeleteAsync(created.Id, companyId)).Should().BeTrue();
        (await repo.GetByIdAsync(created.Id, companyId)).Should().BeNull();
    }
}
