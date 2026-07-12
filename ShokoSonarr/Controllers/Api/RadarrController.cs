using Microsoft.AspNetCore.Mvc;
using ShokoSonarr.Services;

namespace ShokoSonarr.Controllers.Api;

/// <summary>Request body for a Radarr title search — movie-type suggestions have no TMDB link to auto-resolve from, so this is the only matching path.</summary>
/// <param name="Title">The title to search for.</param>
public record RadarrSearchTitleRequest(string Title);

/// <summary>Request body for adding a movie to Radarr, fully monitored with an immediate search.</summary>
/// <param name="TmdbId">The confirmed TMDB ID (from a search-title candidate the user picked).</param>
/// <param name="Title">The movie title to add.</param>
public record AddRadarrDiscoveryRequest(int TmdbId, string Title);

/// <summary>Endpoints for matching and adding movie-type suggestions to Radarr. Mirrors SonarrController's discovery-flow endpoints (search-title, add-discovery).</summary>
public class RadarrController(RadarrClient radarrClient, ScanCacheStore cacheStore, NotificationService notificationService) : ShokoSonarrBaseController
{
    /// <summary>Searches Radarr by title for a movie-type suggestion, which has no TMDB link to auto-resolve from.</summary>
    /// <param name="request">The title to search for.</param>
    /// <returns>200 with the candidate list (possibly empty), or 200 with success=false on a Radarr error.</returns>
    [HttpPost("search-title")]
    public async Task<IActionResult> SearchTitle([FromBody] RadarrSearchTitleRequest request)
    {
        var settings = cacheStore.GetRadarrSettings();
        var result = await radarrClient.LookupByTitleAsync(settings, request.Title);
        return Ok(new ApiResponse<object>(Success: result.Success, Message: result.ErrorMessage, Data: result.Data));
    }

    /// <summary>Adds a movie to Radarr, fully monitored with an immediate search.</summary>
    /// <param name="request">The confirmed TMDB ID and title to add.</param>
    /// <returns>200 on success, 409/400 with a message describing what failed.</returns>
    [HttpPost("add-discovery")]
    public async Task<IActionResult> AddDiscovery([FromBody] AddRadarrDiscoveryRequest request)
    {
        var settings = cacheStore.GetRadarrSettings();
        if (settings.QualityProfileId is null || string.IsNullOrEmpty(settings.RootFolderPath))
            return BadRequest(new ApiResponse<object>(Success: false, Message: "Quality profile and root folder must be configured in Radarr Settings before adding a movie.", Data: null));

        var added = await radarrClient.AddMovieAsync(settings, request.TmdbId, request.Title, settings.QualityProfileId.Value, settings.RootFolderPath!, searchOnAdd: true);
        if (!added.Success)
            return Conflict(new ApiResponse<object>(Success: false, Message: added.ErrorMessage, Data: null));

        var notifySettings = cacheStore.GetSettings();
        await notificationService.NotifyAsync(notifySettings, $"Added **{request.Title}** to Radarr (movie discovery, monitored and searching)");
        return Ok(new ApiResponse<object>(Success: true, Message: null, Data: null));
    }
}
