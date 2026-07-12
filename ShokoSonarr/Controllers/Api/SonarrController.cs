using Microsoft.AspNetCore.Mvc;
using ShokoSonarr.Services;

namespace ShokoSonarr.Controllers.Api;

/// <summary>Request body for adding a series to Sonarr and searching for its missing episodes.</summary>
/// <param name="ShokoSeriesId">The Shoko series ID (must be present in the last scan snapshot).</param>
/// <param name="TvdbId">The confirmed TVDB ID to add (from auto-resolution or user-confirmed title search).</param>
/// <param name="AnidbEpisodeIds">The specific missing episodes (by AniDB episode ID) to monitor and search for.</param>
public record AddAndSearchRequest(int ShokoSeriesId, int TvdbId, List<int> AnidbEpisodeIds);

/// <summary>Request body for triggering search on a series already present in Sonarr.</summary>
/// <param name="ShokoSeriesId">The Shoko series ID (must be present in the last scan snapshot).</param>
/// <param name="SonarrSeriesId">The existing Sonarr series ID.</param>
/// <param name="AnidbEpisodeIds">The specific missing episodes (by AniDB episode ID) to monitor and search for.</param>
public record SearchRequest(int ShokoSeriesId, int SonarrSeriesId, List<int> AnidbEpisodeIds);

/// <summary>Request body for a Sonarr title search on a series not yet in Shoko's scan snapshot (e.g. a discovery suggestion).</summary>
/// <param name="Title">The title to search for.</param>
public record SearchTitleRequest(string Title);

/// <summary>Request body for adding a wholly unowned series to Sonarr with full monitoring and an immediate search.</summary>
/// <param name="TvdbId">The confirmed TVDB ID (from a search-title candidate the user picked).</param>
/// <param name="Title">The series title to add.</param>
public record AddDiscoveryRequest(int TvdbId, string Title);

/// <summary>Result of a single series' tag sync attempt, for the sync-tags summary response.</summary>
public record TagSyncResult(int Updated, int SkippedNoMatch, int Failed);

/// <summary>Endpoints for matching Shoko series to Sonarr and triggering add/monitor/search actions.</summary>
public class SonarrController(SeriesMatcher matcher, SonarrClient sonarrClient, ScanCacheStore cacheStore, NotificationService notificationService) : ShokoSonarrBaseController
{
    /// <summary>Resolves a Sonarr match for the given Shoko series from the cached scan snapshot.</summary>
    /// <param name="shokoSeriesId">The Shoko series ID.</param>
    /// <returns>The match resolution — auto-resolved, candidate list for confirmation, or no-match error.</returns>
    [HttpGet("match/{shokoSeriesId:int}")]
    public async Task<IActionResult> GetMatch(int shokoSeriesId)
    {
        var snapshot = cacheStore.GetLastScan();
        var series = snapshot?.Series.Find(s => s.ShokoSeriesId == shokoSeriesId);
        if (series is null)
            return NotFound(new ApiResponse<object>(Success: false, Message: "Series not found in the last scan.", Data: null));

        var settings = cacheStore.GetSettings();
        var resolution = await matcher.ResolveAsync(settings, series);
        return Ok(new ApiResponse<object>(Success: resolution.ErrorMessage is null, Message: resolution.ErrorMessage, Data: resolution));
    }

    /// <summary>Searches Sonarr by title for a series not present in the last scan snapshot (e.g. a related-series suggestion, which has no Shoko TMDB/TVDB link to auto-resolve from).</summary>
    /// <param name="request">The title to search for.</param>
    /// <returns>200 with the candidate list (possibly empty), or 200 with success=false on a Sonarr error.</returns>
    [HttpPost("search-title")]
    public async Task<IActionResult> SearchTitle([FromBody] SearchTitleRequest request)
    {
        var settings = cacheStore.GetSettings();
        var result = await matcher.SearchByTitleAsync(settings, request.Title);
        return Ok(new ApiResponse<object>(Success: result.Success, Message: result.ErrorMessage, Data: result.Data));
    }

