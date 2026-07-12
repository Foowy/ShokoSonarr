using System.Net.Http.Json;
using System.Text.Json;
using ShokoSonarr.Config;
using ShokoSonarr.Models;

namespace ShokoSonarr.Services;

/// <summary>Typed HTTP client for Radarr's v3 API. Never throws on HTTP/connectivity failure — all calls return a typed result. Mirrors SonarrClient's shape; reuses SonarrActionResult/SonarrQualityProfileResource/SonarrRootFolderResource since Radarr's v3 API shares the same *arr-family conventions.</summary>
public class RadarrClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private HttpRequestMessage BuildRequest(HttpMethod method, RadarrSettings settings, string path)
    {
        // A null/blank BaseUrl (e.g. Radarr never configured) produces a relative URI here rather than
        // throwing — HttpClient.SendAsync then fails with a catchable InvalidOperationException instead
        // of crashing the request outside SendAsync's try/catch.
        var request = new HttpRequestMessage(method, $"{settings.BaseUrl?.TrimEnd('/') ?? string.Empty}{path}");
        request.Headers.Add("X-Api-Key", settings.ApiKey);
        return request;
    }

    private async Task<SonarrActionResult<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return SonarrActionResult<T>.Fail($"Radarr returned {(int)response.StatusCode} {response.ReasonPhrase}");

            if (typeof(T) == typeof(bool))
                return SonarrActionResult<T>.Ok((T)(object)true);

            var data = await response.Content.ReadFromJsonAsync<T>(s_jsonOptions, ct).ConfigureAwait(false);
            return data is null ? SonarrActionResult<T>.Fail("Radarr returned an empty response body") : SonarrActionResult<T>.Ok(data);
        }
        catch (Exception ex)
        {
            return SonarrActionResult<T>.Fail(ex.Message);
        }
    }

    /// <summary>Validates connectivity and API key against Radarr's system status endpoint.</summary>
    public Task<SonarrActionResult<bool>> TestConnectionAsync(RadarrSettings settings, CancellationToken ct = default) =>
        SendAsync<bool>(BuildRequest(HttpMethod.Get, settings, "/api/v3/system/status"), ct);

    /// <summary>Looks up Radarr movie candidates by free-text title — the only matching path for unowned suggestions, which have no TMDB link.</summary>
    public Task<SonarrActionResult<List<RadarrMovieLookupResult>>> LookupByTitleAsync(RadarrSettings settings, string title, CancellationToken ct = default) =>
        SendAsync<List<RadarrMovieLookupResult>>(BuildRequest(HttpMethod.Get, settings, $"/api/v3/movie/lookup?term={Uri.EscapeDataString(title)}"), ct);

    /// <summary>Adds a movie to Radarr, monitored, with an optional immediate search. Radarr has no granular monitor mode like Sonarr — just a flat monitored flag plus searchForMovie.</summary>
    public async Task<SonarrActionResult<int>> AddMovieAsync(RadarrSettings settings, int tmdbId, string title, int qualityProfileId, string rootFolderPath, bool searchOnAdd, CancellationToken ct = default)
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
        }, options: s_jsonOptions);

        var result = await SendAsync<JsonElement>(request, ct).ConfigureAwait(false);
        if (!result.Success)
            return SonarrActionResult<int>.Fail(result.ErrorMessage!);

        return result.Data.TryGetProperty("id", out var idProp)
            ? SonarrActionResult<int>.Ok(idProp.GetInt32())
            : SonarrActionResult<int>.Fail("Radarr's add-movie response did not contain an id.");
    }

    /// <summary>Gets Radarr's configured quality profiles, for the settings dropdown. Reuses SonarrQualityProfileResource — the shape is identical between Sonarr and Radarr's v3 API.</summary>
    public Task<SonarrActionResult<List<SonarrQualityProfileResource>>> GetQualityProfilesAsync(RadarrSettings settings, CancellationToken ct = default) =>
        SendAsync<List<SonarrQualityProfileResource>>(BuildRequest(HttpMethod.Get, settings, "/api/v3/qualityprofile"), ct);

    /// <summary>Gets Radarr's configured root folders, for the settings dropdown. Reuses SonarrRootFolderResource for the same reason.</summary>
    public Task<SonarrActionResult<List<SonarrRootFolderResource>>> GetRootFoldersAsync(RadarrSettings settings, CancellationToken ct = default) =>
        SendAsync<List<SonarrRootFolderResource>>(BuildRequest(HttpMethod.Get, settings, "/api/v3/rootfolder"), ct);
}
