using FluentAssertions;
using Moq;
using MyStore.Models;
using MyStore.Repositories;
using MyStore.Services;
using Xunit;

namespace MyStore.Tests.Services;

public class InventoryServiceTests
{
    private readonly Mock<IInventoryRepository> _repositoryMock;
    private readonly InventoryService _service;

    public InventoryServiceTests()
    {
        _repositoryMock = new Mock<IInventoryRepository>();
        _service = new InventoryService(_repositoryMock.Object);
    }

    [Fact]
    public async Task CreateInventoryItemAsync_MissingLocationId_ReturnsError()
    {
        // Arrange - LocationId is 0 (not provided)
        var request = new CreateInventoryItemRequest
        {
            LocationId = 0,
            Name = "Test Item",
            Category = "Games",
            Quantity = 1,
            SellPrice = 29.99m,
            Condition = "Good",
            Completeness = new Completeness()
        };

        // Act
        var result = await _service.CreateInventoryItemAsync(request, companyId: 1);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Location");
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<InventoryItem>()), Times.Never);
    }

    [Fact]
    public async Task CreateInventoryItemAsync_ValidRequestWithLocationId_CreatesItem()
    {
        // Arrange
        var request = new CreateInventoryItemRequest
        {
            LocationId = 1,
            Name = "Test Item",
            Category = "Games",
            Quantity = 1,
            SellPrice = 29.99m,
            Condition = "Good",
            Completeness = new Completeness()
        };

        InventoryItem? capturedItem = null;
        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<InventoryItem>()))
            .Callback<InventoryItem>(i => capturedItem = i)
            .ReturnsAsync((InventoryItem i) => { i.Id = 1; return i; });

        // Act
        var result = await _service.CreateInventoryItemAsync(request, companyId: 1);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.LocationId.Should().Be(1);
        capturedItem.Should().NotBeNull();
        capturedItem!.LocationId.Should().Be(1);
        capturedItem.CompanyId.Should().Be(1);
    }
}
