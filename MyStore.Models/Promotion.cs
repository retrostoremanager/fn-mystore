namespace MyStore.Models;

public class Promotion
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal? DiscountPercent { get; set; }
    public int? BuyQuantity { get; set; }
    public int? GetQuantity { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string? ScopeValue { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreatePromotionRequest
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal? DiscountPercent { get; set; }
    public int? BuyQuantity { get; set; }
    public int? GetQuantity { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string? ScopeValue { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public int CreatedBy { get; set; }
}

public class UpdatePromotionRequest
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public decimal? DiscountPercent { get; set; }
    public int? BuyQuantity { get; set; }
    public int? GetQuantity { get; set; }
    public string? Scope { get; set; }
    public string? ScopeValue { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool? IsActive { get; set; }
}

public class LineDiscount
{
    public int ItemId { get; set; }
    public decimal DiscountAmount { get; set; }
}

public class CartItem
{
    public int InventoryItemId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Category { get; set; }
}
