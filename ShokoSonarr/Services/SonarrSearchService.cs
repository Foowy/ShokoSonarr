using ShokoSonarr.Config;
using ShokoSonarr.Models;

namespace ShokoSonarr.Services;

/// <summary>Monitors and searches for specific missing episodes on a Sonarr series. Shared by the
/// dashboard's add-and-search/search endpoints and the native <see cref="Actions.SearchMissingEpisodesAction"/>.</summary>
public class SonarrSearchService(SonarrClient sonarrClient, ScanCacheStore cacheStore, NotificationService notificationService)
{
    /// <returns>Success with an optional caveat message (unmapped episodes skipped), or failure with a reason.</returns>
    public async Task<SonarrActionResult<string?>> MonitorAndSearchAsync(SonarrSettings settings, int shokoSeriesId, int sonarrSeriesId, List<int> anidbEpisodeIds, SeriesMissingResult series, CancellationToken ct = default)
    {
        var episodesResult = await sonarrClient.GetEpisodesAsync(settings, sonarrSeriesId, ct);
        if (!episodesResult.Success)
            return SonarrActionResult<string?>.Fail(episodesResult.ErrorMessage!);

        var targetEpisodes = series.MissingEpisodes.Where(e => anidbEpisodeIds.Contains(e.AnidbEpisodeId)).ToList();
        // AniDB per-series episode numbers are absolute; for anime-typed Sonarr series they line up with
        // AbsoluteEpisodeNumber regardless of how TheTVDB splits the run into seasons. Series added before
        // ShokoSonarr set seriesType=anime stay Standard and expose no absolute numbers -- fall back to the
        // old (season 1, N) match for those so an upgrade doesn't silently unmap every legacy series.
        var anySonarrAbsolute = episodesResult.Data!.Any(se => se.AbsoluteEpisodeNumber.HasValue);
        var sonarrEpisodeIds = new List<int>();
        var sonarrEpisodeIdByAnidbId = new Dictionary<int, int>();
        var unmappedIds = new List<int>();
        var unmappedTitles = new List<string>();
        foreach (var ep in targetEpisodes)
        {
            SonarrEpisodeResource? match = ep.IsSpecial
                ? episodesResult.Data!.Find(se => se.SeasonNumber == 0 && se.EpisodeNumber == ep.EpisodeNumber)
                : anySonarrAbsolute
                    ? episodesResult.Data!.Find(se => se.AbsoluteEpisodeNumber == ep.EpisodeNumber)
                    : episodesResult.Data!.Find(se => se.SeasonNumber == 1 && se.EpisodeNumber == ep.EpisodeNumber);
            if (match is null)
            {
                unmappedIds.Add(ep.AnidbEpisodeId);
                unmappedTitles.Add(ep.Title);
            }
            else
            {
                sonarrEpisodeIds.Add(match.Id);
                sonarrEpisodeIdByAnidbId[ep.AnidbEpisodeId] = match.Id;
            }
        }

        if (sonarrEpisodeIds.Count == 0)
            return SonarrActionResult<string?>.Fail($"No episodes could be mapped to Sonarr. Unmapped: {string.Join(", ", unmappedTitles)}");

        var monitorResult = await sonarrClient.MonitorEpisodesAsync(settings, sonarrEpisodeIds, ct);
        if (!monitorResult.Success)
            return SonarrActionResult<string?>.Fail(monitorResult.ErrorMessage!);

        var searchResult = await sonarrClient.TriggerEpisodeSearchAsync(settings, sonarrEpisodeIds, ct);
        if (!searchResult.Success)
            return SonarrActionResult<string?>.Fail(searchResult.ErrorMessage!);

        var triggeredAt = DateTime.UtcNow;
        var historyEntries = new List<SearchHistoryEntry>();
        foreach (var ep in targetEpisodes.Where(e => !unmappedIds.Contains(e.AnidbEpisodeId)))
        {
            cacheStore.AddPendingSearch(new PendingSearch
            {
                ShokoSeriesId = shokoSeriesId,
                SeriesTitle = series.Title,
                AnidbEpisodeId = ep.AnidbEpisodeId,
                EpisodeTitle = ep.Title,
                SonarrSeriesId = sonarrSeriesId,
                SonarrEpisodeId = sonarrEpisodeIdByAnidbId[ep.AnidbEpisodeId],
                TriggeredAtUtc = triggeredAt,
            });
            historyEntries.Add(new SearchHistoryEntry
            {
                ShokoSeriesId = shokoSeriesId,
                SeriesTitle = series.Title,
                AnidbEpisodeId = ep.AnidbEpisodeId,
                EpisodeTitle = ep.Title,
                Outcome = SearchHistoryOutcome.Triggered,
                TimestampUtc = triggeredAt,
            });
        }
        cacheStore.AddHistoryEntries(historyEntries);

        var triggeredCount = targetEpisodes.Count - unmappedIds.Count;
        await notificationService.NotifyAsync(settings, $"Triggered Sonarr search for {triggeredCount} episode(s) of **{series.Title}**");

        var message = unmappedTitles.Count > 0 ? $"Search triggered. Unmapped episodes skipped: {string.Join(", ", unmappedTitles)}" : null;
        return SonarrActionResult<string?>.Ok(message);
    }
}