    /// <summary>Adds a wholly unowned series to Sonarr, fully monitored with an immediate search — used for discovery suggestions, which have no per-episode missing data to selectively monitor (unlike the owned-series add-and-search flow).</summary>
    /// <param name="request">The confirmed TVDB ID and title to add.</param>
    /// <returns>200 on success, 409/400 with a message describing what failed.</returns>
    [HttpPost("add-discovery")]
    public async Task<IActionResult> AddDiscovery([FromBody] AddDiscoveryRequest request)
    {
        var settings = cacheStore.GetSettings();
        if (settings.QualityProfileId is null || string.IsNullOrEmpty(settings.RootFolderPath))
            return BadRequest(new ApiResponse<object>(Success: false, Message: "Quality profile and root folder must be configured in Settings before adding a series.", Data: null));

        var added = await sonarrClient.AddSeriesAsync(settings, request.TvdbId, request.Title, settings.QualityProfileId.Value, settings.RootFolderPath!, monitorMode: "all", searchOnAdd: true);
        if (!added.Success)
            return Conflict(new ApiResponse<object>(Success: false, Message: added.ErrorMessage, Data: null));

        await notificationService.NotifyAsync(settings, $"Added **{request.Title}** to Sonarr (full-series discovery, monitored and searching)");
        return Ok(new ApiResponse<object>(Success: true, Message: null, Data: null));
    }

    /// <summary>Retroactively tags owned series already present in Sonarr with their Shoko group's title, for series added before tag propagation existed. Series not yet in Sonarr are skipped — they get tagged automatically at add time.</summary>
    /// <returns>200 with a summary of updated/skipped/failed counts.</returns>
    [HttpPost("sync-tags")]
    public async Task<IActionResult> SyncTags()
    {
        var snapshot = cacheStore.GetLastScan();
        var settings = cacheStore.GetSettings();
        var candidates = (snapshot?.Series ?? []).Where(s => !string.IsNullOrEmpty(s.GroupTitle) && s.TvdbId.HasValue).ToList();

        int updated = 0, skipped = 0, failed = 0;
        foreach (var series in candidates)
        {
            var existing = await sonarrClient.GetExistingSeriesByTvdbIdAsync(settings, series.TvdbId!.Value);
            if (!existing.Success || existing.Data!.Count == 0)
            {
                skipped++;
                continue;
            }

            var tag = await sonarrClient.EnsureTagIdAsync(settings, series.GroupTitle!);
            if (!tag.Success)
            {
                failed++;
                continue;
            }

            var update = await sonarrClient.UpdateSeriesTagAsync(settings, existing.Data[0].Id, tag.Data);
            if (update.Success) updated++; else failed++;
        }

        return Ok(new ApiResponse<object>(Success: true, Message: null, Data: new TagSyncResult(updated, skipped, failed)));
    }

    /// <summary>Adds a series to Sonarr (monitoring disabled by default), then monitors and searches for the given missing episodes.</summary>
    /// <param name="request">The add-and-search request.</param>
    /// <returns>200 on success, 409/400 with a message describing what failed.</returns>
    [HttpPost("add-and-search")]
    public async Task<IActionResult> AddAndSearch([FromBody] AddAndSearchRequest request)
    {
        var snapshot = cacheStore.GetLastScan();
        var series = snapshot?.Series.Find(s => s.ShokoSeriesId == request.ShokoSeriesId);
        if (series is null)
            return NotFound(new ApiResponse<object>(Success: false, Message: "Series not found in the last scan.", Data: null));

        var settings = cacheStore.GetSettings();
        var seriesOverride = cacheStore.GetSeriesOverride(request.ShokoSeriesId);
        var qualityProfileId = seriesOverride?.QualityProfileId ?? settings.QualityProfileId;
        var rootFolderPath = seriesOverride?.RootFolderPath ?? settings.RootFolderPath;
        if (qualityProfileId is null || string.IsNullOrEmpty(rootFolderPath))
            return BadRequest(new ApiResponse<object>(Success: false, Message: "Quality profile and root folder must be configured in Settings before adding a series.", Data: null));

        // A series matched by TVDB ID may already exist in the user's Sonarr instance (lookup returns
        // candidates regardless of whether they're already added) — check for that first so this single
        // action works whether or not the series is new.
        var existing = await sonarrClient.GetExistingSeriesByTvdbIdAsync(settings, request.TvdbId);
        if (existing.Success && existing.Data!.Count > 0)
            return await MonitorAndSearchAsync(settings, request.ShokoSeriesId, existing.Data[0].Id, request.AnidbEpisodeIds, series);

        List<int>? tagIds = null;
        if (!string.IsNullOrEmpty(series.GroupTitle))
        {
            var tag = await sonarrClient.EnsureTagIdAsync(settings, series.GroupTitle);
            if (tag.Success)
                tagIds = [tag.Data];
        }

        var added = await sonarrClient.AddSeriesAsync(settings, request.TvdbId, series.Title, qualityProfileId.Value, rootFolderPath!, tagIds: tagIds);
        if (!added.Success)
            return Conflict(new ApiResponse<object>(Success: false, Message: added.ErrorMessage, Data: null));

        return await MonitorAndSearchAsync(settings, request.ShokoSeriesId, added.Data, request.AnidbEpisodeIds, series);
    }

