using FluentAssertions;
using Moq;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Services;
using Xunit;

namespace MyStore.Tests.Services;

public class TradeInServiceTests
{
    private readonly Mock<ITradeInRepository> _tradeInRepoMock;
    private readonly Mock<IInventoryRepository> _inventoryRepoMock;
    private readonly Mock<IGameRepository> _gameRepoMock;
    private readonly Mock<ILocationRepository> _locationRepoMock;
    private readonly Mock<ILoyaltyService> _loyaltyMock;
    private readonly TradeInService _service;
    private readonly TradeInService _serviceNoLoyalty;

    public TradeInServiceTests()
    {
        _tradeInRepoMock = new Mock<ITradeInRepository>();
        _inventoryRepoMock = new Mock<IInventoryRepository>();
        _gameRepoMock = new Mock<IGameRepository>();
        _locationRepoMock = new Mock<ILocationRepository>();
        _loyaltyMock = new Mock<ILoyaltyService>();
        _locationRepoMock
            .Setup(r => r.GetByCompanyIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<Location> { new() { Id = 7, CompanyId = 5, Name = "Main", IsPrimary = true } });
        _service = new TradeInService(_tradeInRepoMock.Object, _inventoryRepoMock.Object, _gameRepoMock.Object, _locationRepoMock.Object, _loyaltyMock.Object);
        _serviceNoLoyalty = new TradeInService(_tradeInRepoMock.Object, _inventoryRepoMock.Object, _gameRepoMock.Object, _locationRepoMock.Object, null);
    }

    [Fact]
    public async Task CreateDraftAsync_SetsStatusToDraftAndCompanyId()
    {
        var tradeIn = new TradeIn { PaymentType = "cash", CreatedBy = 1 };
        var created = new TradeIn { Id = 1, CompanyId = 5, Status = "draft", PaymentType = "cash", CreatedBy = 1, Items = new List<TradeInItem>() };

        _tradeInRepoMock
            .Setup(r => r.CreateAsync(It.Is<TradeIn>(t => t.CompanyId == 5 && t.Status == "draft")))
            .ReturnsAsync(created);

        var result = await _service.CreateDraftAsync(tradeIn, 5);

        result.Success.Should().BeTrue();
        result.Data.Should().Be(created);
        result.Data!.Status.Should().Be("draft");
    }

    [Fact]
    public async Task AddItemAsync_DraftTradeIn_AddsItemSuccessfully()
    {
        var draft = new TradeIn { Id = 1, CompanyId = 5, Status = "draft", Items = new List<TradeInItem>() };
        var item = new TradeInItem { GameTitle = "Sonic", Platform = "Sega Genesis", Condition = "good", OfferedValue = 10m };
        var created = new TradeInItem { Id = 10, TradeInId = 1, GameTitle = "Sonic", Platform = "Sega Genesis", Condition = "good", OfferedValue = 10m };

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(draft);
        _tradeInRepoMock.Setup(r => r.AddItemAsync(It.IsAny<TradeInItem>())).ReturnsAsync(created);

        var result = await _service.AddItemAsync(item, 1, 5);

        result.Success.Should().BeTrue();
        result.Data.Should().Be(created);
        _tradeInRepoMock.Verify(r => r.AddItemAsync(It.Is<TradeInItem>(i => i.TradeInId == 1)), Times.Once);
    }

