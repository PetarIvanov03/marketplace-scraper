namespace marketplace_scraper.Services;

public record DebugScrapeResult(
    string Source,
    int HtmlLength,
    int MatchedCards,
    string RawHtmlSavedTo,
    string? ScreenshotSavedTo = null
);

public interface IDebugScraper
{
    string Source { get; }
    Task<DebugScrapeResult> DebugAsync(string wwwrootPath, CancellationToken ct = default);
}
