# Database Schema

Database: `MarketplaceScraper` (MS SQL Server, local)

---

## Table: Listings

Главната таблица — всяка уникална обява от OLX или Bazar.

```sql
CREATE TABLE Listings (
    Id               INT IDENTITY(1,1) PRIMARY KEY,
    ExternalId       NVARCHAR(100)    NOT NULL,
    Source           NVARCHAR(10)     NOT NULL,   -- 'olx' | 'bazar'
    Title            NVARCHAR(500)    NOT NULL,
    PriceBgn         DECIMAL(10,2)    NULL,
    PriceEur         DECIMAL(10,2)    NULL,
    IsNegotiable     BIT              NOT NULL DEFAULT 0,
    Condition        NVARCHAR(20)     NULL,       -- 'new' | 'used' | NULL
    Location         NVARCHAR(200)    NULL,
    PublishedAt      DATETIME2        NULL,       -- кога е публикувана според сайта
    Url              NVARCHAR(1000)   NOT NULL,
    ThumbnailUrl     NVARCHAR(1000)   NULL,
    IsNew            BIT              NOT NULL DEFAULT 1,
    FirstSeenAt      DATETIME2        NOT NULL,   -- кога нашият scraper я е открил
    LastSeenAt       DATETIME2        NOT NULL,   -- кога последно сме я виждали
    IsActive         BIT              NOT NULL DEFAULT 1,

    CONSTRAINT UQ_ExternalId_Source UNIQUE (ExternalId, Source)
);

CREATE INDEX IX_Listings_Source      ON Listings (Source);
CREATE INDEX IX_Listings_IsNew       ON Listings (IsNew);
CREATE INDEX IX_Listings_PublishedAt ON Listings (PublishedAt DESC);
CREATE INDEX IX_Listings_IsActive    ON Listings (IsActive);
```

### Бележки
- `ExternalId` — от URL-а на обявата:
  - OLX: `IDa1iT3` (частта след `CID618-`)
  - Bazar: `54483340` (числото от `obiava-54483340/`)
- `IsNew` се слага `true` само при INSERT (първо виждане); при следващи scrape-ове остава `false`
- `IsActive = false` когато обявата изчезне от резултатите (слага се след всеки успешен scrape)
- `PublishedAt` може да е `NULL` ако датата не може да бъде парсната

---

## Table: ScrapeRuns

Лог на всеки scrape — за дебъгване и статистика.

```sql
CREATE TABLE ScrapeRuns (
    Id                INT IDENTITY(1,1) PRIMARY KEY,
    Source            NVARCHAR(10)     NOT NULL,   -- 'olx' | 'bazar'
    StartedAt         DATETIME2        NOT NULL,
    CompletedAt       DATETIME2        NULL,
    ListingsFound     INT              NOT NULL DEFAULT 0,
    NewListingsCount  INT              NOT NULL DEFAULT 0,
    ErrorMessage      NVARCHAR(2000)   NULL,
    Success           BIT              NOT NULL DEFAULT 0
);

CREATE INDEX IX_ScrapeRuns_Source    ON ScrapeRuns (Source);
CREATE INDEX IX_ScrapeRuns_StartedAt ON ScrapeRuns (StartedAt DESC);
```

---

## EF Core модели (C#)

### Listing.cs
```csharp
public class Listing
{
    public int Id { get; set; }
    public required string ExternalId { get; set; }
    public required string Source { get; set; }      // "olx" | "bazar"
    public required string Title { get; set; }
    public decimal? PriceBgn { get; set; }
    public decimal? PriceEur { get; set; }
    public bool IsNegotiable { get; set; }
    public string? Condition { get; set; }           // "new" | "used"
    public string? Location { get; set; }
    public DateTime? PublishedAt { get; set; }
    public required string Url { get; set; }
    public string? ThumbnailUrl { get; set; }
    public bool IsNew { get; set; } = true;
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public bool IsActive { get; set; } = true;
}
```

### ScrapeRun.cs
```csharp
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
```

### AppDbContext.cs
```csharp
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
    }
}
```

---

## Migrations (команди)
```bash
# Създай първата миграция
dotnet ef migrations add InitialCreate --output-dir Data/Migrations

# Приложи към базата
dotnet ef database update

# Ако трябва reset
dotnet ef database drop --force
dotnet ef database update
```