    [Fact]
    public async Task AddItemAsync_NonDraftTradeIn_ReturnsError()
    {
        var completed = new TradeIn { Id = 1, CompanyId = 5, Status = "completed", Items = new List<TradeInItem>() };
        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(completed);

        var result = await _service.AddItemAsync(new TradeInItem(), 1, 5);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("completed");
        _tradeInRepoMock.Verify(r => r.AddItemAsync(It.IsAny<TradeInItem>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_CreatesInventoryItemForEachAcceptedItem()
    {
        var items = new List<TradeInItem>
        {
            new() { Id = 1, TradeInId = 1, GameTitle = "Mario", Platform = "NES", Condition = "good", OfferedValue = 20m, AcceptedValue = 15m },
            new() { Id = 2, TradeInId = 1, GameTitle = "Zelda", Platform = "NES", Condition = "fair", OfferedValue = 30m, AcceptedValue = 0m },
        };
        var draft = new TradeIn { Id = 1, CompanyId = 5, CustomerId = 10, Status = "draft", PaymentType = "cash", Items = items };
        var completed = new TradeIn { Id = 1, CompanyId = 5, Status = "completed", Items = items };

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(draft);
        _tradeInRepoMock.Setup(r => r.CompleteAsync(1, "cash", It.IsAny<DateTime>())).ReturnsAsync(completed);
        _tradeInRepoMock.Setup(r => r.UpdateItemAsync(It.IsAny<TradeInItem>())).ReturnsAsync((TradeInItem i) => i);
        _inventoryRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<InventoryItem>()))
            .ReturnsAsync((InventoryItem inv) => { inv.Id = 99; return inv; });

        var result = await _service.CompleteAsync(1, 5, "cash");

        result.Success.Should().BeTrue();
        _inventoryRepoMock.Verify(r => r.CreateAsync(It.IsAny<InventoryItem>()), Times.Once);
        _loyaltyMock.Verify(l => l.EarnFromTradeInAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_AcceptedItem_UpsertsGameAndSetsInventoryGameId()
    {
        var items = new List<TradeInItem>
        {
            new() { Id = 1, TradeInId = 1, GameTitle = "Mario Kart", Platform = "N64", Condition = "good", OfferedValue = 20m, AcceptedValue = 20m },
        };
        var draft = new TradeIn { Id = 1, CompanyId = 5, CustomerId = 53, Status = "draft", PaymentType = "store_credit", Items = items };
        var completed = new TradeIn { Id = 1, CompanyId = 5, Status = "completed", Items = items };

        InventoryItem? capturedInventory = null;
        Game? capturedGame = null;

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(draft);
        _tradeInRepoMock.Setup(r => r.CompleteAsync(1, "store_credit", It.IsAny<DateTime>())).ReturnsAsync(completed);
        _tradeInRepoMock.Setup(r => r.UpdateItemAsync(It.IsAny<TradeInItem>())).ReturnsAsync((TradeInItem i) => i);
        _gameRepoMock
            .Setup(r => r.UpsertAsync(It.IsAny<Game>()))
            .Callback<Game>(g => capturedGame = g)
            .Returns(Task.CompletedTask);
        _inventoryRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<InventoryItem>()))
            .Callback<InventoryItem>(inv => capturedInventory = inv)
            .ReturnsAsync((InventoryItem inv) => { inv.Id = 99; return inv; });

        _loyaltyMock
            .Setup(l => l.GetSettingsAsync(5))
            .ReturnsAsync(ApiResponse<LoyaltySettings>.SuccessResponse(new LoyaltySettings { IsEnabled = false }));

        var result = await _service.CompleteAsync(1, 5, "store_credit");

        result.Success.Should().BeTrue();
        _gameRepoMock.Verify(r => r.UpsertAsync(It.IsAny<Game>()), Times.Once);
        capturedGame.Should().NotBeNull();
        capturedGame!.Id.Should().NotBeNullOrWhiteSpace();
        capturedGame.Title.Should().Be("Mario Kart");
        capturedGame.Console.Should().Be("N64");
        capturedInventory.Should().NotBeNull();
        capturedInventory!.Game.Should().NotBeNull();
        capturedInventory.Game!.Id.Should().Be(capturedGame.Id);
    }

    [Fact]
    public async Task CompleteAsync_SameTitleAndPlatform_ProducesStableGameId()
    {
        var firstItems = new List<TradeInItem>
        {
            new() { Id = 1, TradeInId = 1, GameTitle = "Mario Kart", Platform = "N64", Condition = "good", OfferedValue = 20m, AcceptedValue = 20m },
        };
        var secondItems = new List<TradeInItem>
        {
            new() { Id = 2, TradeInId = 2, GameTitle = "  mario kart  ", Platform = "n64", Condition = "good", OfferedValue = 20m, AcceptedValue = 20m },
        };
        var firstDraft = new TradeIn { Id = 1, CompanyId = 5, Status = "draft", PaymentType = "cash", Items = firstItems };
        var secondDraft = new TradeIn { Id = 2, CompanyId = 5, Status = "draft", PaymentType = "cash", Items = secondItems };
        var firstCompleted = new TradeIn { Id = 1, CompanyId = 5, Status = "completed", Items = firstItems };
        var secondCompleted = new TradeIn { Id = 2, CompanyId = 5, Status = "completed", Items = secondItems };

        var capturedGameIds = new List<string>();

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(firstDraft);
        _tradeInRepoMock.Setup(r => r.GetByIdAsync(2, 5)).ReturnsAsync(secondDraft);
        _tradeInRepoMock.Setup(r => r.CompleteAsync(1, "cash", It.IsAny<DateTime>())).ReturnsAsync(firstCompleted);
        _tradeInRepoMock.Setup(r => r.CompleteAsync(2, "cash", It.IsAny<DateTime>())).ReturnsAsync(secondCompleted);
        _tradeInRepoMock.Setup(r => r.UpdateItemAsync(It.IsAny<TradeInItem>())).ReturnsAsync((TradeInItem i) => i);
        _gameRepoMock
            .Setup(r => r.UpsertAsync(It.IsAny<Game>()))
            .Callback<Game>(g => capturedGameIds.Add(g.Id))
            .Returns(Task.CompletedTask);
        _inventoryRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<InventoryItem>()))
            .ReturnsAsync((InventoryItem inv) => { inv.Id = 99; return inv; });

