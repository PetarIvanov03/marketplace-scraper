using System.Globalization;
using System.Text.RegularExpressions;
using marketplace_scraper.Data;
using marketplace_scraper.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace marketplace_scraper.Services;

public class BazarScraper(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<BazarScraper> logger) : IMarketplaceScraper, IDebugScraper
{
    private const decimal EurToBgnRate = 1.956m;

    public string Source => "bazar";

    public async Task<int> ScrapeAsync(CancellationToken ct = default)
    {
        var searchUrl = configuration["Scraper:Sources:Bazar:SearchUrl"]
            ?? throw new InvalidOperationException("Scraper:Sources:Bazar:SearchUrl not configured");

        var run = new ScrapeRun { Source = Source, StartedAt = DateTime.UtcNow };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync(ct);

        try
        {
            var listings = await FetchListingsAsync(searchUrl);
            var (found, newCount) = await UpsertListingsAsync(listings, ct);

            run.CompletedAt = DateTime.UtcNow;
            run.ListingsFound = found;
            run.NewListingsCount = newCount;
            run.Success = true;
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Bazar scrape complete: {Found} listings, {New} new", found, newCount);
            return found;
        }
        catch (Exception ex)
        {
            run.CompletedAt = DateTime.UtcNow;
            run.ErrorMessage = ex.Message;
            run.Success = false;
            await db.SaveChangesAsync(ct);
            logger.LogError(ex, "Bazar scrape failed");
            throw;
        }
    }

    public async Task<DebugScrapeResult> DebugAsync(string wwwrootPath, CancellationToken ct = default)
    {
        var searchUrl = configuration["Scraper:Sources:Bazar:SearchUrl"]
            ?? throw new InvalidOperationException("Scraper:Sources:Bazar:SearchUrl not configured");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();

        await page.GotoAsync(searchUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 30_000
        });

        await Task.Delay(3000, ct);

        var screenshotPath = Path.Combine(wwwrootPath, "debug-bazar.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
        logger.LogInformation("Bazar debug screenshot saved to {Path}", screenshotPath);

        var html = await page.ContentAsync();
        var htmlPath = Path.Combine(wwwrootPath, "debug-bazar.html");
        await File.WriteAllTextAsync(htmlPath, html, ct);
        logger.LogInformation("Bazar debug HTML saved ({Length} chars) to {Path}", html.Length, htmlPath);

        var anchors = await page.QuerySelectorAllAsync("a[href*='/obiava-']");
        int cardCount = anchors.Count;
        logger.LogInformation("Bazar debug: {Count} anchors matched selector", cardCount);

        return new DebugScrapeResult(Source, html.Length, cardCount, "/debug-bazar.html", "/debug-bazar.png");
    }

