using ShokoSonarr.Config;
using ShokoSonarr.Models;

namespace ShokoSonarr.Services;

/// <summary>Resolution outcome for matching a Shoko series to a Sonarr series.</summary>
public record MatchResolution(bool AutoResolved, int? TvdbId, List<SonarrSeriesLookupResult> Candidates, string? ErrorMessage);

/// <summary>Resolves a Shoko series to a Sonarr TVDB ID — via the TMDB-linked TVDB ID first, falling back to a confirmable title search.</summary>
public class SeriesMatcher(SonarrClient sonarrClient)
{
    /// <summary>
    /// Resolves the given series to a Sonarr match. If <paramref name="series"/> already has a TVDB ID
    /// (resolved from its TMDB link by the scanner), this confirms it resolves in Sonarr and auto-resolves.
    /// Otherwise, falls back to a title search whose candidates require user confirmation.
    /// </summary>
    public async Task<MatchResolution> ResolveAsync(SonarrSettings settings, SeriesMissingResult series, CancellationToken ct = default)
    {
        if (series.TvdbId.HasValue)
        {
            var byTvdbId = await sonarrClient.LookupByTvdbIdAsync(settings, series.TvdbId.Value, ct).ConfigureAwait(false);
            if (byTvdbId.Success && byTvdbId.Data!.Count > 0)
                return new MatchResolution(AutoResolved: true, TvdbId: series.TvdbId, Candidates: [], ErrorMessage: null);
        }

        var byTitle = await sonarrClient.LookupByTitleAsync(settings, series.Title, ct).ConfigureAwait(false);
        if (byTitle.Success && byTitle.Data!.Count > 0)
            return new MatchResolution(AutoResolved: false, TvdbId: null, Candidates: byTitle.Data!, ErrorMessage: null);

        return new MatchResolution(AutoResolved: false, TvdbId: null, Candidates: [], ErrorMessage: "no Sonarr match available");
    }
}
