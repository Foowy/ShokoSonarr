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

        // Lookup returns candidates regardless of whether already added, so check existence first — this single action must work whether the series is new or not.
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

    /// <summary>Monitors and searches for the given missing episodes on a Sonarr series, via <see cref="SeriesMatcher.MonitorAndSearchAsync"/>.</summary>
    private async Task<IActionResult> MonitorAndSearchAsync(Config.SonarrSettings settings, int shokoSeriesId, int sonarrSeriesId, List<int> anidbEpisodeIds, Models.SeriesMissingResult series)
    {
        var result = await matcher.MonitorAndSearchAsync(settings, shokoSeriesId, sonarrSeriesId, anidbEpisodeIds, series);
        if (!result.Success)
            return Conflict(new ApiResponse<object>(Success: false, Message: result.ErrorMessage, Data: null));

        return Ok(new ApiResponse<object>(Success: true, Message: result.Data, Data: null));
    }
}
