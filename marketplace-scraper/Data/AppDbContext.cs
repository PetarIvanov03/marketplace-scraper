using marketplace_scraper.Models;
using Microsoft.EntityFrameworkCore;

namespace marketplace_scraper.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ScrapeRun> ScrapeRuns => Set<ScrapeRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Listing>()
            .HasIndex(l => new { l.ExternalId, l.Source })
            .IsUnique();

        modelBuilder.Entity<Listing>()
            .Property(l => l.PriceBgn)
            .HasPrecision(10, 2);

        modelBuilder.Entity<Listing>()
            .Property(l => l.PriceEur)
            .HasPrecision(10, 2);

        modelBuilder.Entity<Listing>().HasIndex(l => l.Source).HasDatabaseName("IX_Listings_Source");
        modelBuilder.Entity<Listing>().HasIndex(l => l.IsNew).HasDatabaseName("IX_Listings_IsNew");
        modelBuilder.Entity<Listing>().HasIndex(l => l.PublishedAt).IsDescending().HasDatabaseName("IX_Listings_PublishedAt");
        modelBuilder.Entity<Listing>().HasIndex(l => l.IsActive).HasDatabaseName("IX_Listings_IsActive");

        modelBuilder.Entity<ScrapeRun>().HasIndex(r => r.Source).HasDatabaseName("IX_ScrapeRuns_Source");
        modelBuilder.Entity<ScrapeRun>().HasIndex(r => r.StartedAt).IsDescending().HasDatabaseName("IX_ScrapeRuns_StartedAt");
    }
}
