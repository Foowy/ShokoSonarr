using Shoko.Abstractions.Metadata.Enums;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Abstractions.Metadata.Shoko;
using ShokoSonarr.Models;

namespace ShokoSonarr.Services;

/// <summary>Scans the Shoko collection for missing episodes on already-inventoried series.</summary>
public class MissingEpisodeScanner(IMetadataService metadataService, ScanCacheStore cacheStore)
{
    /// <summary>Runs a full scan and returns a snapshot of all series with at least one missing episode.</summary>
    public Task<ScanSnapshot> ScanAsync()
    {
        var results = new List<SeriesMissingResult>();
        var globalIncludeSpecials = cacheStore.GetSettings().IncludeSpecials;

        foreach (var series in metadataService.GetAllShokoSeries())
        {
            // Only consider series already inventoried (v1 scope excludes fully-unowned anime).
            if (series.LocalEpisodeCounts.Episodes + series.LocalEpisodeCounts.Specials <= 0)
                continue;

            var overrideValue = cacheStore.GetSeriesOverride(series.ID)?.IncludeSpecials;
            var includeSpecials = overrideValue ?? globalIncludeSpecials;
            var scannedTypes = includeSpecials
                ? new[] { EpisodeType.Episode, EpisodeType.Special }
                : [EpisodeType.Episode];

            var missing = series.Episodes
                .Where(e => scannedTypes.Contains(e.Type) && !e.IsHidden && e.VideoList.Count == 0)
                .Select(e => new MissingEpisodeInfo
                {
                    AnidbEpisodeId = e.AnidbEpisodeID,
                    EpisodeNumber = e.EpisodeNumber,
                    IsSpecial = e.Type == EpisodeType.Special,
                    Title = e.Title,
                    AirDate = e.AirDate,
                })
                .OrderBy(e => e.IsSpecial).ThenBy(e => e.EpisodeNumber)
                .ToList();

            if (missing.Count == 0)
                continue;

            var tvdbId = (series.TmdbShows ?? [])
                .Select(s => s.TvdbShowID)
                .FirstOrDefault(id => id.HasValue);

            results.Add(new SeriesMissingResult
            {
                ShokoSeriesId = series.ID,
                Title = series.Title,
                TvdbId = tvdbId,
                IncludeSpecialsOverride = overrideValue,
                MissingEpisodes = missing,
            });
        }

        return Task.FromResult(new ScanSnapshot
        {
            ScannedAtUtc = DateTime.UtcNow,
            Series = [.. results.OrderByDescending(s => s.MissingEpisodes.Count)],
        });
    }
}
