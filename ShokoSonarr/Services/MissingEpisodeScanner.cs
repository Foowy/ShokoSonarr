using NLog;
using Shoko.Abstractions.Metadata.Enums;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Abstractions.Metadata.Shoko;
using ShokoSonarr.Models;

namespace ShokoSonarr.Services;

/// <summary>Scans the Shoko collection for missing episodes on already-inventoried series, and reconciles previously-triggered Sonarr searches once Shoko confirms an episode was imported.</summary>
public class MissingEpisodeScanner(IMetadataService metadataService, ScanCacheStore cacheStore, SonarrClient sonarrClient, NotificationService notificationService)
{
    private static readonly Logger s_logger = LogManager.GetCurrentClassLogger();

    /// <summary>Pending entries older than this are dropped even if Sonarr keeps rejecting the unmonitor call (e.g. the Sonarr episode was deleted out-of-band), so a permanently-failing entry doesn't retry forever.</summary>
    private static readonly TimeSpan MaxPendingAge = TimeSpan.FromDays(14);

    // ponytail: single global lock -- two overlapping full scans just waste work and race SaveScan.
    // Serializing is enough; no need to cache/share the in-flight result. Revisit only if a per-series
    // scan ever needs to run concurrently with a full scan.
    private readonly SemaphoreSlim _scanLock = new(1, 1);

    /// <summary>Runs a full scan, reconciles any pending Sonarr searches against the fresh results, and returns a snapshot of all series with at least one missing episode.</summary>
    public async Task<ScanSnapshot> ScanAsync(CancellationToken ct = default)
    {
        await _scanLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ScanInternalAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private async Task<ScanSnapshot> ScanInternalAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var results = new List<SeriesMissingResult>();
        var settings = cacheStore.GetSettings();
        var pending = cacheStore.GetPendingSearches();
        var pendingByKey = pending.ToLookup(p => (p.ShokoSeriesId, p.AnidbEpisodeId));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Tracks every actually-missing episode regardless of HideUnaired, so reconciliation doesn't mistake "hidden because unaired" for "no longer missing".
        var stillMissingKeys = new HashSet<(int ShokoSeriesId, int AnidbEpisodeId)>();

        foreach (var series in metadataService.GetAllShokoSeries())
        {
            ct.ThrowIfCancellationRequested();
            // Only consider series already inventoried (v1 scope excludes fully-unowned anime).
            if (series.LocalEpisodeCounts.Episodes + series.LocalEpisodeCounts.Specials <= 0)
                continue;

            foreach (var key in EnumerateMissingKeys(series, settings))
                stillMissingKeys.Add(key);

            var result = BuildSeriesResult(series, settings, pendingByKey, today);
            if (result is not null)
                results.Add(result);
        }

        await ReconcilePendingSearchesAsync(pending, stillMissingKeys, settings, ct).ConfigureAwait(false);

        return new ScanSnapshot
        {
            ScannedAtUtc = DateTime.UtcNow,
            Series = [.. results.OrderByDescending(s => s.MissingEpisodes.Count)],
        };
    }

    /// <summary>The scanned episode types for a series, honoring the global setting and any per-series specials override.</summary>
    private EpisodeType[] ScannedTypesFor(IShokoSeries series, Config.SonarrSettings settings)
    {
        var includeSpecials = cacheStore.GetSeriesOverride(series.ID)?.IncludeSpecials ?? settings.IncludeSpecials;
        return includeSpecials ? [EpisodeType.Episode, EpisodeType.Special] : [EpisodeType.Episode];
    }

    /// <summary>Every actually-missing episode key for a series, ignoring the HideUnaired display filter.</summary>
    private IEnumerable<(int ShokoSeriesId, int AnidbEpisodeId)> EnumerateMissingKeys(IShokoSeries series, Config.SonarrSettings settings)
    {
        var scannedTypes = ScannedTypesFor(series, settings);
        return series.Episodes
            .Where(e => scannedTypes.Contains(e.Type) && !e.IsHidden && e.Videos.Count == 0)
            .Select(e => (series.ID, e.AnidbEpisodeID));
    }

    /// <summary>Builds the missing-episode result for one series, or null if it has nothing missing after the specials scope and HideUnaired filters.</summary>
    private SeriesMissingResult? BuildSeriesResult(IShokoSeries series, Config.SonarrSettings settings, ILookup<(int, int), PendingSearch> pendingByKey, DateOnly today)
    {
        var seriesOverride = cacheStore.GetSeriesOverride(series.ID);
        var overrideValue = seriesOverride?.IncludeSpecials;
        var scannedTypes = ScannedTypesFor(series, settings);

        var missing = series.Episodes
            .Where(e => scannedTypes.Contains(e.Type) && !e.IsHidden && e.Videos.Count == 0)
            .Select(e => new MissingEpisodeInfo
            {
                AnidbEpisodeId = e.AnidbEpisodeID,
                EpisodeNumber = e.EpisodeNumber,
                IsSpecial = e.Type == EpisodeType.Special,
                Title = e.Title,
                AirDate = e.AirDate,
                ActionStatus = pendingByKey.Contains((series.ID, e.AnidbEpisodeID)) ? "search-triggered" : "none",
            })
            .ToList();

        var displayMissing = (settings.HideUnaired ? missing.Where(e => e.AirDate is { } airDate && airDate <= today) : missing)
            .OrderBy(e => e.IsSpecial).ThenBy(e => e.EpisodeNumber)
            .ToList();

        if (displayMissing.Count == 0)
            return null;

        var tvdbId = (series.TmdbShows ?? [])
            .Select(s => s.TvdbShowID)
            .FirstOrDefault(id => id.HasValue);

        return new SeriesMissingResult
        {
            ShokoSeriesId = series.ID,
            Title = series.Title,
            TvdbId = tvdbId,
            GroupTitle = series.ParentGroup?.Title,
            QualityProfileIdOverride = seriesOverride?.QualityProfileId,
            RootFolderPathOverride = seriesOverride?.RootFolderPath,
            IncludeSpecialsOverride = overrideValue,
            MissingEpisodes = displayMissing,
        };
    }