    private async Task<List<Listing>> FetchListingsAsync(string searchUrl)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });

        var page = await browser.NewPageAsync();

        await page.GotoAsync(searchUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 30_000
        });

        try
        {
            await page.WaitForSelectorAsync("a[href*='/obiava-'] div.gallery-item",
                new PageWaitForSelectorOptions { Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Timeout waiting for bazar.bg listing cards at {Url}", searchUrl);
        }

        var anchors = await page.QuerySelectorAllAsync("a[href*='/obiava-']");
        logger.LogDebug("Bazar.bg: found {Count} listing anchors", anchors.Count);

        var listings = new List<Listing>();
        var now = DateTime.UtcNow;

        foreach (var anchor in anchors)
        {
            try
            {
                var listing = await ParseCardAsync(anchor, now);
                if (listing != null) listings.Add(listing);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse bazar.bg card");
            }
        }

        return listings;
    }

    private async Task<Listing?> ParseCardAsync(IElementHandle anchor, DateTime now)
    {
        var href = await anchor.GetAttributeAsync("href");
        if (string.IsNullOrEmpty(href)) return null;

        var externalId = ExtractExternalId(href);
        if (string.IsNullOrEmpty(externalId)) return null;

        var fullUrl = href.StartsWith("http") ? href : "https://bazar.bg" + href;

        var titleEl = await anchor.QuerySelectorAsync("div.info h3");
        if (titleEl == null) return null;
        var title = (await titleEl.InnerTextAsync()).Trim();
        if (string.IsNullOrWhiteSpace(title)) return null;

        var priceEl = await anchor.QuerySelectorAsync("p.price");
        var priceText = priceEl != null ? await priceEl.InnerTextAsync() : "";
        var (priceBgn, priceEur) = ParsePrice(priceText);

        var locationEl = await anchor.QuerySelectorAsync("p.location");
        var location = locationEl != null ? (await locationEl.InnerTextAsync()).Trim() : null;

        var dateEl = await anchor.QuerySelectorAsync("p.date");
        var dateText = dateEl != null ? (await dateEl.InnerTextAsync()).Trim() : "";
        var publishedAt = ParseBazarDate(dateText);

        var imgEl = await anchor.QuerySelectorAsync("img.gallery-image");
        string? thumbnailUrl = null;
        if (imgEl != null)
        {
            var src = await imgEl.GetAttributeAsync("src");
            if (!string.IsNullOrEmpty(src) && !src.Contains("noPhoto"))
                thumbnailUrl = src.StartsWith("http") ? src : "https://bazar.bg" + src;
        }

        return new Listing
        {
            ExternalId = externalId,
            Source = Source,
            Title = title,
            PriceBgn = priceBgn,
            PriceEur = priceEur,
            IsNegotiable = false,
            Location = string.IsNullOrWhiteSpace(location) ? null : location,
            PublishedAt = publishedAt,
            Url = fullUrl,
            ThumbnailUrl = thumbnailUrl,
            FirstSeenAt = now,
            LastSeenAt = now
        };
    }

    private static string ExtractExternalId(string href)
    {
        var match = Regex.Match(href, @"/obiava-(\d+)/");
        return match.Success ? match.Groups[1].Value : "";
    }

    private static (decimal? bgn, decimal? eur) ParsePrice(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);

        var numMatch = Regex.Match(text, @"(\d+(?:[.,]\d+)?)");
        if (!numMatch.Success) return (null, null);

        var numStr = numMatch.Value.Replace(",", ".");
        if (!decimal.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
            return (null, null);

        if (text.Contains('€'))
            return (Math.Round(amount * EurToBgnRate, 2), amount);

        return (amount, null);
    }

    private static DateTime? ParseBazarDate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var now = DateTime.UtcNow;

        if (text.StartsWith("днес", StringComparison.OrdinalIgnoreCase))
        {
            var m = Regex.Match(text, @"(\d{1,2}):(\d{2})");
            return m.Success
                ? now.Date.AddHours(int.Parse(m.Groups[1].Value)).AddMinutes(int.Parse(m.Groups[2].Value))
                : now.Date;
        }

        if (text.StartsWith("вчера", StringComparison.OrdinalIgnoreCase))
        {
            var m = Regex.Match(text, @"(\d{1,2}):(\d{2})");
            return m.Success
                ? now.Date.AddDays(-1).AddHours(int.Parse(m.Groups[1].Value)).AddMinutes(int.Parse(m.Groups[2].Value))
                : now.Date.AddDays(-1);
        }

        var months = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["януари"] = 1, ["февруари"] = 2, ["март"] = 3, ["април"] = 4,
            ["май"] = 5, ["юни"] = 6, ["юли"] = 7, ["август"] = 8,
            ["септември"] = 9, ["октомври"] = 10, ["ноември"] = 11, ["декември"] = 12
        };

        foreach (var (name, month) in months)
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

        var scrapedIds = scrapedListings.Select(l => l.ExternalId).ToHashSet();
        var toDeactivate = await db.Listings
            .Where(l => l.Source == Source && l.IsActive && !scrapedIds.Contains(l.ExternalId))
            .ToListAsync(ct);
        foreach (var l in toDeactivate) l.IsActive = false;

        await db.SaveChangesAsync(ct);
        return (scrapedListings.Count, newCount);
    }
}
