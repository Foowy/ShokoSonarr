namespace ShokoSonarr.Models;

/// <summary>A single missing episode identified for a series.</summary>
public class MissingEpisodeInfo
{
    /// <summary>The AniDB episode ID, used as the stable key for action-status tracking.</summary>
    public int AnidbEpisodeId { get; set; }

    /// <summary>The episode number within its type (normal or special).</summary>
    public int EpisodeNumber { get; set; }

    /// <summary>Whether this is a normal episode or a special.</summary>
    public bool IsSpecial { get; set; }

    /// <summary>Episode title, if known.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The date the episode aired, if known.</summary>
    public DateOnly? AirDate { get; set; }

    /// <summary>Current action status: "none", or "search-triggered" if a pending Sonarr search is recorded for this episode (see <see cref="Services.ScanCacheStore.GetPendingSearches"/>).</summary>
    public string ActionStatus { get; set; } = "none";
}
