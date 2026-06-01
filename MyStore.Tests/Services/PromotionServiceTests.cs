using FluentAssertions;
using Moq;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Services;
using Xunit;

namespace MyStore.Tests.Services;

public class PromotionServiceTests
{
    private readonly Mock<IPromotionRepository> _repositoryMock;
    private readonly PromotionService _service;

    public PromotionServiceTests()
    {
        _repositoryMock = new Mock<IPromotionRepository>();
        _service = new PromotionService(_repositoryMock.Object);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsPromotions()
    {
        var promotions = new List<Promotion>
        {
            new Promotion { Id = 1, CompanyId = 10, Name = "Spring Sale", Type = "percentage", Scope = "store_wide", DiscountPercent = 10m, StartDate = DateTime.UtcNow.AddDays(-1), IsActive = true }
        };
        _repositoryMock.Setup(r => r.GetAllAsync(10)).ReturnsAsync(promotions);

        var result = await _service.GetAllAsync(10);

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data![0].Name.Should().Be("Spring Sale");
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsError()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(99, 10)).ReturnsAsync((Promotion?)null);

        var result = await _service.GetByIdAsync(99, 10);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("99");
    }

    [Fact]
    public async Task GetByIdAsync_Found_ReturnsPromotion()
    {
        var promotion = new Promotion { Id = 1, CompanyId = 10, Name = "Test", Type = "percentage", Scope = "store_wide", DiscountPercent = 5m, StartDate = DateTime.UtcNow.AddDays(-1), IsActive = true };
        _repositoryMock.Setup(r => r.GetByIdAsync(1, 10)).ReturnsAsync(promotion);

        var result = await _service.GetByIdAsync(1, 10);

        result.Success.Should().BeTrue();
        result.Data!.Name.Should().Be("Test");
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_ReturnsCreatedPromotion()
    {
        var request = new CreatePromotionRequest
        {
            Name = "Summer Deal",
            Type = "percentage",
            DiscountPercent = 15m,
            Scope = "store_wide",
            StartDate = DateTime.UtcNow,
            IsActive = true,
            CreatedBy = 1,
        };
        var created = new Promotion { Id = 5, CompanyId = 10, Name = "Summer Deal", Type = "percentage", Scope = "store_wide", DiscountPercent = 15m, StartDate = request.StartDate, IsActive = true, CreatedBy = 1 };
        _repositoryMock.Setup(r => r.CreateAsync(It.IsAny<Promotion>())).ReturnsAsync(created);

        var result = await _service.CreateAsync(request, 10);

        result.Success.Should().BeTrue();
        result.Data!.Id.Should().Be(5);
        result.Data.DiscountPercent.Should().Be(15m);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ReturnsError()
    {
        _repositoryMock.Setup(r => r.DeleteAsync(99, 10)).ReturnsAsync(false);

        var result = await _service.DeleteAsync(99, 10);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("99");
    }

    [Fact]
    public async Task DeleteAsync_Found_ReturnsSuccess()
    {
        _repositoryMock.Setup(r => r.DeleteAsync(1, 10)).ReturnsAsync(true);

        var result = await _service.DeleteAsync(1, 10);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetActivePromotionsAsync_ReturnsOnlyActiveInRange()
    {
        var active = new List<Promotion>
        {
            new Promotion { Id = 1, CompanyId = 10, Name = "Active", Type = "percentage", Scope = "store_wide", DiscountPercent = 10m, StartDate = DateTime.UtcNow.AddDays(-1), IsActive = true }
        };
        _repositoryMock.Setup(r => r.GetActiveAsync(10, It.IsAny<DateTime>())).ReturnsAsync(active);

        var result = await _service.GetActivePromotionsAsync(10);

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
    }

    [Fact]
    public async Task ApplyPromotionsAsync_PercentageStoreWide_AppliesDiscountToAllItems()
    {
        var promotions = new List<Promotion>
        {
            new Promotion { Id = 1, CompanyId = 10, Name = "10% Off Everything", Type = "percentage", DiscountPercent = 10m, Scope = "store_wide", StartDate = DateTime.UtcNow.AddDays(-1), IsActive = true }
        };
        _repositoryMock.Setup(r => r.GetActiveAsync(10, It.IsAny<DateTime>())).ReturnsAsync(promotions);

        var cartItems = new List<CartItem>
        {
            new CartItem { InventoryItemId = 1, Quantity = 2, UnitPrice = 50m, Category = "Games" },
            new CartItem { InventoryItemId = 2, Quantity = 1, UnitPrice = 20m, Category = "Accessories" },
        };

        var discounts = (await _service.ApplyPromotionsAsync(cartItems, 10)).ToList();

        discounts.Should().HaveCount(2);
        discounts.Single(d => d.ItemId == 1).DiscountAmount.Should().Be(10m);
        discounts.Single(d => d.ItemId == 2).DiscountAmount.Should().Be(2m);
    }

    [Fact]
    public async Task ApplyPromotionsAsync_PercentageCategory_AppliesOnlyToMatchingCategory()
    {
        var promotions = new List<Promotion>
        {
            new Promotion { Id = 1, CompanyId = 10, Name = "Games 20% Off", Type = "percentage", DiscountPercent = 20m, Scope = "category", ScopeValue = "Games", StartDate = DateTime.UtcNow.AddDays(-1), IsActive = true }
        };
        _repositoryMock.Setup(r => r.GetActiveAsync(10, It.IsAny<DateTime>())).ReturnsAsync(promotions);

        var cartItems = new List<CartItem>
        {
            new CartItem { InventoryItemId = 1, Quantity = 1, UnitPrice = 50m, Category = "Games" },
            new CartItem { InventoryItemId = 2, Quantity = 1, UnitPrice = 20m, Category = "Accessories" },
        };

        var discounts = (await _service.ApplyPromotionsAsync(cartItems, 10)).ToList();

        discounts.Should().HaveCount(1);
        discounts.Single(d => d.ItemId == 1).DiscountAmount.Should().Be(10m);
    }

    [Fact]
    public async Task ApplyPromotionsAsync_PercentageItem_AppliesOnlyToMatchingItem()
    {
        var promotions = new List<Promotion>
        {
            new Promotion { Id = 1, CompanyId = 10, Name = "Item 2 discount", Type = "percentage", DiscountPercent = 50m, Scope = "item", ScopeValue = "2", StartDate = DateTime.UtcNow.AddDays(-1), IsActive = true }
        };
        _repositoryMock.Setup(r => r.GetActiveAsync(10, It.IsAny<DateTime>())).ReturnsAsync(promotions);

        var cartItems = new List<CartItem>
        {
            new CartItem { InventoryItemId = 1, Quantity = 1, UnitPrice = 100m, Category = "Games" },
            new CartItem { InventoryItemId = 2, Quantity = 1, UnitPrice = 40m, Category = "Games" },
        };

        var discounts = (await _service.ApplyPromotionsAsync(cartItems, 10)).ToList();

        discounts.Should().HaveCount(1);
        discounts.Single(d => d.ItemId == 2).DiscountAmount.Should().Be(20m);
    }

    [Fact]
    public async Task ApplyPromotionsAsync_Bxgy_GrantsFreeUnits()
    {
        var promotions = new List<Promotion>
        {
            new Promotion { Id = 1, CompanyId = 10, Name = "Buy 2 Get 1", Type = "bxgy", BuyQuantity = 2, GetQuantity = 1, Scope = "store_wide", StartDate = DateTime.UtcNow.AddDays(-1), IsActive = true }
        };
        _repositoryMock.Setup(r => r.GetActiveAsync(10, It.IsAny<DateTime>())).ReturnsAsync(promotions);

        var cartItems = new List<CartItem>
        {
            new CartItem { InventoryItemId = 1, Quantity = 3, UnitPrice = 10m, Category = "Games" },
        };

        var discounts = (await _service.ApplyPromotionsAsync(cartItems, 10)).ToList();

        discounts.Should().HaveCount(1);
        discounts.Single(d => d.ItemId == 1).DiscountAmount.Should().Be(10m);
    }

    [Fact]
    public async Task ApplyPromotionsAsync_NoActivePromotions_ReturnsNoDiscounts()
    {
        _repositoryMock.Setup(r => r.GetActiveAsync(10, It.IsAny<DateTime>())).ReturnsAsync(new List<Promotion>());

        var cartItems = new List<CartItem>
        {
            new CartItem { InventoryItemId = 1, Quantity = 1, UnitPrice = 50m, Category = "Games" },
        };

        var discounts = (await _service.ApplyPromotionsAsync(cartItems, 10)).ToList();

        discounts.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyPromotionsAsync_EmptyCart_ReturnsEmptyDiscountList()
    {
        var promotions = new List<Promotion>
        {
            new Promotion { Id = 1, CompanyId = 10, Name = "10% Off", Type = "percentage", DiscountPercent = 10m, Scope = "store_wide", StartDate = DateTime.UtcNow.AddDays(-1), IsActive = true }
        };
        _repositoryMock.Setup(r => r.GetActiveAsync(10, It.IsAny<DateTime>())).ReturnsAsync(promotions);

        var discounts = (await _service.ApplyPromotionsAsync(new List<CartItem>(), 10)).ToList();

        discounts.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyPromotionsAsync_Bxgy_DoubleBuyQuantity_GrantsDoubleGetQuantityFree()
    {
        var promotions = new List<Promotion>
        {
            new Promotion { Id = 1, CompanyId = 10, Name = "Buy 2 Get 1", Type = "bxgy", BuyQuantity = 2, GetQuantity = 1, Scope = "store_wide", StartDate = DateTime.UtcNow.AddDays(-1), IsActive = true }
        };
        _repositoryMock.Setup(r => r.GetActiveAsync(10, It.IsAny<DateTime>())).ReturnsAsync(promotions);

        var cartItems = new List<CartItem>
        {
            new CartItem { InventoryItemId = 1, Quantity = 6, UnitPrice = 10m, Category = "Games" },
        };

        var discounts = (await _service.ApplyPromotionsAsync(cartItems, 10)).ToList();

        discounts.Should().HaveCount(1);
        discounts.Single(d => d.ItemId == 1).DiscountAmount.Should().Be(30m);
    }

    [Fact]
    public async Task ApplyPromotionsAsync_Bxgy_LessThanBuyQuantity_GrantsNoDiscount()
    {
        var promotions = new List<Promotion>
        {
            new Promotion { Id = 1, CompanyId = 10, Name = "Buy 3 Get 1", Type = "bxgy", BuyQuantity = 3, GetQuantity = 1, Scope = "store_wide", StartDate = DateTime.UtcNow.AddDays(-1), IsActive = true }
        };
        _repositoryMock.Setup(r => r.GetActiveAsync(10, It.IsAny<DateTime>())).ReturnsAsync(promotions);

        var cartItems = new List<CartItem>
        {
            new CartItem { InventoryItemId = 1, Quantity = 2, UnitPrice = 10m, Category = "Games" },
        };

        var discounts = (await _service.ApplyPromotionsAsync(cartItems, 10)).ToList();

        discounts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActivePromotionsAsync_ExpiredPromotion_ReturnsEmpty()
    {
        _repositoryMock
            .Setup(r => r.GetActiveAsync(10, It.IsAny<DateTime>()))
            .ReturnsAsync(new List<Promotion>());

        var result = await _service.GetActivePromotionsAsync(10);

        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActivePromotionsAsync_FuturePromotion_ReturnsEmpty()
    {
        _repositoryMock
            .Setup(r => r.GetActiveAsync(10, It.IsAny<DateTime>()))
            .ReturnsAsync(new List<Promotion>());

        var result = await _service.GetActivePromotionsAsync(10);

        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyPromotionsAsync_MultipleSimultaneousPromotions_AllAppliedIndependently()
    {
        var promotions = new List<Promotion>
        {
            new Promotion { Id = 1, CompanyId = 10, Name = "10% Off Everything", Type = "percentage", DiscountPercent = 10m, Scope = "store_wide", StartDate = DateTime.UtcNow.AddDays(-1), IsActive = true },
            new Promotion { Id = 2, CompanyId = 10, Name = "20% Off Games", Type = "percentage", DiscountPercent = 20m, Scope = "category", ScopeValue = "Games", StartDate = DateTime.UtcNow.AddDays(-1), IsActive = true }
        };
        _repositoryMock.Setup(r => r.GetActiveAsync(10, It.IsAny<DateTime>())).ReturnsAsync(promotions);

        var cartItems = new List<CartItem>
        {
            new CartItem { InventoryItemId = 1, Quantity = 1, UnitPrice = 100m, Category = "Games" },
            new CartItem { InventoryItemId = 2, Quantity = 1, UnitPrice = 50m, Category = "Accessories" },
        };

        var discounts = (await _service.ApplyPromotionsAsync(cartItems, 10)).ToList();

        discounts.Should().HaveCount(2);
        discounts.Single(d => d.ItemId == 1).DiscountAmount.Should().Be(30m);
        discounts.Single(d => d.ItemId == 2).DiscountAmount.Should().Be(5m);
    }

    [Fact]
    public async Task ApplyPromotionsAsync_PercentageStoreWide_RoundingEdgeCase()
    {
        var promotions = new List<Promotion>
        {
            new Promotion { Id = 1, CompanyId = 10, Name = "33.3% Off", Type = "percentage", DiscountPercent = 33.3m, Scope = "store_wide", StartDate = DateTime.UtcNow.AddDays(-1), IsActive = true }
        };
        _repositoryMock.Setup(r => r.GetActiveAsync(10, It.IsAny<DateTime>())).ReturnsAsync(promotions);

        var cartItems = new List<CartItem>
        {
            new CartItem { InventoryItemId = 1, Quantity = 1, UnitPrice = 10m, Category = "Games" },
        };

        var discounts = (await _service.ApplyPromotionsAsync(cartItems, 10)).ToList();

        discounts.Should().HaveCount(1);
        discounts.Single(d => d.ItemId == 1).DiscountAmount.Should().Be(3.33m);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsError()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(99, 10)).ReturnsAsync((Promotion?)null);

        var result = await _service.UpdateAsync(99, new UpdatePromotionRequest { Name = "New Name" }, 10);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("99");
    }

    [Fact]
    public async Task UpdateAsync_ValidRequest_ReturnsUpdatedPromotion()
    {
        var existing = new Promotion { Id = 1, CompanyId = 10, Name = "Old", Type = "percentage", Scope = "store_wide", DiscountPercent = 5m, StartDate = DateTime.UtcNow, IsActive = true };
        var updated = new Promotion { Id = 1, CompanyId = 10, Name = "New", Type = "percentage", Scope = "store_wide", DiscountPercent = 5m, StartDate = existing.StartDate, IsActive = true };
        _repositoryMock.Setup(r => r.GetByIdAsync(1, 10)).ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Promotion>())).ReturnsAsync(updated);

        var result = await _service.UpdateAsync(1, new UpdatePromotionRequest { Name = "New" }, 10);

        result.Success.Should().BeTrue();
        result.Data!.Name.Should().Be("New");
    }

    [Fact]
    public async Task CreateAsync_BxgyWithoutBuyOrGetQuantity_ReturnsValidationError()
    {
        var request = new CreatePromotionRequest
        {
            Name = "Bad BXGY",
            Type = "bxgy",
            Scope = "store_wide",
            StartDate = DateTime.UtcNow,
            IsActive = true,
        };

        var result = await _service.CreateAsync(request, 10);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("BuyQuantity").And.Contain("GetQuantity");
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<Promotion>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_BxgyWithZeroBuyQuantity_ReturnsValidationError()
    {
        var request = new CreatePromotionRequest
        {
            Name = "Bad BXGY",
            Type = "bxgy",
            BuyQuantity = 0,
            GetQuantity = 1,
            Scope = "store_wide",
            StartDate = DateTime.UtcNow,
        };

        var result = await _service.CreateAsync(request, 10);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<Promotion>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_CategoryScopeWithoutScopeValue_ReturnsValidationError()
    {
        var request = new CreatePromotionRequest
        {
            Name = "No ScopeValue",
            Type = "percentage",
            DiscountPercent = 10m,
            Scope = "category",
            StartDate = DateTime.UtcNow,
        };

        var result = await _service.CreateAsync(request, 10);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("ScopeValue");
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<Promotion>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ItemScopeWithoutScopeValue_ReturnsValidationError()
    {
        var request = new CreatePromotionRequest
        {
            Name = "No ScopeValue",
            Type = "percentage",
            DiscountPercent = 10m,
            Scope = "item",
            StartDate = DateTime.UtcNow,
        };

        var result = await _service.CreateAsync(request, 10);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("ScopeValue");
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<Promotion>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_PercentageGreaterThan100_ReturnsValidationError()
    {
        var request = new CreatePromotionRequest
        {
            Name = "Too High",
            Type = "percentage",
            DiscountPercent = 150m,
            Scope = "store_wide",
            StartDate = DateTime.UtcNow,
        };

        var result = await _service.CreateAsync(request, 10);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("DiscountPercent");
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<Promotion>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_PercentageBelowZero_ReturnsValidationError()
    {
        var request = new CreatePromotionRequest
        {
            Name = "Negative",
            Type = "percentage",
            DiscountPercent = -5m,
            Scope = "store_wide",
            StartDate = DateTime.UtcNow,
        };

        var result = await _service.CreateAsync(request, 10);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("DiscountPercent");
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<Promotion>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_PercentageMissing_ReturnsValidationError()
    {
        var request = new CreatePromotionRequest
        {
            Name = "Missing Pct",
            Type = "percentage",
            Scope = "store_wide",
            StartDate = DateTime.UtcNow,
        };

        var result = await _service.CreateAsync(request, 10);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("DiscountPercent");
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<Promotion>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ValidBxgy_PersistsPromotion()
    {
        var request = new CreatePromotionRequest
        {
            Name = "Buy 2 Get 1",
            Type = "bxgy",
            BuyQuantity = 2,
            GetQuantity = 1,
            Scope = "store_wide",
            StartDate = DateTime.UtcNow,
        };
        var created = new Promotion { Id = 7, CompanyId = 10, Name = "Buy 2 Get 1", Type = "bxgy", BuyQuantity = 2, GetQuantity = 1, Scope = "store_wide", StartDate = request.StartDate, IsActive = true };
        _repositoryMock.Setup(r => r.CreateAsync(It.IsAny<Promotion>())).ReturnsAsync(created);

        var result = await _service.CreateAsync(request, 10);

        result.Success.Should().BeTrue();
        result.Data!.Id.Should().Be(7);
    }

    [Fact]
    public async Task UpdateAsync_ChangeTypeToBxgyWithoutQuantities_ReturnsValidationError()
    {
        var existing = new Promotion { Id = 1, CompanyId = 10, Name = "Old", Type = "percentage", Scope = "store_wide", DiscountPercent = 5m, StartDate = DateTime.UtcNow, IsActive = true };
        _repositoryMock.Setup(r => r.GetByIdAsync(1, 10)).ReturnsAsync(existing);

        var result = await _service.UpdateAsync(1, new UpdatePromotionRequest { Type = "bxgy" }, 10);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("BuyQuantity").And.Contain("GetQuantity");
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<Promotion>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ChangeScopeToCategoryWithoutScopeValue_ReturnsValidationError()
    {
        var existing = new Promotion { Id = 1, CompanyId = 10, Name = "Old", Type = "percentage", Scope = "store_wide", DiscountPercent = 5m, StartDate = DateTime.UtcNow, IsActive = true };
        _repositoryMock.Setup(r => r.GetByIdAsync(1, 10)).ReturnsAsync(existing);

        var result = await _service.UpdateAsync(1, new UpdatePromotionRequest { Scope = "category" }, 10);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("ScopeValue");
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<Promotion>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_PercentageOutOfRange_ReturnsValidationError()
    {
        var existing = new Promotion { Id = 1, CompanyId = 10, Name = "Old", Type = "percentage", Scope = "store_wide", DiscountPercent = 5m, StartDate = DateTime.UtcNow, IsActive = true };
        _repositoryMock.Setup(r => r.GetByIdAsync(1, 10)).ReturnsAsync(existing);

        var result = await _service.UpdateAsync(1, new UpdatePromotionRequest { DiscountPercent = 150m }, 10);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("DiscountPercent");
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<Promotion>()), Times.Never);
    }
}
