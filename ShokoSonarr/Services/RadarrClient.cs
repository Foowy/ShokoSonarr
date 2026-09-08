using System.Net.Http.Json;
using System.Text.Json;
using ShokoSonarr.Config;
using ShokoSonarr.Models;

namespace ShokoSonarr.Services;

/// <summary>Typed HTTP client for Radarr's v3 API. Never throws on HTTP/connectivity failure — all calls return a typed result. Mirrors SonarrClient's shape; reuses ArrActionResult/ArrQualityProfileResource/ArrRootFolderResource since Radarr's v3 API shares the same *arr-family conventions.</summary>
public class RadarrClient(HttpClient httpClient) : ArrClientBase(httpClient)
{
    private HttpRequestMessage BuildRequest(HttpMethod method, RadarrSettings settings, string path) =>
        BuildRequest(method, settings.BaseUrl, settings.ApiKey, path);

    /// <summary>Validates connectivity and API key against Radarr's system status endpoint.</summary>
    public Task<ArrActionResult<bool>> TestConnectionAsync(RadarrSettings settings, CancellationToken ct = default) =>
        SendAsync<bool>(BuildRequest(HttpMethod.Get, settings, "/api/v3/system/status"), ct);

    /// <summary>Looks up Radarr movie candidates by free-text title — the only matching path for unowned suggestions, which have no TMDB link.</summary>
    public Task<ArrActionResult<List<RadarrMovieLookupResult>>> LookupByTitleAsync(RadarrSettings settings, string title, CancellationToken ct = default) =>
        SendAsync<List<RadarrMovieLookupResult>>(BuildRequest(HttpMethod.Get, settings, $"/api/v3/movie/lookup?term={Uri.EscapeDataString(title)}"), ct);

    /// <summary>Adds a movie to Radarr, monitored, with an optional immediate search. Radarr has no granular monitor mode like Sonarr — just a flat monitored flag plus searchForMovie.</summary>
    public async Task<ArrActionResult<int>> AddMovieAsync(RadarrSettings settings, int tmdbId, string title, int qualityProfileId, string rootFolderPath, bool searchOnAdd, CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Post, settings, "/api/v3/movie");
        request.Content = JsonContent.Create(new
        {
            tmdbId,
            title,
            qualityProfileId,
            rootFolderPath,
            monitored = true,
            addOptions = new { searchForMovie = searchOnAdd },
        }, options: JsonOptions);

        var result = await SendAsync<JsonElement>(request, ct).ConfigureAwait(false);
        if (!result.Success)
            return ArrActionResult<int>.Fail(result.ErrorMessage!);

        return result.Data.TryGetProperty("id", out var idProp)
            ? ArrActionResult<int>.Ok(idProp.GetInt32())
            : ArrActionResult<int>.Fail("Radarr's add-movie response did not contain an id.");
    }

    /// <summary>Gets Radarr's configured quality profiles, for the settings dropdown. Reuses ArrQualityProfileResource — the shape is identical between Sonarr and Radarr's v3 API.</summary>
    public Task<ArrActionResult<List<ArrQualityProfileResource>>> GetQualityProfilesAsync(RadarrSettings settings, CancellationToken ct = default) =>
        SendAsync<List<ArrQualityProfileResource>>(BuildRequest(HttpMethod.Get, settings, "/api/v3/qualityprofile"), ct);

    /// <summary>Gets Radarr's configured root folders, for the settings dropdown. Reuses ArrRootFolderResource for the same reason.</summary>
    public Task<ArrActionResult<List<ArrRootFolderResource>>> GetRootFoldersAsync(RadarrSettings settings, CancellationToken ct = default) =>
        SendAsync<List<ArrRootFolderResource>>(BuildRequest(HttpMethod.Get, settings, "/api/v3/rootfolder"), ct);
}
