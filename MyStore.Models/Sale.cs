namespace MyStore.Models;

public class Sale
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? UserId { get; set; }
    public User? User { get; set; }
    public List<SaleItem> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TaxRate { get; set; }
    public string? TaxLabel { get; set; }
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
    public int? UserId { get; set; }
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

public class ReceiptLineItem
{
    public string Name { get; set; } = string.Empty;
    public int Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public class ReceiptResponse
{
    public string ReceiptNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string? StoreAddress { get; set; }
    public string? StorePhone { get; set; }
    public List<ReceiptLineItem> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public string? TaxLabel { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? EmployeeName { get; set; }
}

public class SendReceiptEmailRequest
{
    public string Email { get; set; } = string.Empty;
}

