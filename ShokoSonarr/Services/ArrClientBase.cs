using System.Net.Http.Json;
using System.Text.Json;
using ShokoSonarr.Models;

namespace ShokoSonarr.Services;

/// <summary>Shared request-building and error-handling logic for typed *arr-family (Sonarr/Radarr) v3 API clients. Never throws on HTTP/connectivity failure — all calls return a typed result.</summary>
public abstract class ArrClientBase(HttpClient httpClient)
{
    private protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private string ServiceName => GetType().Name.Replace("Client", "");

    private protected HttpRequestMessage BuildRequest(HttpMethod method, string? baseUrl, string? apiKey, string path)
    {
        // A null/blank BaseUrl (e.g. the service was never configured) produces a relative URI here rather
        // than throwing — HttpClient.SendAsync then fails with a catchable InvalidOperationException instead
        // of crashing the request outside SendAsync's try/catch.
        var request = new HttpRequestMessage(method, $"{baseUrl?.TrimEnd('/') ?? string.Empty}{path}");
        request.Headers.Add("X-Api-Key", apiKey);
        return request;
    }

    private protected async Task<SonarrActionResult<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return SonarrActionResult<T>.Fail($"{ServiceName} returned {(int)response.StatusCode} {response.ReasonPhrase}");

            if (typeof(T) == typeof(bool))
                return SonarrActionResult<T>.Ok((T)(object)true);

            var data = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
            return data is null ? SonarrActionResult<T>.Fail($"{ServiceName} returned an empty response body") : SonarrActionResult<T>.Ok(data);
        }
        catch (Exception ex)
        {
            return SonarrActionResult<T>.Fail(ex.Message);
        }
    }
}
