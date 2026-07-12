namespace ShokoSonarr.Models;

/// <summary>An AniDB-related series the user owns zero episodes of, suggested from a series they do own.</summary>
public class RelatedSeriesSuggestion
{
    /// <summary>The Shoko series ID of the owned series this suggestion was found through.</summary>
    public int OwningShokoSeriesId { get; set; }

    /// <summary>The owned series' preferred display title.</summary>
    public string OwningSeriesTitle { get; set; } = string.Empty;

    /// <summary>How the suggested series relates to the owned one (e.g. "Sequel", "SideStory").</summary>
    public string RelationType { get; set; } = string.Empty;

    /// <summary>The unowned related series' preferred display title.</summary>
    public string RelatedTitle { get; set; } = string.Empty;

    /// <summary>The unowned related series' AniDB type (e.g. "Movie", "TV", "OVA"). Used to route movie-type suggestions to Radarr instead of Sonarr.</summary>
    public string RelatedType { get; set; } = string.Empty;
}
