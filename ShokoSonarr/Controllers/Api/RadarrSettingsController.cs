using Microsoft.AspNetCore.Mvc;
using ShokoSonarr.Config;
using ShokoSonarr.Services;

namespace ShokoSonarr.Controllers.Api;

/// <summary>Endpoints for reading/writing Radarr connection settings. Mirrors SettingsController's shape for the Sonarr equivalent.</summary>
public class RadarrSettingsController(ScanCacheStore cacheStore, RadarrClient radarrClient) : ShokoSonarrBaseController
{
    /// <summary>Gets the current Radarr settings, with the API key masked.</summary>
    [HttpGet]
    public IActionResult GetSettings()
    {
        var settings = cacheStore.GetRadarrSettings();
        var masked = new RadarrSettings
        {
            BaseUrl = settings.BaseUrl,
            ApiKey = string.IsNullOrEmpty(settings.ApiKey) ? null : new string('*', 8),
            QualityProfileId = settings.QualityProfileId,
            RootFolderPath = settings.RootFolderPath,
        };
        return Ok(new ApiResponse<RadarrSettings>(Success: true, Message: null, Data: masked));
    }

    /// <summary>Saves new Radarr settings. A blank API key, quality profile, or root folder preserves the previously-stored value, same as SettingsController.SaveSettings.</summary>
    [HttpPut]
    public IActionResult SaveSettings([FromBody] RadarrSettings settings)
    {
        var stored = cacheStore.GetRadarrSettings();
        if (string.IsNullOrEmpty(settings.ApiKey))
            settings.ApiKey = stored.ApiKey;
        if (settings.QualityProfileId is null)
            settings.QualityProfileId = stored.QualityProfileId;
        if (string.IsNullOrEmpty(settings.RootFolderPath))
            settings.RootFolderPath = stored.RootFolderPath;

        cacheStore.SaveRadarrSettings(settings);
        return Ok(new ApiResponse<object>(Success: true, Message: null, Data: null));
    }

    /// <summary>Tests connectivity to Radarr using the given (not-yet-saved) settings. A blank API key falls back to the stored one.</summary>
    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection([FromBody] RadarrSettings settings)
    {
        if (string.IsNullOrEmpty(settings.ApiKey))
            settings.ApiKey = cacheStore.GetRadarrSettings().ApiKey;

        var result = await radarrClient.TestConnectionAsync(settings);
        return Ok(new ApiResponse<object>(Success: result.Success, Message: result.ErrorMessage, Data: null));
    }

    /// <summary>Gets Radarr's quality profiles and root folders, for the dashboard's settings dropdowns.</summary>
    [HttpPost("radarr-options")]
    public async Task<IActionResult> GetRadarrOptions([FromBody] RadarrSettings settings)
    {
        if (string.IsNullOrEmpty(settings.ApiKey))
            settings.ApiKey = cacheStore.GetRadarrSettings().ApiKey;

        var profiles = await radarrClient.GetQualityProfilesAsync(settings);
        if (!profiles.Success)
            return Ok(new ApiResponse<object>(Success: false, Message: profiles.ErrorMessage, Data: null));

        var rootFolders = await radarrClient.GetRootFoldersAsync(settings);
        if (!rootFolders.Success)
            return Ok(new ApiResponse<object>(Success: false, Message: rootFolders.ErrorMessage, Data: null));

        return Ok(new ApiResponse<object>(Success: true, Message: null, Data: new { qualityProfiles = profiles.Data, rootFolders = rootFolders.Data }));
    }
}
