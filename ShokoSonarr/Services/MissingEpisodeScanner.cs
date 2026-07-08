using NLog;
using Shoko.Abstractions.Metadata.Enums;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Abstractions.Metadata.Shoko;
using ShokoSonarr.Models;

namespace ShokoSonarr.Services;

/// <summary>Scans the Shoko collection for missing episodes on already-inventoried series, and reconciles previously-triggered Sonarr searches once Shoko confirms an episode was imported.</summary>
public class MissingEpisodeScanner(IMetadataService metadataService, ScanCacheStore cacheStore, SonarrClient sonarrClient)
{
    private static readonly Logger s_logger = LogManager.GetCurrentClassLogger();

    /// <summary>Runs a full scan, reconciles any pending Sonarr searches against the fresh results, and returns a snapshot of all series with at least one missing episode.</summary>
    public async Task<ScanSnapshot> ScanAsync()
    {
        var results = new List<SeriesMissingResult>();
        var globalIncludeSpecials = cacheStore.GetSettings().IncludeSpecials;
        var pending = cacheStore.GetPendingSearches();
        var pendingByKey = pending.ToLookup(p => (p.ShokoSeriesId, p.AnidbEpisodeId));

        foreach (var series in metadataService.GetAllShokoSeries())
        {
            // Only consider series already inventoried (v1 scope excludes fully-unowned anime).
            if (series.LocalEpisodeCounts.Episodes + series.LocalEpisodeCounts.Specials <= 0)
                continue;

            var overrideValue = cacheStore.GetSeriesOverride(series.ID)?.IncludeSpecials;
            var includeSpecials = overrideValue ?? globalIncludeSpecials;
            var scannedTypes = includeSpecials
                ? new[] { EpisodeType.Episode, EpisodeType.Special }
                : [EpisodeType.Episode];

            var missing = series.Episodes
                .Where(e => scannedTypes.Contains(e.Type) && !e.IsHidden && e.VideoList.Count == 0)
                .Select(e => new MissingEpisodeInfo
                {
                    AnidbEpisodeId = e.AnidbEpisodeID,
                    EpisodeNumber = e.EpisodeNumber,
                    IsSpecial = e.Type == EpisodeType.Special,
                    Title = e.Title,
                    AirDate = e.AirDate,
                    ActionStatus = pendingByKey.Contains((series.ID, e.AnidbEpisodeID)) ? "search-triggered" : "none",
                })
                .OrderBy(e => e.IsSpecial).ThenBy(e => e.EpisodeNumber)
                .ToList();

            if (missing.Count == 0)
                continue;

            var tvdbId = (series.TmdbShows ?? [])
                .Select(s => s.TvdbShowID)
                .FirstOrDefault(id => id.HasValue);

            results.Add(new SeriesMissingResult
            {
                ShokoSeriesId = series.ID,
                Title = series.Title,
                TvdbId = tvdbId,
                IncludeSpecialsOverride = overrideValue,
                MissingEpisodes = missing,
            });
        }

        await ReconcilePendingSearchesAsync(pending, results).ConfigureAwait(false);

        return new ScanSnapshot
        {
            ScannedAtUtc = DateTime.UtcNow,
            Series = [.. results.OrderByDescending(s => s.MissingEpisodes.Count)],
        };
    }

    /// <summary>For each pending search whose episode is no longer in the fresh missing-episode results, tells Sonarr to unmonitor it and clears the pending entry. A failed Sonarr call is logged and left pending for the next scan — it must never fail the scan itself.
    /// "No longer in the results" covers two cases treated identically: the episode was actually imported by Shoko, or it fell out of scan scope (e.g. a specials-exclude override was set after the search was triggered). Both mean the plugin should stop tracking it and tell Sonarr to stop chasing it.</summary>
    private async Task ReconcilePendingSearchesAsync(List<PendingSearch> pending, List<SeriesMissingResult> freshResults)
    {
        if (pending.Count == 0)
            return;

        var stillMissing = freshResults
            .SelectMany(s => s.MissingEpisodes.Select(e => (s.ShokoSeriesId, e.AnidbEpisodeId)))
            .ToHashSet();

        var settings = cacheStore.GetSettings();
        foreach (var entry in pending)
        {
            if (stillMissing.Contains((entry.ShokoSeriesId, entry.AnidbEpisodeId)))
                continue;

            try
            {
                var result = await sonarrClient.UnmonitorEpisodesAsync(settings, [entry.SonarrEpisodeId]).ConfigureAwait(false);
                if (result.Success)
                {
                    cacheStore.RemovePendingSearch(entry.ShokoSeriesId, entry.AnidbEpisodeId);
                }
                else
                {
                    s_logger.Warn("ShokoSonarr: failed to unmonitor Sonarr episode {SonarrEpisodeId} for AniDB episode {AnidbEpisodeId}: {Error}", entry.SonarrEpisodeId, entry.AnidbEpisodeId, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                s_logger.Warn(ex, "ShokoSonarr: failed to unmonitor Sonarr episode {SonarrEpisodeId} for AniDB episode {AnidbEpisodeId}", entry.SonarrEpisodeId, entry.AnidbEpisodeId);
            }
        }
    }
}
