using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ShokoSonarr.Config;
using ShokoSonarr.Models;

namespace ShokoSonarr.Services;

/// <summary>Sonarr episode resource, as returned by Sonarr's v3 API.</summary>
public record SonarrEpisodeResource(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("seasonNumber")] int SeasonNumber,
    [property: JsonPropertyName("episodeNumber")] int EpisodeNumber);

/// <summary>Sonarr quality profile resource, as returned by Sonarr's v3 API.</summary>
public record SonarrQualityProfileResource(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name);

/// <summary>Sonarr root folder resource, as returned by Sonarr's v3 API.</summary>
public record SonarrRootFolderResource(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("path")] string Path);

/// <summary>Minimal Sonarr series resource, used only to detect whether a series already exists.</summary>
public record SonarrSeriesResource([property: JsonPropertyName("id")] int Id);

/// <summary>Sonarr tag resource, as returned by Sonarr's v3 API.</summary>
public record SonarrTagResource([property: JsonPropertyName("id")] int Id, [property: JsonPropertyName("label")] string Label);

/// <summary>Typed HTTP client for Sonarr's v3 API. Never throws on HTTP/connectivity failure — all calls return a typed result.</summary>
public class SonarrClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private HttpRequestMessage BuildRequest(HttpMethod method, SonarrSettings settings, string path)
    {
        // A null/blank BaseUrl (e.g. Sonarr never configured) produces a relative URI here rather than
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
                return SonarrActionResult<T>.Fail($"Sonarr returned {(int)response.StatusCode} {response.ReasonPhrase}");

            if (typeof(T) == typeof(bool))
                return SonarrActionResult<T>.Ok((T)(object)true);

            var data = await response.Content.ReadFromJsonAsync<T>(s_jsonOptions, ct).ConfigureAwait(false);
            return data is null ? SonarrActionResult<T>.Fail("Sonarr returned an empty response body") : SonarrActionResult<T>.Ok(data);
        }
        catch (Exception ex)
        {
            return SonarrActionResult<T>.Fail(ex.Message);
        }
    }

    /// <summary>Validates connectivity and API key against Sonarr's system status endpoint.</summary>
    public Task<SonarrActionResult<bool>> TestConnectionAsync(SonarrSettings settings, CancellationToken ct = default) =>
        SendAsync<bool>(BuildRequest(HttpMethod.Get, settings, "/api/v3/system/status"), ct);

    /// <summary>Looks up Sonarr series candidates by TVDB ID.</summary>
    public Task<SonarrActionResult<List<SonarrSeriesLookupResult>>> LookupByTvdbIdAsync(SonarrSettings settings, int tvdbId, CancellationToken ct = default) =>
        SendAsync<List<SonarrSeriesLookupResult>>(BuildRequest(HttpMethod.Get, settings, $"/api/v3/series/lookup?term={Uri.EscapeDataString($"tvdb:{tvdbId}")}"), ct);

    /// <summary>Looks up Sonarr series candidates by free-text title, for series with no TMDB-linked TVDB ID.</summary>
    public Task<SonarrActionResult<List<SonarrSeriesLookupResult>>> LookupByTitleAsync(SonarrSettings settings, string title, CancellationToken ct = default) =>
        SendAsync<List<SonarrSeriesLookupResult>>(BuildRequest(HttpMethod.Get, settings, $"/api/v3/series/lookup?term={Uri.EscapeDataString(title)}"), ct);

    /// <summary>Adds a series to Sonarr. Defaults to monitoring disabled and no immediate search — the owned-series flow explicitly monitors and searches only specific missing episodes afterward. Pass monitorMode "all" and searchOnAdd true for the discovery flow (adding a wholly unowned series), which has no per-episode missing data to act on selectively.</summary>
    public async Task<SonarrActionResult<int>> AddSeriesAsync(SonarrSettings settings, int tvdbId, string title, int qualityProfileId, string rootFolderPath, string monitorMode = "none", bool searchOnAdd = false, List<int>? tagIds = null, CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Post, settings, "/api/v3/series");
        request.Content = JsonContent.Create(new
        {
            tvdbId,
            title,
            qualityProfileId,
            rootFolderPath,
            monitored = true,
            tags = tagIds ?? [],
            addOptions = new { monitor = monitorMode, searchForMissingEpisodes = searchOnAdd },
        }, options: s_jsonOptions);

        var result = await SendAsync<JsonElement>(request, ct).ConfigureAwait(false);
        if (!result.Success)
            return SonarrActionResult<int>.Fail(result.ErrorMessage!);

        return result.Data.TryGetProperty("id", out var idProp)
            ? SonarrActionResult<int>.Ok(idProp.GetInt32())
            : SonarrActionResult<int>.Fail("Sonarr's add-series response did not contain an id.");
    }

    /// <summary>Gets Sonarr's configured quality profiles, for the user to choose one during settings setup.</summary>
    public Task<SonarrActionResult<List<SonarrQualityProfileResource>>> GetQualityProfilesAsync(SonarrSettings settings, CancellationToken ct = default) =>
        SendAsync<List<SonarrQualityProfileResource>>(BuildRequest(HttpMethod.Get, settings, "/api/v3/qualityprofile"), ct);

    /// <summary>Gets Sonarr's configured root folders, for the user to choose one during settings setup.</summary>
    public Task<SonarrActionResult<List<SonarrRootFolderResource>>> GetRootFoldersAsync(SonarrSettings settings, CancellationToken ct = default) =>
        SendAsync<List<SonarrRootFolderResource>>(BuildRequest(HttpMethod.Get, settings, "/api/v3/rootfolder"), ct);

    /// <summary>Looks up a series already added to Sonarr by TVDB ID (as opposed to <see cref="LookupByTvdbIdAsync"/>, which searches TheTVDB regardless of whether it's already added).</summary>
    public Task<SonarrActionResult<List<SonarrSeriesResource>>> GetExistingSeriesByTvdbIdAsync(SonarrSettings settings, int tvdbId, CancellationToken ct = default) =>
        SendAsync<List<SonarrSeriesResource>>(BuildRequest(HttpMethod.Get, settings, $"/api/v3/series?tvdbId={tvdbId}"), ct);

    /// <summary>Gets all of Sonarr's configured tags.</summary>
    public Task<SonarrActionResult<List<SonarrTagResource>>> GetTagsAsync(SonarrSettings settings, CancellationToken ct = default) =>
        SendAsync<List<SonarrTagResource>>(BuildRequest(HttpMethod.Get, settings, "/api/v3/tag"), ct);

    /// <summary>Creates a new Sonarr tag with the given label.</summary>
    public async Task<SonarrActionResult<SonarrTagResource>> CreateTagAsync(SonarrSettings settings, string label, CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Post, settings, "/api/v3/tag");
        request.Content = JsonContent.Create(new { label }, options: s_jsonOptions);
        return await SendAsync<SonarrTagResource>(request, ct).ConfigureAwait(false);
    }

    /// <summary>Finds an existing tag matching the label (case-insensitive), or creates one if none exists.</summary>
    public async Task<SonarrActionResult<int>> EnsureTagIdAsync(SonarrSettings settings, string label, CancellationToken ct = default)
    {
        var existing = await GetTagsAsync(settings, ct).ConfigureAwait(false);
        if (!existing.Success)
            return SonarrActionResult<int>.Fail(existing.ErrorMessage!);

        var match = existing.Data!.FirstOrDefault(t => string.Equals(t.Label, label, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            return SonarrActionResult<int>.Ok(match.Id);

        var created = await CreateTagAsync(settings, label, ct).ConfigureAwait(false);
        return created.Success ? SonarrActionResult<int>.Ok(created.Data!.Id) : SonarrActionResult<int>.Fail(created.ErrorMessage!);
    }

    /// <summary>Adds a tag to an existing Sonarr series if not already present. Sonarr requires a full-resource PUT for updates, so this fetches the series as a mutable JSON node, adds the tag ID into its "tags" array if missing, and PUTs the whole node back unchanged otherwise.</summary>
    public async Task<SonarrActionResult<bool>> UpdateSeriesTagAsync(SonarrSettings settings, int sonarrSeriesId, int tagId, CancellationToken ct = default)
    {
        var getResult = await SendAsync<JsonNode>(BuildRequest(HttpMethod.Get, settings, $"/api/v3/series/{sonarrSeriesId}"), ct).ConfigureAwait(false);
        if (!getResult.Success)
            return SonarrActionResult<bool>.Fail(getResult.ErrorMessage!);

        var series = getResult.Data!;
        var tags = series["tags"]?.AsArray() ?? [];
        if (!tags.Any(t => t!.GetValue<int>() == tagId))
            tags.Add(tagId);
        series["tags"] = tags;

        var putRequest = BuildRequest(HttpMethod.Put, settings, $"/api/v3/series/{sonarrSeriesId}");
        putRequest.Content = JsonContent.Create(series, options: s_jsonOptions);
        return await SendAsync<bool>(putRequest, ct).ConfigureAwait(false);
    }

    /// <summary>Gets all episodes for a Sonarr series (used to map AniDB episode numbers to Sonarr episode IDs).</summary>
    public Task<SonarrActionResult<List<SonarrEpisodeResource>>> GetEpisodesAsync(SonarrSettings settings, int sonarrSeriesId, CancellationToken ct = default) =>
        SendAsync<List<SonarrEpisodeResource>>(BuildRequest(HttpMethod.Get, settings, $"/api/v3/episode?seriesId={sonarrSeriesId}"), ct);

    /// <summary>Sets the given episodes to monitored, without touching any other episode's monitored state.</summary>
    public Task<SonarrActionResult<bool>> MonitorEpisodesAsync(SonarrSettings settings, List<int> sonarrEpisodeIds, CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Put, settings, "/api/v3/episode/monitor");
        request.Content = JsonContent.Create(new { episodeIds = sonarrEpisodeIds, monitored = true }, options: s_jsonOptions);
        return SendAsync<bool>(request, ct);
    }

    /// <summary>Sets the given episodes to unmonitored, without touching any other episode's monitored state. Used to reconcile episodes Shoko has already imported so Sonarr's own automatic/RSS search stops re-fetching them.</summary>
    public virtual Task<SonarrActionResult<bool>> UnmonitorEpisodesAsync(SonarrSettings settings, List<int> sonarrEpisodeIds, CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Put, settings, "/api/v3/episode/monitor");
        request.Content = JsonContent.Create(new { episodeIds = sonarrEpisodeIds, monitored = false }, options: s_jsonOptions);
        return SendAsync<bool>(request, ct);
    }

    /// <summary>Triggers Sonarr's EpisodeSearch command for the given episodes.</summary>
    public Task<SonarrActionResult<bool>> TriggerEpisodeSearchAsync(SonarrSettings settings, List<int> sonarrEpisodeIds, CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Post, settings, "/api/v3/command");
        request.Content = JsonContent.Create(new { name = "EpisodeSearch", episodeIds = sonarrEpisodeIds }, options: s_jsonOptions);
        return SendAsync<bool>(request, ct);
    }
}
