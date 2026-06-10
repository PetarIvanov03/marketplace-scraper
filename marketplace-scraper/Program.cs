using marketplace_scraper.Data;
using marketplace_scraper.Endpoints;
using marketplace_scraper.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient("OlxScraper", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddScoped<IMarketplaceScraper, OlxScraper>();
builder.Services.AddScoped<IMarketplaceScraper, BazarScraper>();
builder.Services.AddScoped<IDebugScraper, OlxScraper>();
builder.Services.AddScoped<IDebugScraper, BazarScraper>();
builder.Services.AddHostedService<ScraperBackgroundService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapListingEndpoints();
app.MapScrapeEndpoints();

app.Run();