        await _service.CompleteAsync(1, 5, "cash");
        await _service.CompleteAsync(2, 5, "cash");

        capturedGameIds.Should().HaveCount(2);
        capturedGameIds[0].Should().Be(capturedGameIds[1]);
    }

    [Fact]
    public async Task CompleteAsync_StoreCreditWithCustomer_CallsLoyaltyService()
    {
        var items = new List<TradeInItem>
        {
            new() { Id = 1, TradeInId = 1, GameTitle = "Mario", Platform = "NES", Condition = "good", OfferedValue = 20m, AcceptedValue = 15m },
        };
        var draft = new TradeIn { Id = 1, CompanyId = 5, CustomerId = 10, Status = "draft", PaymentType = "store_credit", Items = items };
        var completed = new TradeIn { Id = 1, CompanyId = 5, Status = "completed", Items = items };

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(draft);
        _tradeInRepoMock.Setup(r => r.CompleteAsync(1, "store_credit", It.IsAny<DateTime>())).ReturnsAsync(completed);
        _tradeInRepoMock.Setup(r => r.UpdateItemAsync(It.IsAny<TradeInItem>())).ReturnsAsync((TradeInItem i) => i);
        _inventoryRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<InventoryItem>()))
            .ReturnsAsync((InventoryItem inv) => { inv.Id = 99; return inv; });
        _loyaltyMock
            .Setup(l => l.GetSettingsAsync(5))
            .ReturnsAsync(ApiResponse<LoyaltySettings>.SuccessResponse(new LoyaltySettings { IsEnabled = true }));
        _loyaltyMock
            .Setup(l => l.EarnFromTradeInAsync(10, 5, 15m))
            .Returns(Task.CompletedTask);

        var result = await _service.CompleteAsync(1, 5, "store_credit");

        result.Success.Should().BeTrue();
        _loyaltyMock.Verify(l => l.EarnFromTradeInAsync(10, 5, 15m), Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_StoreCreditNoLoyaltyService_CompletesWithoutError()
    {
        var items = new List<TradeInItem>
        {
            new() { Id = 1, TradeInId = 1, GameTitle = "Mario", Platform = "NES", Condition = "good", OfferedValue = 20m, AcceptedValue = 15m },
        };
        var draft = new TradeIn { Id = 1, CompanyId = 5, CustomerId = 10, Status = "draft", PaymentType = "store_credit", Items = items };
        var completed = new TradeIn { Id = 1, CompanyId = 5, Status = "completed", Items = items };

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(draft);
        _tradeInRepoMock.Setup(r => r.CompleteAsync(1, "store_credit", It.IsAny<DateTime>())).ReturnsAsync(completed);
        _tradeInRepoMock.Setup(r => r.UpdateItemAsync(It.IsAny<TradeInItem>())).ReturnsAsync((TradeInItem i) => i);
        _inventoryRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<InventoryItem>()))
            .ReturnsAsync((InventoryItem inv) => { inv.Id = 99; return inv; });

        var result = await _serviceNoLoyalty.CompleteAsync(1, 5, "store_credit");

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task RejectAsync_DraftTradeIn_SetsStatusToRejected()
    {
        var draft = new TradeIn { Id = 1, CompanyId = 5, Status = "draft", Items = new List<TradeInItem>() };
        TradeIn? captured = null;

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(draft);
        _tradeInRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<TradeIn>()))
            .Callback<TradeIn>(t => captured = t)
            .ReturnsAsync((TradeIn t) => t);

        var result = await _service.RejectAsync(1, 5);

        result.Success.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Status.Should().Be("rejected");
        _inventoryRepoMock.Verify(r => r.CreateAsync(It.IsAny<InventoryItem>()), Times.Never);
    }

    [Fact]
    public async Task RejectAsync_NonDraftTradeIn_ReturnsError()
    {
        var completed = new TradeIn { Id = 1, CompanyId = 5, Status = "completed", Items = new List<TradeInItem>() };
        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(completed);

        var result = await _service.RejectAsync(1, 5);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("completed");
        _tradeInRepoMock.Verify(r => r.UpdateAsync(It.IsAny<TradeIn>()), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_WrongCompany_ReturnsNull()
    {
        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 99)).ReturnsAsync((TradeIn?)null);

        var result = await _service.GetByIdAsync(1, 99);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task GetByIdAsync_CorrectCompany_ReturnsTradeIn()
    {
        var tradeIn = new TradeIn { Id = 1, CompanyId = 5, Status = "draft", Items = new List<TradeInItem>() };
        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(tradeIn);

        var result = await _service.GetByIdAsync(1, 5);

        result.Success.Should().BeTrue();
        result.Data.Should().Be(tradeIn);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsListFromRepository()
    {
        var tradeIns = new List<TradeIn>
        {
            new() { Id = 1, CompanyId = 5, Status = "draft", Items = new List<TradeInItem>() },
            new() { Id = 2, CompanyId = 5, Status = "completed", Items = new List<TradeInItem>() },
        };
        _tradeInRepoMock.Setup(r => r.GetAllAsync(5, null, null, null)).ReturnsAsync(tradeIns);

        var result = await _service.GetAllAsync(5);

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllAsync_WithStatusFilter_PassesFilterToRepository()
    {
        var tradeIns = new List<TradeIn>
        {
            new() { Id = 1, CompanyId = 5, Status = "completed", Items = new List<TradeInItem>() },
        };
        _tradeInRepoMock.Setup(r => r.GetAllAsync(5, "completed", null, null)).ReturnsAsync(tradeIns);

        var result = await _service.GetAllAsync(5, "completed");

        result.Success.Should().BeTrue();
        _tradeInRepoMock.Verify(r => r.GetAllAsync(5, "completed", null, null), Times.Once);
    }

    [Fact]
    public async Task UpdateItemAsync_DraftTradeIn_UpdatesItemSuccessfully()
    {
        var item = new TradeInItem { Id = 10, TradeInId = 1, GameTitle = "Sonic", Platform = "NES", Condition = "fair", OfferedValue = 10m, AcceptedValue = 8m };
        var draft = new TradeIn { Id = 1, CompanyId = 5, Status = "draft", Items = new List<TradeInItem> { item } };
        var updated = new TradeInItem { Id = 10, TradeInId = 1, GameTitle = "Sonic", Platform = "NES", Condition = "fair", OfferedValue = 10m, AcceptedValue = 8m };

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(draft);
        _tradeInRepoMock.Setup(r => r.UpdateItemAsync(It.IsAny<TradeInItem>())).ReturnsAsync(updated);

        var result = await _service.UpdateItemAsync(item, 5);

        result.Success.Should().BeTrue();
        result.Data.Should().Be(updated);
        _tradeInRepoMock.Verify(r => r.UpdateItemAsync(item), Times.Once);
    }

    [Fact]
    public async Task UpdateItemAsync_NonDraftTradeIn_ReturnsError()
    {
        var item = new TradeInItem { Id = 10, TradeInId = 1 };
        var completed = new TradeIn { Id = 1, CompanyId = 5, Status = "completed", Items = new List<TradeInItem> { item } };

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(completed);

        var result = await _service.UpdateItemAsync(item, 5);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("completed");
        _tradeInRepoMock.Verify(r => r.UpdateItemAsync(It.IsAny<TradeInItem>()), Times.Never);
    }

    [Fact]
    public async Task UpdateItemAsync_ItemNotFound_ReturnsError()
    {
        var item = new TradeInItem { Id = 99, TradeInId = 1 };
        var draft = new TradeIn { Id = 1, CompanyId = 5, Status = "draft", Items = new List<TradeInItem> { item } };

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(draft);
        _tradeInRepoMock.Setup(r => r.UpdateItemAsync(It.IsAny<TradeInItem>())).ReturnsAsync((TradeInItem?)null);

        var result = await _service.UpdateItemAsync(item, 5);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task UpdateTradeInAsync_DraftTradeIn_UpdatesNotesAndCustomer()
    {
        var existingItem = new TradeInItem { Id = 1, TradeInId = 1 };
        var draft = new TradeIn { Id = 1, CompanyId = 5, Status = "draft", Notes = "old notes", CustomerId = null, Items = new List<TradeInItem> { existingItem } };
        var updatedDraft = new TradeIn { Id = 1, CompanyId = 5, Status = "draft", Notes = "new notes", CustomerId = 7, Items = new List<TradeInItem> { existingItem } };

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(draft);
        _tradeInRepoMock.Setup(r => r.UpdateAsync(It.IsAny<TradeIn>())).ReturnsAsync((TradeIn t) => t);
        _tradeInRepoMock.Setup(r => r.UpdateItemAsync(It.IsAny<TradeInItem>())).ReturnsAsync((TradeInItem i) => i);
        _tradeInRepoMock.SetupSequence(r => r.GetByIdAsync(1, 5))
            .ReturnsAsync(draft)
            .ReturnsAsync(updatedDraft);

        var result = await _service.UpdateTradeInAsync(1, 5, "new notes", 7, new List<TradeInItem> { existingItem });

        result.Success.Should().BeTrue();
        _tradeInRepoMock.Verify(r => r.UpdateAsync(It.Is<TradeIn>(t => t.Notes == "new notes" && t.CustomerId == 7)), Times.Once);
    }

    [Fact]
    public async Task UpdateTradeInAsync_AddsNewItems()
    {
        var draft = new TradeIn { Id = 1, CompanyId = 5, Status = "draft", Items = new List<TradeInItem>() };
        var newItem = new TradeInItem { Id = 0, TradeInId = 0, GameTitle = "Mario", Platform = "NES", Condition = "good", OfferedValue = 10m };
        var addedItem = new TradeInItem { Id = 5, TradeInId = 1, GameTitle = "Mario", Platform = "NES", Condition = "good", OfferedValue = 10m };

        _tradeInRepoMock.SetupSequence(r => r.GetByIdAsync(1, 5))
            .ReturnsAsync(draft)
            .ReturnsAsync(draft);
        _tradeInRepoMock.Setup(r => r.UpdateAsync(It.IsAny<TradeIn>())).ReturnsAsync((TradeIn t) => t);
        _tradeInRepoMock.Setup(r => r.AddItemAsync(It.IsAny<TradeInItem>())).ReturnsAsync(addedItem);

        var result = await _service.UpdateTradeInAsync(1, 5, null, null, new List<TradeInItem> { newItem });

        result.Success.Should().BeTrue();
        _tradeInRepoMock.Verify(r => r.AddItemAsync(It.IsAny<TradeInItem>()), Times.Once);
    }

    [Fact]
    public async Task UpdateTradeInAsync_NonDraftTradeIn_ReturnsError()
    {
        var completed = new TradeIn { Id = 1, CompanyId = 5, Status = "completed", Items = new List<TradeInItem>() };
        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(completed);

        var result = await _service.UpdateTradeInAsync(1, 5, "notes", null, new List<TradeInItem>());

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("completed");
        _tradeInRepoMock.Verify(r => r.UpdateAsync(It.IsAny<TradeIn>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_CashPayment_DoesNotCallLoyaltyService()
    {
        var items = new List<TradeInItem>
        {
            new() { Id = 1, TradeInId = 1, GameTitle = "Mario", Platform = "NES", Condition = "good", OfferedValue = 20m, AcceptedValue = 15m },
        };
        var draft = new TradeIn { Id = 1, CompanyId = 5, CustomerId = 10, Status = "draft", Items = items };
        var completed = new TradeIn { Id = 1, CompanyId = 5, Status = "completed", Items = items };

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(draft);
        _tradeInRepoMock.Setup(r => r.CompleteAsync(1, "cash", It.IsAny<DateTime>())).ReturnsAsync(completed);
        _tradeInRepoMock.Setup(r => r.UpdateItemAsync(It.IsAny<TradeInItem>())).ReturnsAsync((TradeInItem i) => i);
        _inventoryRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<InventoryItem>()))
            .ReturnsAsync((InventoryItem inv) => { inv.Id = 99; return inv; });

        var result = await _service.CompleteAsync(1, 5, "cash");

        result.Success.Should().BeTrue();
        _loyaltyMock.Verify(l => l.EarnFromTradeInAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_StoreCreditAmountEqualsTotalAcceptedValue()
    {
        var items = new List<TradeInItem>
        {
            new() { Id = 1, TradeInId = 1, GameTitle = "Mario", Platform = "NES", Condition = "good", OfferedValue = 20m, AcceptedValue = 15m },
            new() { Id = 2, TradeInId = 1, GameTitle = "Zelda", Platform = "NES", Condition = "fair", OfferedValue = 10m, AcceptedValue = 7m },
        };
        var draft = new TradeIn { Id = 1, CompanyId = 5, CustomerId = 10, Status = "draft", Items = items };
        var completed = new TradeIn { Id = 1, CompanyId = 5, Status = "completed", Items = items };

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(draft);
        _tradeInRepoMock.Setup(r => r.CompleteAsync(1, "store_credit", It.IsAny<DateTime>())).ReturnsAsync(completed);
        _tradeInRepoMock.Setup(r => r.UpdateItemAsync(It.IsAny<TradeInItem>())).ReturnsAsync((TradeInItem i) => i);
        _inventoryRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<InventoryItem>()))
            .ReturnsAsync((InventoryItem inv) => { inv.Id = 99; return inv; });
        _loyaltyMock
            .Setup(l => l.GetSettingsAsync(5))
            .ReturnsAsync(ApiResponse<LoyaltySettings>.SuccessResponse(new LoyaltySettings { IsEnabled = true }));
        _loyaltyMock
            .Setup(l => l.EarnFromTradeInAsync(10, 5, 22m))
            .Returns(Task.CompletedTask);

        await _service.CompleteAsync(1, 5, "store_credit");

        _loyaltyMock.Verify(l => l.EarnFromTradeInAsync(10, 5, 22m), Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_ItemsWithZeroAcceptedValue_NotAddedToInventory()
    {
        var items = new List<TradeInItem>
        {
            new() { Id = 1, TradeInId = 1, GameTitle = "Mario", Platform = "NES", Condition = "good", OfferedValue = 20m, AcceptedValue = 0m },
            new() { Id = 2, TradeInId = 1, GameTitle = "Zelda", Platform = "NES", Condition = "fair", OfferedValue = 10m, AcceptedValue = null },
        };
        var draft = new TradeIn { Id = 1, CompanyId = 5, CustomerId = 10, Status = "draft", Items = items };
        var completed = new TradeIn { Id = 1, CompanyId = 5, Status = "completed", Items = items };

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(draft);
        _tradeInRepoMock.Setup(r => r.CompleteAsync(1, "cash", It.IsAny<DateTime>())).ReturnsAsync(completed);

        var result = await _service.CompleteAsync(1, 5, "cash");

        result.Success.Should().BeTrue();
        _inventoryRepoMock.Verify(r => r.CreateAsync(It.IsAny<InventoryItem>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_TradeInNotFound_ReturnsError()
    {
        _tradeInRepoMock.Setup(r => r.GetByIdAsync(99, 5)).ReturnsAsync((TradeIn?)null);

        var result = await _service.CompleteAsync(99, 5, "cash");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task CompleteAsync_NonDraftTradeIn_ReturnsError()
    {
        var rejected = new TradeIn { Id = 1, CompanyId = 5, Status = "rejected", Items = new List<TradeInItem>() };
        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(rejected);

        var result = await _service.CompleteAsync(1, 5, "cash");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("rejected");
    }

    [Fact]
    public async Task RejectAsync_TradeInNotFound_ReturnsError()
    {
        _tradeInRepoMock.Setup(r => r.GetByIdAsync(99, 5)).ReturnsAsync((TradeIn?)null);

        var result = await _service.RejectAsync(99, 5);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task AddItemAsync_TradeInNotFound_ReturnsError()
    {
        _tradeInRepoMock.Setup(r => r.GetByIdAsync(99, 5)).ReturnsAsync((TradeIn?)null);

        var result = await _service.AddItemAsync(new TradeInItem(), 99, 5);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task CompleteAsync_AcceptedItem_SetsInventoryLocationIdFromPrimaryLocation()
    {
        var items = new List<TradeInItem>
        {
            new() { Id = 1, TradeInId = 1, GameTitle = "Mario Kart", Platform = "N64", Condition = "good", OfferedValue = 20m, AcceptedValue = 20m },
        };
        var draft = new TradeIn { Id = 1, CompanyId = 5, CustomerId = 53, Status = "draft", PaymentType = "store_credit", Items = items };
        var completed = new TradeIn { Id = 1, CompanyId = 5, Status = "completed", Items = items };

        InventoryItem? capturedInventory = null;

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(draft);
        _tradeInRepoMock.Setup(r => r.CompleteAsync(1, "store_credit", It.IsAny<DateTime>())).ReturnsAsync(completed);
        _tradeInRepoMock.Setup(r => r.UpdateItemAsync(It.IsAny<TradeInItem>())).ReturnsAsync((TradeInItem i) => i);
        _locationRepoMock
            .Setup(r => r.GetByCompanyIdAsync(5))
            .ReturnsAsync(new List<Location>
            {
                new() { Id = 11, CompanyId = 5, Name = "Secondary", IsPrimary = false },
                new() { Id = 22, CompanyId = 5, Name = "Main", IsPrimary = true },
            });
        _inventoryRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<InventoryItem>()))
            .Callback<InventoryItem>(inv => capturedInventory = inv)
            .ReturnsAsync((InventoryItem inv) => { inv.Id = 99; return inv; });
        _loyaltyMock
            .Setup(l => l.GetSettingsAsync(5))
            .ReturnsAsync(ApiResponse<LoyaltySettings>.SuccessResponse(new LoyaltySettings { IsEnabled = false }));

        var result = await _service.CompleteAsync(1, 5, "store_credit");

        result.Success.Should().BeTrue();
        capturedInventory.Should().NotBeNull();
        capturedInventory!.LocationId.Should().BeGreaterThan(0);
        capturedInventory.LocationId.Should().Be(22);
    }

    [Fact]
    public async Task CompleteAsync_NoPrimaryLocation_UsesFirstLocationByIdAndSetsLocationId()
    {
        var items = new List<TradeInItem>
        {
            new() { Id = 1, TradeInId = 1, GameTitle = "Mario", Platform = "NES", Condition = "good", OfferedValue = 15m, AcceptedValue = 15m },
        };
        var draft = new TradeIn { Id = 1, CompanyId = 5, Status = "draft", PaymentType = "cash", Items = items };
        var completed = new TradeIn { Id = 1, CompanyId = 5, Status = "completed", Items = items };

        InventoryItem? capturedInventory = null;

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(draft);
        _tradeInRepoMock.Setup(r => r.CompleteAsync(1, "cash", It.IsAny<DateTime>())).ReturnsAsync(completed);
        _tradeInRepoMock.Setup(r => r.UpdateItemAsync(It.IsAny<TradeInItem>())).ReturnsAsync((TradeInItem i) => i);
        _locationRepoMock
            .Setup(r => r.GetByCompanyIdAsync(5))
            .ReturnsAsync(new List<Location>
            {
                new() { Id = 33, CompanyId = 5, Name = "Second", IsPrimary = false },
                new() { Id = 12, CompanyId = 5, Name = "First", IsPrimary = false },
            });
        _inventoryRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<InventoryItem>()))
            .Callback<InventoryItem>(inv => capturedInventory = inv)
            .ReturnsAsync((InventoryItem inv) => { inv.Id = 99; return inv; });

        var result = await _service.CompleteAsync(1, 5, "cash");

        result.Success.Should().BeTrue();
        capturedInventory.Should().NotBeNull();
        capturedInventory!.LocationId.Should().Be(12);
    }

    [Fact]
    public async Task CompleteAsync_AcceptedItem_WritesCreatedInventoryIdBackToTradeInItem()
    {
        var items = new List<TradeInItem>
        {
            new() { Id = 1, TradeInId = 1, GameTitle = "Mario Kart", Platform = "N64", Condition = "good", OfferedValue = 20m, AcceptedValue = 20m },
        };
        var draft = new TradeIn { Id = 1, CompanyId = 5, CustomerId = 53, Status = "draft", PaymentType = "store_credit", Items = items };
        var completed = new TradeIn { Id = 1, CompanyId = 5, Status = "completed", Items = items };

        TradeInItem? capturedItemUpdate = null;

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(draft);
        _tradeInRepoMock.Setup(r => r.CompleteAsync(1, "store_credit", It.IsAny<DateTime>())).ReturnsAsync(completed);
        _tradeInRepoMock
            .Setup(r => r.UpdateItemAsync(It.IsAny<TradeInItem>()))
            .Callback<TradeInItem>(i => capturedItemUpdate = i)
            .ReturnsAsync((TradeInItem i) => i);
        _inventoryRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<InventoryItem>()))
            .ReturnsAsync((InventoryItem inv) => { inv.Id = 4242; return inv; });
        _loyaltyMock
            .Setup(l => l.GetSettingsAsync(5))
            .ReturnsAsync(ApiResponse<LoyaltySettings>.SuccessResponse(new LoyaltySettings { IsEnabled = false }));

        var result = await _service.CompleteAsync(1, 5, "store_credit");

        result.Success.Should().BeTrue();
        capturedItemUpdate.Should().NotBeNull();
        capturedItemUpdate!.InventoryItemId.Should().Be(4242);
        _tradeInRepoMock.Verify(r => r.UpdateItemAsync(It.Is<TradeInItem>(i => i.InventoryItemId == 4242)), Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_InventoryCreationFails_DoesNotMarkTradeInCompleted()
    {
        var items = new List<TradeInItem>
        {
            new() { Id = 1, TradeInId = 1, GameTitle = "Mario", Platform = "NES", Condition = "good", OfferedValue = 15m, AcceptedValue = 15m },
        };
        var draft = new TradeIn { Id = 1, CompanyId = 5, Status = "draft", PaymentType = "cash", Items = items };

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(draft);
        _inventoryRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<InventoryItem>()))
            .ThrowsAsync(new Exception("23505: duplicate key value violates unique constraint \"ix_inventory_company_game_condition\""));

        var result = await _service.CompleteAsync(1, 5, "cash");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Failed to complete trade-in");
        _tradeInRepoMock.Verify(r => r.CompleteAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        _tradeInRepoMock.Verify(r => r.UpdateItemAsync(It.IsAny<TradeInItem>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_NoLocationsForCompany_ReturnsErrorAndDoesNotCreateInventory()
    {
        var items = new List<TradeInItem>
        {
            new() { Id = 1, TradeInId = 1, GameTitle = "Mario", Platform = "NES", Condition = "good", OfferedValue = 15m, AcceptedValue = 15m },
        };
        var draft = new TradeIn { Id = 1, CompanyId = 5, Status = "draft", PaymentType = "cash", Items = items };
        var completed = new TradeIn { Id = 1, CompanyId = 5, Status = "completed", Items = items };

        _tradeInRepoMock.Setup(r => r.GetByIdAsync(1, 5)).ReturnsAsync(draft);
        _tradeInRepoMock.Setup(r => r.CompleteAsync(1, "cash", It.IsAny<DateTime>())).ReturnsAsync(completed);
        _locationRepoMock.Setup(r => r.GetByCompanyIdAsync(5)).ReturnsAsync(new List<Location>());

        var result = await _service.CompleteAsync(1, 5, "cash");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("location");
        _inventoryRepoMock.Verify(r => r.CreateAsync(It.IsAny<InventoryItem>()), Times.Never);
    }
}
