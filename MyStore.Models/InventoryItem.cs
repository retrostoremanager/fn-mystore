namespace MyStore.Models;

public class InventoryItem
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal SellPrice { get; set; }
    public decimal? BuyPrice { get; set; }
    public string Condition { get; set; } = string.Empty;
    public Game? Game { get; set; }
    public Completeness Completeness { get; set; } = new();
    public string? Notes { get; set; }
    public DateTime AddedDate { get; set; }
    public DateTime? LastModifiedDate { get; set; }
}

public class Completeness
{
    public bool Box { get; set; }
    public bool Instructions { get; set; }
    public bool Game { get; set; }
    public bool Inserts { get; set; }
    public bool Other { get; set; }
}

public class CreateInventoryItemRequest
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal SellPrice { get; set; }
    public decimal? BuyPrice { get; set; }
    public string Condition { get; set; } = string.Empty;
    public string? GameId { get; set; }
    public Completeness Completeness { get; set; } = new();
    public string? Notes { get; set; }
}

public class UpdateInventoryItemRequest
{
    public string? Name { get; set; }
    public string? Category { get; set; }
    public int? Quantity { get; set; }
    public decimal? SellPrice { get; set; }
    public decimal? BuyPrice { get; set; }
    public string? Condition { get; set; }
    public string? GameId { get; set; }
    public Completeness? Completeness { get; set; }
    public string? Notes { get; set; }
}