    /// <summary>Recomputes a single series' missing-episode result, e.g. after a per-series override changed. Does not run reconciliation -- that only happens on a full <see cref="ScanAsync"/>.</summary>
    public Task<SeriesMissingResult?> ScanSeriesAsync(int shokoSeriesId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var series = metadataService.GetShokoSeriesByID(shokoSeriesId);
        if (series is null
            || series.LocalEpisodeCounts.Episodes + series.LocalEpisodeCounts.Specials <= 0)
            return Task.FromResult<SeriesMissingResult?>(null);

        var settings = cacheStore.GetSettings();
        var pendingByKey = cacheStore.GetPendingSearches().ToLookup(p => (p.ShokoSeriesId, p.AnidbEpisodeId));
        return Task.FromResult(BuildSeriesResult(series, settings, pendingByKey, DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    /// <summary>For each pending search whose episode is no longer in the fresh missing-episode results, tells Sonarr to unmonitor it and clears the pending entry. A failed Sonarr call is logged and left pending for the next scan — it must never fail the scan itself.
    /// "No longer in the results" covers two cases treated identically: the episode was actually imported by Shoko, or it fell out of scan scope (e.g. a specials-exclude override was set after the search was triggered). Both mean the plugin should stop tracking it and tell Sonarr to stop chasing it.
    /// <paramref name="stillMissingKeys"/> deliberately ignores the HideUnaired display filter — an episode hidden from the dashboard because it hasn't aired yet is still missing, not reconciled.</summary>
    private async Task ReconcilePendingSearchesAsync(List<PendingSearch> pending, HashSet<(int ShokoSeriesId, int AnidbEpisodeId)> stillMissingKeys, Config.SonarrSettings settings, CancellationToken ct)
    {
        if (pending.Count == 0)
            return;

        foreach (var entry in pending)
        {
            ct.ThrowIfCancellationRequested();
            if (stillMissingKeys.Contains((entry.ShokoSeriesId, entry.AnidbEpisodeId)))
                continue;

            try
            {
                var result = await sonarrClient.UnmonitorEpisodesAsync(settings, [entry.SonarrEpisodeId], ct).ConfigureAwait(false);
                if (result.Success)
                {
                    cacheStore.RemovePendingSearch(entry.ShokoSeriesId, entry.AnidbEpisodeId);
                    cacheStore.AddHistoryEntry(new SearchHistoryEntry
                    {
                        ShokoSeriesId = entry.ShokoSeriesId,
                        SeriesTitle = entry.SeriesTitle,
                        AnidbEpisodeId = entry.AnidbEpisodeId,
                        EpisodeTitle = entry.EpisodeTitle,
                        Outcome = SearchHistoryOutcome.Imported,
                        TimestampUtc = DateTime.UtcNow,
                    });
                }
                else
                {
                    s_logger.Warn("ShokoSonarr: failed to unmonitor Sonarr episode {SonarrEpisodeId} for AniDB episode {AnidbEpisodeId}: {Error}", entry.SonarrEpisodeId, entry.AnidbEpisodeId, result.ErrorMessage);
                    await ExpireIfStaleAsync(settings, entry, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                s_logger.Warn(ex, "ShokoSonarr: failed to unmonitor Sonarr episode {SonarrEpisodeId} for AniDB episode {AnidbEpisodeId}", entry.SonarrEpisodeId, entry.AnidbEpisodeId);
                await ExpireIfStaleAsync(settings, entry, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Drops a pending entry that has failed reconciliation for longer than <see cref="MaxPendingAge"/>, instead of retrying it forever.</summary>
    private async Task ExpireIfStaleAsync(Config.SonarrSettings settings, PendingSearch entry, CancellationToken ct)
    {
        if (DateTime.UtcNow - entry.TriggeredAtUtc < MaxPendingAge)
            return;

        s_logger.Warn("ShokoSonarr: giving up on Sonarr episode {SonarrEpisodeId} for AniDB episode {AnidbEpisodeId} after {MaxPendingAge} of failed reconciliation attempts", entry.SonarrEpisodeId, entry.AnidbEpisodeId, MaxPendingAge);
        cacheStore.RemovePendingSearch(entry.ShokoSeriesId, entry.AnidbEpisodeId);
        cacheStore.AddHistoryEntry(new SearchHistoryEntry
        {
            ShokoSeriesId = entry.ShokoSeriesId,
            SeriesTitle = entry.SeriesTitle,
            AnidbEpisodeId = entry.AnidbEpisodeId,
            EpisodeTitle = entry.EpisodeTitle,
            Outcome = SearchHistoryOutcome.Expired,
            TimestampUtc = DateTime.UtcNow,
        });

        var seriesLabel = string.IsNullOrEmpty(entry.SeriesTitle) ? $"series #{entry.ShokoSeriesId}" : entry.SeriesTitle;
        var episodeLabel = string.IsNullOrEmpty(entry.EpisodeTitle) ? $"AniDB episode {entry.AnidbEpisodeId}" : entry.EpisodeTitle;
        await notificationService.NotifyAsync(settings, $"Gave up tracking **{seriesLabel}** — {episodeLabel} — after {MaxPendingAge.TotalDays:0} days of failed reconciliation", ct).ConfigureAwait(false);
    }
}
