namespace ShokoSonarr.Config;

/// <summary>User-configured connection details and scan cadence for Sonarr integration.</summary>
public class SonarrSettings
{
    /// <summary>Base URL of the Sonarr instance, e.g. "http://192.168.1.40:8989".</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Sonarr API key (from Settings > General in Sonarr's UI).</summary>
    public string? ApiKey { get; set; }

    /// <summary>How often the background scan job runs, in hours. Set to 0 to disable the scheduled scan.</summary>
    public int ScanIntervalHours { get; set; } = 24;

    /// <summary>Sonarr quality profile ID to use when adding new series. Must be set before series can be added.</summary>
    public int? QualityProfileId { get; set; }

    /// <summary>Sonarr root folder path to use when adding new series. Must be set before series can be added.</summary>
    public string? RootFolderPath { get; set; }

    /// <summary>Whether to include specials when scanning for missing episodes, unless a series has its own override. Defaults to true (matches pre-existing behavior).</summary>
    public bool IncludeSpecials { get; set; } = true;
}
