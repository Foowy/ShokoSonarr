namespace ShokoSonarr.Config;

/// <summary>User-configured connection details for Radarr integration (movie-type anime bridging).</summary>
public class RadarrSettings
{
    /// <summary>Base URL of the Radarr instance, e.g. "http://192.168.1.60:7878".</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Radarr API key (from Settings > General in Radarr's UI).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Radarr quality profile ID to use when adding new movies. Must be set before movies can be added.</summary>
    public int? QualityProfileId { get; set; }

    /// <summary>Radarr root folder path to use when adding new movies. Must be set before movies can be added.</summary>
    public string? RootFolderPath { get; set; }
}
