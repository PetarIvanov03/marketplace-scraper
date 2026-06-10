namespace marketplace_scraper.Models;

public class ScrapeRun
{
    public int Id { get; set; }
    public required string Source { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int ListingsFound { get; set; }
    public int NewListingsCount { get; set; }
    public string? ErrorMessage { get; set; }
    public bool Success { get; set; }
}
