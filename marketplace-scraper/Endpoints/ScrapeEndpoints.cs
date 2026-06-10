using marketplace_scraper.Data;
using marketplace_scraper.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;

namespace marketplace_scraper.Endpoints;

public static class ScrapeEndpoints
{
    public static void MapScrapeEndpoints(this WebApplication app)
    {
        app.MapPost("/api/scrape/run", async (
            IEnumerable<IMarketplaceScraper> scrapers,
            string? source) =>
        {
            IEnumerable<IMarketplaceScraper> targets = string.IsNullOrEmpty(source)
                ? scrapers
                : scrapers.Where(s => s.Source == source.ToLower());

            var results = new List<object>();
            foreach (IMarketplaceScraper scraper in targets)
            {
                try
                {
                    int count = await scraper.ScrapeAsync();
                    results.Add(new { source = scraper.Source, success = true, listingsFound = count });
                }
                catch (Exception ex)
                {
                    results.Add(new { source = scraper.Source, success = false, error = ex.Message });
                }
            }

            return Results.Ok(results);
        });

        app.MapGet("/api/debug/scrape", async (
            IEnumerable<IDebugScraper> debugScrapers,
            IWebHostEnvironment env) =>
        {
            var wwwrootPath = env.WebRootPath;
            var results = new List<object>();
            foreach (IDebugScraper scraper in debugScrapers)
            {
                try
                {
                    DebugScrapeResult result = await scraper.DebugAsync(wwwrootPath);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    results.Add(new { source = scraper.Source, error = ex.Message });
                }
            }
            return Results.Ok(results);
        });

        app.MapGet("/api/scrape/status", async (AppDbContext db) =>
        {
            var runs = await db.ScrapeRuns
                .OrderByDescending(r => r.StartedAt)
                .Take(10)
                .ToListAsync();

            return Results.Ok(runs);
        });
    }
}
