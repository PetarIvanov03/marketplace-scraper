namespace marketplace_scraper.Services;

public class ScraperBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ScraperBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = configuration.GetValue<int>("Scraper:IntervalMinutes", 30);
        logger.LogInformation("Scraper background service started. Interval: {Interval} min", intervalMinutes);

        await RunScrapersAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunScrapersAsync(stoppingToken);
        }
    }

    private async Task RunScrapersAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var scrapers = scope.ServiceProvider.GetServices<IMarketplaceScraper>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        foreach (var scraper in scrapers)
        {
            var enabledKey = $"Scraper:Sources:{ToPascalCase(scraper.Source)}:Enabled";
            if (!config.GetValue<bool>(enabledKey, true))
            {
                logger.LogDebug("Scraper {Source} disabled, skipping", scraper.Source);
                continue;
            }

            try
            {
                await scraper.ScrapeAsync(ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scraper {Source} failed", scraper.Source);
            }
        }
    }

    private static string ToPascalCase(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
