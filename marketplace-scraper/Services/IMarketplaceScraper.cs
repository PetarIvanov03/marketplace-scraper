namespace marketplace_scraper.Services;

public interface IMarketplaceScraper
{
    string Source { get; }
    Task<int> ScrapeAsync(CancellationToken ct = default);
}
