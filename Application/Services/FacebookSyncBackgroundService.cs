namespace Crm_Api.Application.Services;

/// <summary>
/// Hosted background service that periodically runs FacebookSyncService.
/// Interval is controlled by appsettings.json → FacebookSync:IntervalMinutes (default 10).
/// </summary>
public class FacebookSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FacebookSyncBackgroundService> _logger;
    private readonly TimeSpan _interval;

    public FacebookSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<FacebookSyncBackgroundService> logger,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;

        var minutes = config.GetValue<int>("FacebookSync:IntervalMinutes", 10);
        _interval   = TimeSpan.FromMinutes(minutes);

        _logger.LogInformation(
            "FacebookSyncBackgroundService: will sync every {M} minutes.", minutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait 30 seconds after startup before first sync (let app fully initialise)
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var syncService = scope.ServiceProvider.GetRequiredService<FacebookSyncService>();
                var (added, deleted, skipped) = await syncService.RunAsync(stoppingToken);

                if (added > 0 || deleted > 0)
                    _logger.LogInformation(
                        "FB auto-sync done: +{A} new, {D} deleted, {S} skipped.",
                        added, deleted, skipped);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FacebookSyncBackgroundService error.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
