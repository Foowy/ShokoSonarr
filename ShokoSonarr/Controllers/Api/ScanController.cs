using Microsoft.AspNetCore.Mvc;
using ShokoSonarr.Services;

namespace ShokoSonarr.Controllers.Api;

/// <summary>Endpoints for running and reading missing-episode scans.</summary>
public class ScanController(MissingEpisodeScanner scanner, ScanCacheStore cacheStore) : ShokoSonarrBaseController
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

    /// <summary>Gets the most recently computed scan snapshot.</summary>
    /// <returns>The cached scan snapshot, or 204 if no scan has run yet.</returns>
    [HttpGet("results")]
    public IActionResult GetResults()
    {
        var snapshot = cacheStore.GetLastScan();
        return snapshot is null ? NoContent() : Ok(new ApiResponse<object>(Success: true, Message: null, Data: snapshot));
    }
}
