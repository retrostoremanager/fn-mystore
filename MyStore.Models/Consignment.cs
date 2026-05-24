namespace MyStore.Models;

public class ConsignmentItem
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int CustomerId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal AskingPrice { get; set; }
    public decimal? SalePrice { get; set; }
    public decimal SplitPercent { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ConsignmentPayout
{
    public int Id { get; set; }
    public int ConsignmentItemId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }
    public string? Notes { get; set; }
}
