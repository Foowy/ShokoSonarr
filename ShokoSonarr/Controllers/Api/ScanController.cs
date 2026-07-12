using Microsoft.AspNetCore.Mvc;
using Shoko.Abstractions.Metadata.Services;
using ShokoSonarr.Services;

namespace ShokoSonarr.Controllers.Api;

/// <summary>Request body for setting a series' specials override.</summary>
/// <param name="IncludeSpecials">True/false to force include/exclude specials for this series; null clears the override (inherit the global default).</param>
public record SetSeriesSpecialsRequest(bool? IncludeSpecials);

/// <summary>Request body for setting a series' Sonarr quality-profile/root-folder override.</summary>
/// <param name="QualityProfileId">The quality profile ID to use for this series; null clears the override.</param>
/// <param name="RootFolderPath">The root folder path to use for this series; null clears the override.</param>
public record SetSeriesSonarrOverrideRequest(int? QualityProfileId, string? RootFolderPath);

/// <summary>Endpoints for running and reading missing-episode scans.</summary>
public class ScanController(MissingEpisodeScanner scanner, ScanCacheStore cacheStore, IMetadataService metadataService, SonarrClient sonarrClient, Services.RelatedSeriesFinder relatedSeriesFinder) : ShokoSonarrBaseController
{
    /// <summary>Runs a missing-episode scan immediately and persists the result as the current snapshot.</summary>
    /// <returns>The freshly computed scan snapshot.</returns>
    [HttpPost]
    public async Task<IActionResult> RunScan()
    {
        var snapshot = await scanner.ScanAsync();
        cacheStore.SaveScan(snapshot);
        return Ok(new ApiResponse<object>(Success: true, Message: null, Data: snapshot));
    }

    /// <summary>Sets (or clears) a series' specials override, then re-runs the scan so the dashboard reflects it immediately.</summary>
    /// <param name="shokoSeriesId">The Shoko series ID.</param>
    /// <param name="request">The override to set.</param>
    /// <returns>The freshly computed scan snapshot.</returns>
    [HttpPut("series/{shokoSeriesId:int}/include-specials")]
    public async Task<IActionResult> SetSeriesSpecials(int shokoSeriesId, [FromBody] SetSeriesSpecialsRequest request)
    {
        if (metadataService.GetShokoSeriesByID(shokoSeriesId) is null)
            return NotFound(new ApiResponse<object>(Success: false, Message: $"No Shoko series with ID {shokoSeriesId}.", Data: null));

        cacheStore.SetSeriesOverride(shokoSeriesId, request.IncludeSpecials);
        var snapshot = await scanner.ScanAsync();
        cacheStore.SaveScan(snapshot);
        return Ok(new ApiResponse<object>(Success: true, Message: null, Data: snapshot));
    }

    /// <summary>Sets (or clears) a series' Sonarr quality-profile/root-folder override, then re-runs the scan so the dashboard reflects it immediately.</summary>
    /// <param name="shokoSeriesId">The Shoko series ID.</param>
    /// <param name="request">The override to set.</param>
    /// <returns>The freshly computed scan snapshot.</returns>
    [HttpPut("series/{shokoSeriesId:int}/sonarr-override")]
    public async Task<IActionResult> SetSeriesSonarrOverride(int shokoSeriesId, [FromBody] SetSeriesSonarrOverrideRequest request)
    {
        if (metadataService.GetShokoSeriesByID(shokoSeriesId) is null)
            return NotFound(new ApiResponse<object>(Success: false, Message: $"No Shoko series with ID {shokoSeriesId}.", Data: null));

        cacheStore.SetSeriesSonarrOverride(shokoSeriesId, request.QualityProfileId, request.RootFolderPath);
        var snapshot = await scanner.ScanAsync();
        cacheStore.SaveScan(snapshot);
        return Ok(new ApiResponse<object>(Success: true, Message: null, Data: snapshot));
    }

    /// <summary>Gets the most recently computed scan snapshot.</summary>
    /// <returns>The cached scan snapshot, or 204 if no scan has run yet.</returns>
    [HttpGet("results")]
    public IActionResult GetResults()
    {
        var snapshot = cacheStore.GetLastScan();
        return snapshot is null ? NoContent() : Ok(new ApiResponse<object>(Success: true, Message: null, Data: snapshot));
    }

    /// <summary>Gets all episodes currently pending reconciliation with Sonarr — searches the plugin has triggered but Shoko hasn't yet confirmed as imported.</summary>
    [HttpGet("pending")]
    public IActionResult GetPending() =>
        Ok(new ApiResponse<object>(Success: true, Message: null, Data: cacheStore.GetPendingSearches()));

    /// <summary>Cancels a pending search: tells Sonarr to unmonitor the episode and stops tracking it for reconciliation.</summary>
    /// <param name="shokoSeriesId">The Shoko series ID.</param>
    /// <param name="anidbEpisodeId">The AniDB episode ID.</param>
    [HttpDelete("pending/{shokoSeriesId:int}/{anidbEpisodeId:int}")]
    public async Task<IActionResult> CancelPending(int shokoSeriesId, int anidbEpisodeId)
    {
        var entry = cacheStore.GetPendingSearches().FirstOrDefault(p => p.ShokoSeriesId == shokoSeriesId && p.AnidbEpisodeId == anidbEpisodeId);
        if (entry is null)
            return NotFound(new ApiResponse<object>(Success: false, Message: "No pending search for that series/episode.", Data: null));

        var settings = cacheStore.GetSettings();
        var result = await sonarrClient.UnmonitorEpisodesAsync(settings, [entry.SonarrEpisodeId]);
        if (!result.Success)
            return Ok(new ApiResponse<object>(Success: false, Message: $"Failed to unmonitor in Sonarr: {result.ErrorMessage}", Data: cacheStore.GetPendingSearches()));

        cacheStore.RemovePendingSearch(shokoSeriesId, anidbEpisodeId);
        cacheStore.AddHistoryEntry(new Models.SearchHistoryEntry
        {
            ShokoSeriesId = entry.ShokoSeriesId,
            SeriesTitle = entry.SeriesTitle,
            AnidbEpisodeId = entry.AnidbEpisodeId,
            EpisodeTitle = entry.EpisodeTitle,
            Outcome = Models.SearchHistoryOutcome.Cancelled,
            TimestampUtc = DateTime.UtcNow,
        });
        return Ok(new ApiResponse<object>(Success: true, Message: null, Data: cacheStore.GetPendingSearches()));
    }

    /// <summary>Gets the search-history log: past outcomes (imported, cancelled, expired) for episodes that were previously pending, most recent first.</summary>
    [HttpGet("history")]
    public IActionResult GetHistory() =>
        Ok(new ApiResponse<object>(Success: true, Message: null, Data: cacheStore.GetHistory()));

    /// <summary>Gets AniDB-related series the user owns zero episodes of, suggested from series they do own (sequels, prequels, side stories, etc.). Computed fresh on every call — no persistence.</summary>
    [HttpGet("related-suggestions")]
    public IActionResult GetRelatedSuggestions() =>
        Ok(new ApiResponse<object>(Success: true, Message: null, Data: relatedSeriesFinder.FindSuggestions()));
}
