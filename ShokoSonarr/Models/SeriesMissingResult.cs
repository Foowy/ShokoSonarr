namespace ShokoSonarr.Models;

/// <summary>Missing-episode results for a single Shoko series.</summary>
public class SeriesMissingResult
{
    /// <summary>The Shoko series ID.</summary>
    public int ShokoSeriesId { get; set; }

    /// <summary>The series' preferred display title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Relative path to the series' poster image, if available.</summary>
    public string? PosterPath { get; set; }

    /// <summary>The resolved Sonarr TVDB ID, if a match was found (via TMDB link or confirmed title search). Null if no Sonarr match exists yet.</summary>
    public int? TvdbId { get; set; }

    /// <summary>The Shoko group's title this series belongs to, if any. Used to propagate a franchise tag onto the Sonarr series.</summary>
    public string? GroupTitle { get; set; }

    /// <summary>This series' specials override, if one is set (null means it inherits the global default). Mirrors <see cref="Services.ScanCacheStore.GetSeriesOverride"/> for the dashboard to render the current state without a separate call.</summary>
    public bool? IncludeSpecialsOverride { get; set; }

    /// <summary>This series' Sonarr quality-profile override, if one is set (null means it inherits the global default). Mirrors <see cref="IncludeSpecialsOverride"/>'s pattern.</summary>
    public int? QualityProfileIdOverride { get; set; }

    /// <summary>This series' Sonarr root-folder override, if one is set (null means it inherits the global default).</summary>
    public string? RootFolderPathOverride { get; set; }

    /// <summary>The list of missing episodes for this series.</summary>
    public List<MissingEpisodeInfo> MissingEpisodes { get; set; } = [];
}
