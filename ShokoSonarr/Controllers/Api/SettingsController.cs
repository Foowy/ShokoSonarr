using Microsoft.AspNetCore.Mvc;
using ShokoSonarr.Config;
using ShokoSonarr.Services;

namespace ShokoSonarr.Controllers.Api;

/// <summary>Endpoints for reading/writing Sonarr connection settings.</summary>
public class SettingsController(ScanCacheStore cacheStore, SonarrClient sonarrClient) : ShokoSonarrBaseController
{
    /// <summary>Gets the current Sonarr settings, with the API key masked.</summary>
    /// <returns>The current settings, API key redacted.</returns>
    [HttpGet]
    public IActionResult GetSettings()
    {
        var settings = cacheStore.GetSettings();
        var masked = new SonarrSettings
        {
            BaseUrl = settings.BaseUrl,
            ApiKey = string.IsNullOrEmpty(settings.ApiKey) ? null : new string('*', 8),
            ScanIntervalHours = settings.ScanIntervalHours,
            QualityProfileId = settings.QualityProfileId,
            RootFolderPath = settings.RootFolderPath,
            IncludeSpecials = settings.IncludeSpecials,
        };
        return Ok(new ApiResponse<SonarrSettings>(Success: true, Message: null, Data: masked));
    }

    /// <summary>Saves new Sonarr settings. If the incoming API key, quality profile, or root folder is blank/unset
    /// (e.g. the dashboard re-saved without re-testing the connection, which is the only way those dropdowns get
    /// populated), the previously-stored value is kept instead of being wiped.</summary>
    /// <param name="settings">The settings to save.</param>
    /// <returns>200 on success.</returns>
    [HttpPut]
    public IActionResult SaveSettings([FromBody] SonarrSettings settings)
    {
        var stored = cacheStore.GetSettings();
        if (string.IsNullOrEmpty(settings.ApiKey))
            settings.ApiKey = stored.ApiKey;
        if (settings.QualityProfileId is null)
            settings.QualityProfileId = stored.QualityProfileId;
        if (string.IsNullOrEmpty(settings.RootFolderPath))
            settings.RootFolderPath = stored.RootFolderPath;

        cacheStore.SaveSettings(settings);
        return Ok(new ApiResponse<object>(Success: true, Message: null, Data: null));
    }

    /// <summary>Tests connectivity to Sonarr using the given (not-yet-saved) settings.</summary>
    /// <param name="settings">The settings to test.</param>
    /// <returns>200 with success=true if reachable, success=false with an error message otherwise.</returns>
    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection([FromBody] SonarrSettings settings)
    {
        var result = await sonarrClient.TestConnectionAsync(settings);
        return Ok(new ApiResponse<object>(Success: result.Success, Message: result.ErrorMessage, Data: null));
    }

    /// <summary>Gets Sonarr's quality profiles and root folders, for the dashboard's settings dropdowns.</summary>
    /// <param name="settings">The settings to use for the lookup (not necessarily saved yet).</param>
    /// <returns>200 with the available quality profiles and root folders, or success=false with an error message.</returns>
    [HttpPost("sonarr-options")]
    public async Task<IActionResult> GetSonarrOptions([FromBody] SonarrSettings settings)
    {
        var profiles = await sonarrClient.GetQualityProfilesAsync(settings);
        if (!profiles.Success)
            return Ok(new ApiResponse<object>(Success: false, Message: profiles.ErrorMessage, Data: null));

        var rootFolders = await sonarrClient.GetRootFoldersAsync(settings);
        if (!rootFolders.Success)
            return Ok(new ApiResponse<object>(Success: false, Message: rootFolders.ErrorMessage, Data: null));

        return Ok(new ApiResponse<object>(Success: true, Message: null, Data: new { qualityProfiles = profiles.Data, rootFolders = rootFolders.Data }));
    }
}
