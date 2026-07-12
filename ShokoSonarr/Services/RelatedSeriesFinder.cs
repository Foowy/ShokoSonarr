using Shoko.Abstractions.Metadata.Enums;
using Shoko.Abstractions.Metadata.Services;
using ShokoSonarr.Models;

namespace ShokoSonarr.Services;

/// <summary>Finds AniDB-related series the user owns zero episodes of, surfaced as read-only dashboard suggestions.</summary>
public class RelatedSeriesFinder(IMetadataService metadataService)
{
    private static readonly RelationType[] s_coreStoryRelations =
    [
        RelationType.Sequel,
        RelationType.Prequel,
        RelationType.SideStory,
        RelationType.MainStory,
        RelationType.FullStory,
        RelationType.Summary,
    ];

    /// <summary>Computes the current suggestion list from Shoko's already-cached AniDB relation data. No AniDB calls, no persistence.</summary>
    public List<RelatedSeriesSuggestion> FindSuggestions()
    {
        var suggestions = new List<RelatedSeriesSuggestion>();
        var seenRelatedIds = new HashSet<int>();

        foreach (var series in metadataService.GetAllShokoSeries())
        {
            foreach (var relation in series.RelatedSeries)
            {
                if (!s_coreStoryRelations.Contains(relation.RelationType))
                    continue;

                var related = relation.Related;
                if (related is null)
                    continue;

                if (related.ShokoSeriesIDs.Count > 0)
                    continue;

                if (!seenRelatedIds.Add(related.ID))
                    continue;

                suggestions.Add(new RelatedSeriesSuggestion
                {
                    OwningShokoSeriesId = series.ID,
                    OwningSeriesTitle = series.Title,
                    RelationType = relation.RelationType.ToString(),
                    RelatedTitle = related.Title,
                    RelatedType = related.Type.ToString(),
                });
            }
        }

        return suggestions;
    }
}
