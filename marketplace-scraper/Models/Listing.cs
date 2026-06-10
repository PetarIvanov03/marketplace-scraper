namespace marketplace_scraper.Models;

public class Listing
{
    public int Id { get; set; }
    public required string ExternalId { get; set; }
    public required string Source { get; set; }
    public required string Title { get; set; }
    public decimal? PriceBgn { get; set; }
    public decimal? PriceEur { get; set; }
    public bool IsNegotiable { get; set; }
    public string? Condition { get; set; }
    public string? Location { get; set; }
    public DateTime? PublishedAt { get; set; }
    public required string Url { get; set; }
    public string? ThumbnailUrl { get; set; }
    public bool IsNew { get; set; } = true;
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public bool IsActive { get; set; } = true;
}
