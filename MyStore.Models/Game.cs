namespace MyStore.Models;

public class Game
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Console { get; set; } = string.Empty;
    public DateTime? ReleaseDate { get; set; }
    public string? Publisher { get; set; }
    public string? Genre { get; set; }
    public string? ImageUrl { get; set; }
}

public class MarketPrices
{
    public decimal? Loose { get; set; }
    public decimal? Complete { get; set; }
    public decimal? New { get; set; }
    public decimal? Cib { get; set; } // Complete in Box
}

