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
    private readonly Mock<ILoyaltyService> _loyaltyMock;
    private readonly TradeInService _service;
    private readonly TradeInService _serviceNoLoyalty;

    public TradeInServiceTests()
    {
        _tradeInRepoMock = new Mock<ITradeInRepository>();
        _inventoryRepoMock = new Mock<IInventoryRepository>();
        _loyaltyMock = new Mock<ILoyaltyService>();
        _service = new TradeInService(_tradeInRepoMock.Object, _inventoryRepoMock.Object, _loyaltyMock.Object);
        _serviceNoLoyalty = new TradeInService(_tradeInRepoMock.Object, _inventoryRepoMock.Object, null);
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
}
