namespace ShokoSonarr.Models;

/// <summary>A per-series override for whether specials are included when scanning for missing episodes.</summary>
public class SeriesOverride
{
    /// <summary>The Shoko series ID this override applies to.</summary>
    public int ShokoSeriesId { get; set; }

    /// <summary>True/false to force include/exclude specials for this series; null means "no override, use the global default" and is never stored (see <see cref="Services.ScanCacheStore.SetSeriesOverride"/>).</summary>
    public bool? IncludeSpecials { get; set; }

    /// <summary>Sonarr quality profile ID to use when adding this series, overriding the global default. Null means "no override".</summary>
    public int? QualityProfileId { get; set; }

    /// <summary>Sonarr root folder path to use when adding this series, overriding the global default. Null means "no override".</summary>
    public string? RootFolderPath { get; set; }
}