    /// <summary>Monitors and searches for the given missing episodes on a series already present in Sonarr.</summary>
    /// <param name="request">The search request.</param>
    /// <returns>200 on success, 409/400 with a message describing what failed.</returns>
    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] SearchRequest request)
    {
        var snapshot = cacheStore.GetLastScan();
        var series = snapshot?.Series.Find(s => s.ShokoSeriesId == request.ShokoSeriesId);
        if (series is null)
            return NotFound(new ApiResponse<object>(Success: false, Message: "Series not found in the last scan.", Data: null));

        var settings = cacheStore.GetSettings();
        return await MonitorAndSearchAsync(settings, request.ShokoSeriesId, request.SonarrSeriesId, request.AnidbEpisodeIds, series);
    }

    /// <summary>
    /// Monitors and searches for the given missing episodes on a Sonarr series.
    /// </summary>
    /// <remarks>
    /// v1 limitation: AniDB episode numbers are mapped to Sonarr season/episode numbers by assuming a single
    /// season of normal episodes (Sonarr season 1) plus specials (Sonarr season 0). TVDB series that split
    /// this same AniDB run across multiple Sonarr seasons are not handled and may cause the wrong episode to
    /// be searched. Full multi-season mapping is a known v2 improvement.
    /// </remarks>
    private async Task<IActionResult> MonitorAndSearchAsync(Config.SonarrSettings settings, int shokoSeriesId, int sonarrSeriesId, List<int> anidbEpisodeIds, Models.SeriesMissingResult series)
    {
        var episodesResult = await sonarrClient.GetEpisodesAsync(settings, sonarrSeriesId);
        if (!episodesResult.Success)
            return Conflict(new ApiResponse<object>(Success: false, Message: episodesResult.ErrorMessage, Data: null));

        // Map AniDB episode numbers (normal episodes only map cleanly to Sonarr season 1+; specials map to Sonarr's season 0)
        // to Sonarr episode IDs by season/episode number.
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
            return BadRequest(new ApiResponse<object>(Success: false, Message: $"No episodes could be mapped to Sonarr. Unmapped: {string.Join(", ", unmappedTitles)}", Data: null));

        var monitorResult = await sonarrClient.MonitorEpisodesAsync(settings, sonarrEpisodeIds);
        if (!monitorResult.Success)
            return Conflict(new ApiResponse<object>(Success: false, Message: monitorResult.ErrorMessage, Data: null));

        var searchResult = await sonarrClient.TriggerEpisodeSearchAsync(settings, sonarrEpisodeIds);
        if (!searchResult.Success)
            return Conflict(new ApiResponse<object>(Success: false, Message: searchResult.ErrorMessage, Data: null));

        var triggeredAt = DateTime.UtcNow;
        foreach (var ep in targetEpisodes.Where(e => !unmappedIds.Contains(e.AnidbEpisodeId)))
        {
            cacheStore.AddPendingSearch(new Models.PendingSearch
            {
                ShokoSeriesId = shokoSeriesId,
                SeriesTitle = series.Title,
                AnidbEpisodeId = ep.AnidbEpisodeId,
                EpisodeTitle = ep.Title,
                SonarrSeriesId = sonarrSeriesId,
                SonarrEpisodeId = sonarrEpisodeIdByAnidbId[ep.AnidbEpisodeId],
                TriggeredAtUtc = triggeredAt,
            });
            cacheStore.AddHistoryEntry(new Models.SearchHistoryEntry
            {
                ShokoSeriesId = shokoSeriesId,
                SeriesTitle = series.Title,
                AnidbEpisodeId = ep.AnidbEpisodeId,
                EpisodeTitle = ep.Title,
                Outcome = Models.SearchHistoryOutcome.Triggered,
                TimestampUtc = triggeredAt,
            });
        }

        var triggeredCount = targetEpisodes.Count - unmappedIds.Count;
        await notificationService.NotifyAsync(settings, $"Triggered Sonarr search for {triggeredCount} episode(s) of **{series.Title}**");

        var message = unmappedTitles.Count > 0 ? $"Search triggered. Unmapped episodes skipped: {string.Join(", ", unmappedTitles)}" : null;
        return Ok(new ApiResponse<object>(Success: true, Message: message, Data: null));
    }
}
