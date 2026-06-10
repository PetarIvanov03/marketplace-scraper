# marketplace-scraper — Claude Code Context

## What this project does
A local web application that scrapes bicycle listings from **OLX.bg** and **Bazar.bg**,
stores them in MS SQL, and shows only new listings (or listings published in the last X hours)
via a simple browser UI.

---

## Tech stack
| Layer | Technology |
|---|---|
| Backend | ASP.NET Core 8 — Minimal API |
| Scraping (static HTML) | `HttpClient` + `HtmlAgilityPack` |
| Scraping (JS-rendered) | `Microsoft.Playwright` |
| ORM | Entity Framework Core 8 |
| Database | MS SQL Server (local) |
| Frontend | Vanilla HTML + CSS + JavaScript (served as static files) |
| Background jobs | `IHostedService` with a configurable timer |

---

## Project structure
```
marketplace-scraper/
├── Program.cs                        # App entry point, DI, middleware, endpoint registration
├── appsettings.json                  # Base config (scrape interval, search URLs)
├── appsettings.Development.json      # Local connection string (not committed)
├── Models/
│   ├── Listing.cs                    # Main entity
│   └── ScrapeRun.cs                  # Scrape history/log entity
├── Data/
│   └── AppDbContext.cs               # EF Core DbContext
├── Services/
│   ├── IMarketplaceScraper.cs        # Common scraper interface
│   ├── OlxScraper.cs                 # OLX.bg scraper (HttpClient + HtmlAgilityPack)
│   ├── BazarScraper.cs               # Bazar.bg scraper (Playwright)
│   └── ScraperBackgroundService.cs   # IHostedService — runs scrapers on a timer
├── Endpoints/
│   ├── ListingEndpoints.cs           # GET /api/listings, GET /api/listings/{id}
│   └── ScrapeEndpoints.cs            # POST /api/scrape/run, GET /api/scrape/status
└── wwwroot/
    ├── index.html
    ├── css/style.css
    └── js/app.js
```

---

## NuGet packages
```xml
<PackageReference Include="HtmlAgilityPack" Version="1.11.*" />
<PackageReference Include="Microsoft.Playwright" Version="1.44.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="8.*" />
```

---

## Database schema
Full schema in `docs/SCHEMA.md`.

### Listings (главната таблица)
```
Id               int IDENTITY PK
ExternalId       nvarchar(100)     -- ID от сайта-източник
Source           nvarchar(10)      -- 'olx' | 'bazar'
Title            nvarchar(500)
PriceBgn         decimal(10,2)?
PriceEur         decimal(10,2)?
IsNegotiable     bit
Condition        nvarchar(20)      -- 'new' | 'used'
Location         nvarchar(200)
PublishedAt      datetime2?        -- кога е публикувана обявата
Url              nvarchar(1000)
ThumbnailUrl     nvarchar(1000)?
IsNew            bit               -- дали е видяна за първи път при последния scrape
FirstSeenAt      datetime2         -- кога нашият scraper я е открил за първи път
LastSeenAt       datetime2         -- кога последно сме я видели
IsActive         bit               -- дали е все още активна (default: true)
```

Unique constraint: `(ExternalId, Source)` — предотвратява дубликати.

### ScrapeRuns (лог на всеки scrape)
```
Id               int IDENTITY PK
Source           nvarchar(10)      -- 'olx' | 'bazar'
StartedAt        datetime2
CompletedAt      datetime2?
ListingsFound    int
NewListingsCount int
ErrorMessage     nvarchar(2000)?
Success          bit
```

---

## API endpoints
```
GET  /api/listings                  -- всички активни обяви
     ?source=olx|bazar              -- филтър по сайт
     ?isNew=true                    -- само нови
     ?hours=24                      -- публикувани в последните N часа
     ?maxPrice=200                  -- максимална цена в лв
GET  /api/listings/{id}             -- конкретна обява

POST /api/scrape/run                -- ръчен scrape (и двата сайта или ?source=olx)
GET  /api/scrape/status             -- последните N ScrapeRun записа
```

---

## Scraping strategy

### OLX.bg
- Renderer: **HttpClient** (статичен HTML)
- Search URL: `https://www.olx.bg/sport-knigi-hobi/sportni-stoki/velosipedi/?search[order]=created_at:desc&search[filter_float_price:to]=200`
- HTML parsing: **HtmlAgilityPack**
- External ID: извлича се от URL-а на обявата — частта `ID[a-zA-Z0-9]+` след `CID618-`
- Дата: текстово поле — "Днес в 07:20 ч." / "08 юни 2026 г." / "Обновено на X" — парсва се до `datetime2`

### Bazar.bg
- Renderer: **Playwright** (headless Chromium) — страницата с резултати е JS-rendered
- Search URL: `https://bazar.bg/obiavi/velosipedi?price_to=200&sort=date`
- External ID: числото в URL-а на обявата — `obiava-{ID}/`
- Дата: текстово поле — "днес в 11:12 ч." / "вчера в 09:00 ч." / "03 юни"

### Обща логика (в двата скрейпъра)
1. Fetch страницата с резултати
2. Парсни всички обяви до `List<ListingDto>`
3. За всяка обява — провери дали `(ExternalId, Source)` вече съществува в БД
   - Ако не съществува → Insert с `IsNew = true`, `FirstSeenAt = now`
   - Ако съществува → Update `LastSeenAt`, `IsActive = true`, `IsNew = false`
4. Обяви, невидени при последния scrape → `IsActive = false`
5. Запиши `ScrapeRun` запис

---

## appsettings.json structure
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MarketplaceScraper;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "Scraper": {
    "IntervalMinutes": 30,
    "Sources": {
      "Olx": {
        "Enabled": true,
        "SearchUrl": "https://www.olx.bg/sport-knigi-hobi/sportni-stoki/velosipedi/?search[order]=created_at:desc&search[filter_float_price:to]=200"
      },
      "Bazar": {
        "Enabled": true,
        "SearchUrl": "https://bazar.bg/obiavi/velosipedi?price_to=200&sort=date"
      }
    }
  }
}
```

---

## Coding conventions
- **Async/await** навсякъде — всички I/O операции са async
- **Dependency injection** — инжектирай IConfiguration, AppDbContext, ILogger навсякъде
- **Minimal API** — без контролери; endpoints се регистрират в `Endpoints/*.cs` като extension methods върху `WebApplication`
- **EF Core** — използвай `async` методи (`ToListAsync`, `FirstOrDefaultAsync`, `SaveChangesAsync`)
- **Не използвай** `var` прекомерно — пиши типа където помага на четимостта
- **Именуване**: PascalCase за класове и методи, camelCase за локални променливи
- **Logging**: използвай `ILogger<T>` — не `Console.WriteLine`

---

## Local dev setup
```bash
# 1. Build first (generates playwright.ps1), then install Playwright browsers (once)
dotnet build
pwsh bin/Debug/net9.0/playwright.ps1 install chromium

# 2. Migrations
dotnet ef migrations add InitialCreate
dotnet ef database update

# 3. Стартирай
dotnet run

# Приложението е на http://localhost:5000
```

---

## Important notes
- `appsettings.Development.json` **не се commit-ва** — съдържа connection string
- Playwright изисква инсталирани браузъри (`playwright install chromium`) — трябва да се направи веднъж след клониране
- OLX показва и "промотирани" обяви (повтарят се на страницата) — дедупликацията по `ExternalId` ги елиминира автоматично
- Датата от OLX е на **български** — "януари", "февруари" и т.н. — парсването трябва да ги поддържа
