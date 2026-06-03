namespace MyStore.Models;

public class LoyaltySettings
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public decimal PointsPerDollarSpent { get; set; } = 1m;
    public decimal PointsPerDollarTradeIn { get; set; } = 1m;
    public decimal RedemptionRate { get; set; } = 100m;
    public bool IsEnabled { get; set; }
}

public class LoyaltyTransaction
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int CustomerId { get; set; }
    public int Points { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public int? ReferenceId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class LoyaltyBalanceResponse
{
    public int CustomerId { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("pointsBalance")]
    public int Balance { get; set; }
    public List<LoyaltyTransaction> Transactions { get; set; } = new();
}

public class RedeemPointsRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("points")]
    public int PointsToRedeem { get; set; }
}

public class RedeemPointsResponse
{
    public int PointsRedeemed { get; set; }
    public decimal CreditAmount { get; set; }
    public int NewBalance { get; set; }
}
