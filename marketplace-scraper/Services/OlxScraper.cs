using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using marketplace_scraper.Data;
using marketplace_scraper.Models;
using Microsoft.EntityFrameworkCore;

namespace marketplace_scraper.Services;

public class OlxScraper(
    IHttpClientFactory httpClientFactory,
    AppDbContext db,
    IConfiguration configuration,
    ILogger<OlxScraper> logger) : IMarketplaceScraper, IDebugScraper
{
    public string Source => "olx";

    public async Task<int> ScrapeAsync(CancellationToken ct = default)
    {
        var searchUrl = configuration["Scraper:Sources:Olx:SearchUrl"]
            ?? throw new InvalidOperationException("Scraper:Sources:Olx:SearchUrl not configured");

        var run = new ScrapeRun { Source = Source, StartedAt = DateTime.UtcNow };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync(ct);

        try
        {
            var listings = await FetchAllListingsAsync(searchUrl, ct);
            var (found, newCount) = await UpsertListingsAsync(listings, ct);

            run.CompletedAt = DateTime.UtcNow;
            run.ListingsFound = found;
            run.NewListingsCount = newCount;
            run.Success = true;
            await db.SaveChangesAsync(ct);

            logger.LogInformation("OLX scrape complete: {Found} listings, {New} new", found, newCount);
            return found;
        }
        catch (Exception ex)
        {
            run.CompletedAt = DateTime.UtcNow;
            run.ErrorMessage = ex.Message;
            run.Success = false;
            await db.SaveChangesAsync(ct);
            logger.LogError(ex, "OLX scrape failed");
            throw;
        }
    }

    public async Task<DebugScrapeResult> DebugAsync(string wwwrootPath, CancellationToken ct = default)
    {
        var searchUrl = configuration["Scraper:Sources:Olx:SearchUrl"]
            ?? throw new InvalidOperationException("Scraper:Sources:Olx:SearchUrl not configured");

        var client = httpClientFactory.CreateClient("OlxScraper");
        string html;
        try
        {
            html = await client.GetStringAsync(searchUrl, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "OLX debug fetch failed");
            html = "";
        }

        var htmlPath = Path.Combine(wwwrootPath, "debug-olx.html");
        await File.WriteAllTextAsync(htmlPath, html, ct);
        logger.LogInformation("OLX debug HTML saved ({Length} chars) to {Path}", html.Length, htmlPath);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var cards = doc.DocumentNode.SelectNodes("//div[@data-cy='l-card'] | //li[@data-cy='l-card']");
        int cardCount = cards?.Count ?? 0;
        logger.LogInformation("OLX debug: {Count} cards matched selector", cardCount);

        return new DebugScrapeResult(Source, html.Length, cardCount, "/debug-olx.html");
    }

    private async Task<List<Listing>> FetchAllListingsAsync(string searchUrl, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("OlxScraper");
        var all = new List<Listing>();
        string? url = searchUrl;

        while (!string.IsNullOrEmpty(url))
        {
            logger.LogDebug("Fetching OLX page: {Url}", url);
            string html;
            try
            {
                html = await client.GetStringAsync(url, ct);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "HTTP error fetching {Url}", url);
                break;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var cards = doc.DocumentNode.SelectNodes(
                "//div[@data-cy='l-card'] | //li[@data-cy='l-card']");

            if (cards == null || cards.Count == 0)
            {
                logger.LogDebug("No cards found on page, stopping pagination");
                break;
            }

            foreach (HtmlNode card in cards)
            {
                var listing = ParseCard(card);
                if (listing != null) all.Add(listing);
            }

            url = GetNextPageUrl(doc);
        }

        return all;
    }

    private Listing? ParseCard(HtmlNode card)
    {
        try
        {
            var link = card.SelectSingleNode(".//a[@href]");
            if (link == null) return null;

            var href = link.GetAttributeValue("href", "");
            if (!href.Contains("/ad/") && !href.Contains("/obiava/")) return null;

            var fullUrl = href.StartsWith("http") ? href : "https://www.olx.bg" + href;
            var externalId = ExtractExternalId(href);
            if (string.IsNullOrEmpty(externalId)) return null;

            var title = (card.SelectSingleNode(".//h6")
                      ?? card.SelectSingleNode(".//h4")
                      ?? card.SelectSingleNode(".//h3"))?.InnerText.Trim();
            if (string.IsNullOrEmpty(title)) return null;

            var priceText = (card.SelectSingleNode(".//*[@data-testid='ad-price']")
                          ?? card.SelectSingleNode(".//*[contains(@class,'price')]"))?.InnerText.Trim() ?? "";

            var (priceBgn, priceEur, isNegotiable) = ParsePrice(priceText);

            var locationDateText = (card.SelectSingleNode(".//*[@data-testid='ad-featured-details']")
                                 ?? card.SelectSingleNode(".//*[@data-testid='location-date']"))?.InnerText.Trim() ?? "";

            var (location, publishedAt) = ParseLocationDate(locationDateText);

            var thumbnailUrl = card.SelectSingleNode(".//img[@src]")?.GetAttributeValue("src", null)
                            ?? card.SelectSingleNode(".//img[@data-src]")?.GetAttributeValue("data-src", null);

            var now = DateTime.UtcNow;
            return new Listing
            {
                ExternalId = externalId,
                Source = Source,
                Title = HtmlEntity.DeEntitize(title),
                PriceBgn = priceBgn,
                PriceEur = priceEur,
                IsNegotiable = isNegotiable,
                Location = location,
                PublishedAt = publishedAt,
                Url = fullUrl,
                ThumbnailUrl = thumbnailUrl,
                FirstSeenAt = now,
                LastSeenAt = now
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse OLX card");
            return null;
        }
    }

    private static string ExtractExternalId(string href)
    {
        var match = Regex.Match(href, @"/(ID[^/\.\?]+)");
        return match.Success ? match.Groups[1].Value : "";
    }

    private static (decimal? bgn, decimal? eur, bool negotiable) ParsePrice(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null, false);

        bool negotiable = text.Contains("Договаряне", StringComparison.OrdinalIgnoreCase);

        var numMatch = Regex.Match(text, @"(\d[\d\s]*(?:[,\.]\d+)?)");
        if (!numMatch.Success) return (null, null, negotiable);

        var numStr = numMatch.Value.Replace(" ", "").Replace(",", ".");
        if (!decimal.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
            return (null, null, negotiable);

        if (text.Contains("лв") || text.Contains("BGN", StringComparison.OrdinalIgnoreCase))
            return (amount, null, negotiable);
        if (text.Contains("€") || text.Contains("EUR", StringComparison.OrdinalIgnoreCase))
            return (null, amount, negotiable);

        return (amount, null, negotiable);
    }

    private static (string? location, DateTime? publishedAt) ParseLocationDate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);

        var parts = text.Split('-', 2);
        var location = parts.Length > 1 ? parts[0].Trim() : null;
        var datePart = parts.Length > 1 ? parts[1].Trim() : text.Trim();

        return (string.IsNullOrEmpty(location) ? null : location, ParseBulgarianDate(datePart));
    }

    private static DateTime? ParseBulgarianDate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var now = DateTime.UtcNow;

        if (text.Contains("Днес", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Today", StringComparison.OrdinalIgnoreCase))
        {
            var m = Regex.Match(text, @"(\d{1,2}):(\d{2})");
            return m.Success
                ? now.Date.AddHours(int.Parse(m.Groups[1].Value)).AddMinutes(int.Parse(m.Groups[2].Value))
                : now.Date;
        }

        if (text.Contains("Вчера", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Yesterday", StringComparison.OrdinalIgnoreCase))
        {
            var m = Regex.Match(text, @"(\d{1,2}):(\d{2})");
            return m.Success
                ? now.Date.AddDays(-1).AddHours(int.Parse(m.Groups[1].Value)).AddMinutes(int.Parse(m.Groups[2].Value))
                : now.Date.AddDays(-1);
        }

        var bulgarianMonths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["януари"] = 1, ["февруари"] = 2, ["март"] = 3, ["април"] = 4,
            ["май"] = 5, ["юни"] = 6, ["юли"] = 7, ["август"] = 8,
            ["септември"] = 9, ["октомври"] = 10, ["ноември"] = 11, ["декември"] = 12
        };

        foreach (var (name, month) in bulgarianMonths)
        {
            if (!text.Contains(name, StringComparison.OrdinalIgnoreCase)) continue;

            var dayMatch = Regex.Match(text, @"(\d{1,2})");
            var yearMatch = Regex.Match(text, @"(\d{4})");
            if (!dayMatch.Success) break;

            int day = int.Parse(dayMatch.Value);
            int year = yearMatch.Success ? int.Parse(yearMatch.Value) : now.Year;

            try { return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc); }
            catch { break; }
        }

        return null;
    }

    private static string? GetNextPageUrl(HtmlDocument doc)
    {
        var nextLink = doc.DocumentNode.SelectSingleNode("//a[@data-cy='pagination-forward']")
                    ?? doc.DocumentNode.SelectSingleNode("//a[@data-cy='next-page']")
                    ?? doc.DocumentNode.SelectSingleNode("//a[contains(@class,'pagination-next')]");

        if (nextLink == null) return null;

        var href = nextLink.GetAttributeValue("href", "");
        if (string.IsNullOrEmpty(href)) return null;

        return href.StartsWith("http") ? href : "https://www.olx.bg" + href;
    }

    private async Task<(int found, int newCount)> UpsertListingsAsync(List<Listing> scrapedListings, CancellationToken ct)
    {
        if (scrapedListings.Count == 0) return (0, 0);

        var externalIds = scrapedListings.Select(l => l.ExternalId).Distinct().ToList();
        var existing = await db.Listings
            .Where(l => l.Source == Source && externalIds.Contains(l.ExternalId))
            .ToDictionaryAsync(l => l.ExternalId, ct);

        var now = DateTime.UtcNow;
        int newCount = 0;

        foreach (var item in scrapedListings)
        {
            if (existing.TryGetValue(item.ExternalId, out var existingListing))
            {
                existingListing.Title = item.Title;
                existingListing.PriceBgn = item.PriceBgn;
                existingListing.PriceEur = item.PriceEur;
                existingListing.IsNegotiable = item.IsNegotiable;
                existingListing.Location = item.Location;
                existingListing.ThumbnailUrl = item.ThumbnailUrl;
                existingListing.LastSeenAt = now;
                existingListing.IsNew = false;
                existingListing.IsActive = true;
            }
            else
            {
                item.FirstSeenAt = now;
                item.LastSeenAt = now;
                item.IsNew = true;
                item.IsActive = true;
                db.Listings.Add(item);
                newCount++;
            }
        }

        // Deactivate listings not seen in this scrape
        var scrapedIds = scrapedListings.Select(l => l.ExternalId).ToHashSet();
        var toDeactivate = await db.Listings
            .Where(l => l.Source == Source && l.IsActive && !scrapedIds.Contains(l.ExternalId))
            .ToListAsync(ct);
        foreach (var l in toDeactivate) l.IsActive = false;

        await db.SaveChangesAsync(ct);
        return (scrapedListings.Count, newCount);
    }
}
