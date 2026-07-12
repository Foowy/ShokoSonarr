namespace ShokoSonarr.Models;

/// <summary>How a search-history entry was resolved.</summary>
public enum SearchHistoryOutcome
{
    /// <summary>The plugin told Sonarr to monitor and search for the episode.</summary>
    Triggered,

    /// <summary>Shoko confirmed the episode was imported and the plugin told Sonarr to stop chasing it.</summary>
    Imported,

    /// <summary>The user cancelled the pending search from the dashboard.</summary>
    Cancelled,

    /// <summary>Reconciliation kept failing (e.g. the Sonarr episode was deleted out-of-band) until the pending entry's max age was reached.</summary>
    Expired,
}

/// <summary>A record of a search-related event for a specific episode, kept for operational visibility after a <see cref="PendingSearch"/> is resolved one way or another.</summary>
public class SearchHistoryEntry
{
    /// <summary>LiteDB auto-increment primary key. Used (not <see cref="TimestampUtc"/>) to determine pruning order, since multiple entries from the same call (e.g. a multi-episode search trigger) can share an identical timestamp.</summary>
    public int Id { get; set; }

    /// <summary>The Shoko series ID.</summary>
    public int ShokoSeriesId { get; set; }

    /// <summary>The series title at the time of the event.</summary>
    public string SeriesTitle { get; set; } = string.Empty;

    /// <summary>The AniDB episode ID.</summary>
    public int AnidbEpisodeId { get; set; }

    /// <summary>The episode title at the time of the event.</summary>
    public string EpisodeTitle { get; set; } = string.Empty;

    /// <summary>What happened.</summary>
    public SearchHistoryOutcome Outcome { get; set; }

    /// <summary>When this event occurred, in UTC.</summary>
    public DateTime TimestampUtc { get; set; }
}
