using marketplace_scraper.Data;
using Microsoft.EntityFrameworkCore;

namespace marketplace_scraper.Endpoints;

public static class ListingEndpoints
{
    public static void MapListingEndpoints(this WebApplication app)
    {
        app.MapGet("/api/listings", async (
            AppDbContext db,
            string? source,
            bool? isNew,
            int? hours,
            decimal? maxPrice) =>
        {
            var query = db.Listings.Where(l => l.IsActive).AsQueryable();

            if (!string.IsNullOrEmpty(source))
                query = query.Where(l => l.Source == source.ToLower());

            if (isNew == true)
                query = query.Where(l => l.IsNew);

            if (hours.HasValue)
            {
                DateTime cutoff = DateTime.UtcNow.AddHours(-hours.Value);
                query = query.Where(l => l.PublishedAt >= cutoff);
            }

            if (maxPrice.HasValue)
                query = query.Where(l => l.PriceBgn == null || l.PriceBgn <= maxPrice.Value);

            var listings = await query
                .OrderByDescending(l => l.FirstSeenAt)
                .ToListAsync();

            return Results.Ok(listings);
        });

        app.MapGet("/api/listings/{id:int}", async (int id, AppDbContext db) =>
        {
            var listing = await db.Listings.FindAsync(id);
            return listing is null ? Results.NotFound() : Results.Ok(listing);
        });
    }
}
