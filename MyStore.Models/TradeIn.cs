namespace MyStore.Models;

public class ParseImageRequest
{
    public string ImageBase64 { get; set; } = string.Empty;
    public string MimeType { get; set; } = "image/jpeg";
}

public class ParsedTradeInItem
{
    public string GameTitle { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public decimal? OfferedValue { get; set; }
}

public class ParseImageResult
{
    public List<ParsedTradeInItem> Items { get; set; } = new();
}

public class TradeIn
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int? CustomerId { get; set; }
    public string Status { get; set; } = "draft";
    public decimal TotalOfferedValue { get; set; }
    public decimal? TotalAcceptedValue { get; set; }
    public string? PaymentType { get; set; }
    public string? Notes { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<TradeInItem> Items { get; set; } = new();
}

public class TradeInItem
{
    public int Id { get; set; }
    public int TradeInId { get; set; }
    public string GameTitle { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public decimal OfferedValue { get; set; }
    public decimal? AcceptedValue { get; set; }
    public int? InventoryItemId { get; set; }
    public bool ParsedByAi { get; set; }
    public DateTime CreatedAt { get; set; }
}
