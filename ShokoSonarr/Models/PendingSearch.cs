namespace ShokoSonarr.Models;

/// <summary>An episode the plugin has told Sonarr to monitor and search for, pending confirmation (via a later scan) that Shoko has actually imported it.</summary>
public class PendingSearch
{
    /// <summary>The Shoko series ID.</summary>
    public int ShokoSeriesId { get; set; }

    /// <summary>The AniDB episode ID — the stable key used to match this entry back to a scan result.</summary>
    public int AnidbEpisodeId { get; set; }

    /// <summary>The Sonarr series ID this episode belongs to.</summary>
    public int SonarrSeriesId { get; set; }

    /// <summary>The Sonarr episode ID to unmonitor once Shoko confirms the episode is no longer missing.</summary>
    public int SonarrEpisodeId { get; set; }

    /// <summary>When the search was triggered, in UTC.</summary>
    public DateTime TriggeredAtUtc { get; set; }
}
