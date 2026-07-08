using Microsoft.Extensions.Hosting;
using NLog;
using Shoko.Abstractions.Core.Services;
using ShokoSonarr.Services;

namespace ShokoSonarr.BackgroundServices;

/// <summary>Runs the missing-episode scan on a configurable interval, in addition to the on-demand dashboard trigger.</summary>
public class ScanSchedulerService(ISystemService systemService, MissingEpisodeScanner scanner, ScanCacheStore cacheStore) : BackgroundService
{
    private static readonly Logger s_logger = LogManager.GetCurrentClassLogger();

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        s_logger.Info("ShokoSonarr scan scheduler waiting for Shoko Server to reach 'Started' state...");
        while (!systemService.IsStarted && !stoppingToken.IsCancellationRequested)
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
        if (stoppingToken.IsCancellationRequested)
            return;

        s_logger.Info("ShokoSonarr scan scheduler started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var intervalHours = cacheStore.GetSettings().ScanIntervalHours;
                if (intervalHours <= 0)
                {
                    // Scheduled scanning disabled — re-check hourly in case the user re-enables it.
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var lastScan = cacheStore.GetLastScan();
                var dueAt = (lastScan?.ScannedAtUtc ?? DateTime.MinValue).AddHours(intervalHours);
                if (DateTime.UtcNow >= dueAt)
                {
                    s_logger.Info("ShokoSonarr: running scheduled missing-episode scan.");
                    var snapshot = await scanner.ScanAsync().ConfigureAwait(false);
                    cacheStore.SaveScan(snapshot);
                    dueAt = DateTime.UtcNow.AddHours(intervalHours);
                }

                var delay = dueAt - DateTime.UtcNow;
                await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                s_logger.Warn(ex, "ShokoSonarr: scheduled scan loop error, retrying in 1 minute.");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
