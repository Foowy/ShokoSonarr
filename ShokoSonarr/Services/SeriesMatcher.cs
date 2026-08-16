using ShokoSonarr.Config;
using ShokoSonarr.Models;

namespace ShokoSonarr.Services;

/// <summary>Resolution outcome for matching a Shoko series to a Sonarr series.</summary>
public record MatchResolution(bool AutoResolved, int? TvdbId, List<SonarrSeriesLookupResult> Candidates, string? ErrorMessage);

/// <summary>Resolves a Shoko series to a Sonarr TVDB ID — via the TMDB-linked TVDB ID first, falling back to a confirmable title search.</summary>
public class SeriesMatcher(SonarrClient sonarrClient, ScanCacheStore cacheStore, NotificationService notificationService)
{
    /// <summary>
    /// Resolves the given series to a Sonarr match. If <paramref name="series"/> already has a TVDB ID
    /// (resolved from its TMDB link by the scanner), this confirms it resolves in Sonarr and auto-resolves.
    /// Otherwise, falls back to a title search whose candidates require user confirmation.
    /// </summary>
    public async Task<MatchResolution> ResolveAsync(SonarrSettings settings, SeriesMissingResult series, CancellationToken ct = default)
    {
        if (series.TvdbId.HasValue)
        {
            var byTvdbId = await sonarrClient.LookupByTvdbIdAsync(settings, series.TvdbId.Value, ct).ConfigureAwait(false);
            if (byTvdbId.Success && byTvdbId.Data!.Count > 0)
                return new MatchResolution(AutoResolved: true, TvdbId: series.TvdbId, Candidates: [], ErrorMessage: null);
        }

        var byTitle = await sonarrClient.LookupByTitleAsync(settings, series.Title, ct).ConfigureAwait(false);
        if (byTitle.Success && byTitle.Data!.Count > 0)
            return new MatchResolution(AutoResolved: false, TvdbId: null, Candidates: byTitle.Data!, ErrorMessage: null);

        return new MatchResolution(AutoResolved: false, TvdbId: null, Candidates: [], ErrorMessage: "no Sonarr match available");
    }

    /// <summary>Searches Sonarr by title directly, without going through a scanned series' TMDB-linked TVDB ID first. Used when there is no Shoko series to resolve from (e.g. a discovery suggestion).</summary>
    public Task<SonarrActionResult<List<SonarrSeriesLookupResult>>> SearchByTitleAsync(SonarrSettings settings, string title, CancellationToken ct = default) =>
        sonarrClient.LookupByTitleAsync(settings, title, ct);

    /// <summary>
    /// Monitors and searches for the given missing episodes on a Sonarr series. Shared by the dashboard's
    /// add-and-search/search endpoints and the native <see cref="Actions.SearchMissingEpisodesAction"/>.
    /// </summary>
    /// <remarks>
    /// v1 limitation: AniDB episode numbers are mapped to Sonarr season/episode numbers by assuming a single
    /// season of normal episodes (Sonarr season 1) plus specials (Sonarr season 0). TVDB series that split
    /// this same AniDB run across multiple Sonarr seasons are not handled and may cause the wrong episode to
    /// be searched. Full multi-season mapping is a known v2 improvement.
    /// </remarks>
    /// <returns>Success with an optional caveat message (unmapped episodes skipped), or failure with a reason.</returns>
    public async Task<SonarrActionResult<string?>> MonitorAndSearchAsync(SonarrSettings settings, int shokoSeriesId, int sonarrSeriesId, List<int> anidbEpisodeIds, SeriesMissingResult series, CancellationToken ct = default)
    {
        var episodesResult = await sonarrClient.GetEpisodesAsync(settings, sonarrSeriesId, ct);
        if (!episodesResult.Success)
            return SonarrActionResult<string?>.Fail(episodesResult.ErrorMessage!);

        // Normal episodes map to Sonarr season 1+; specials map to Sonarr's season 0.
        var targetEpisodes = series.MissingEpisodes.Where(e => anidbEpisodeIds.Contains(e.AnidbEpisodeId)).ToList();
        var sonarrEpisodeIds = new List<int>();
        var sonarrEpisodeIdByAnidbId = new Dictionary<int, int>();
        var unmappedIds = new List<int>();
        var unmappedTitles = new List<string>();
        foreach (var ep in targetEpisodes)
        {
            var seasonNumber = ep.IsSpecial ? 0 : 1;
            var match = episodesResult.Data!.Find(se => se.SeasonNumber == seasonNumber && se.EpisodeNumber == ep.EpisodeNumber);
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
            cacheStore.AddHistoryEntry(new SearchHistoryEntry
            {
                ShokoSeriesId = shokoSeriesId,
                SeriesTitle = series.Title,
                AnidbEpisodeId = ep.AnidbEpisodeId,
                EpisodeTitle = ep.Title,
                Outcome = SearchHistoryOutcome.Triggered,
                TimestampUtc = triggeredAt,
            });
        }

        var triggeredCount = targetEpisodes.Count - unmappedIds.Count;
        await notificationService.NotifyAsync(settings, $"Triggered Sonarr search for {triggeredCount} episode(s) of **{series.Title}**");

        var message = unmappedTitles.Count > 0 ? $"Search triggered. Unmapped episodes skipped: {string.Join(", ", unmappedTitles)}" : null;
        return SonarrActionResult<string?>.Ok(message);
    }
}
