namespace ShokoSonarr.Models;

/// <summary>A single candidate result from Radarr's movie lookup endpoint.</summary>
public class RadarrMovieLookupResult
{
    /// <summary>The candidate's TMDB ID.</summary>
    public int TmdbId { get; set; }

    /// <summary>The candidate's title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The candidate's release year, if known.</summary>
    public int? Year { get; set; }
}
