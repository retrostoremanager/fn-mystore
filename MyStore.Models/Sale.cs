namespace MyStore.Models;

public class Sale
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public List<SaleItem> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public DateTime SaleDate { get; set; }
    public string? Notes { get; set; }
}

public class SaleItem
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public int InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
}

public class CreateSaleRequest
{
    public int CustomerId { get; set; }
    public int? EmployeeId { get; set; }
    public List<CreateSaleItemRequest> Items { get; set; } = new();
    public decimal Tax { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class CreateSaleItemRequest
{
    public int InventoryItemId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

