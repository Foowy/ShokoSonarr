using Shoko.Abstractions.Actions;
using ShokoSonarr.Models;
using ShokoSonarr.Services;

namespace ShokoSonarr.Actions;

/// <summary>
/// Triggers a Sonarr search for every currently-missing episode of this series, per the last scan snapshot.
/// Native equivalent of the dashboard's per-series "Search" button, scoped to all missing episodes rather
/// than a hand-picked subset — for per-episode control, use the dashboard directly.
/// </summary>
public class SearchMissingEpisodesAction(SeriesMatcher matcher, SonarrClient sonarrClient, ScanCacheStore cacheStore) : SeriesAction
{
    /// <inheritdoc/>
    public override string Name => "Search Missing Episodes in Sonarr";

    /// <inheritdoc/>
    public string? Description => "Triggers a Sonarr search for every episode this series is currently missing.";

    /// <inheritdoc/>
    public ActionCategory Category => ActionCategory.PluginInferred;

    /// <inheritdoc/>
    public override ActionPermission Permission => ActionPermission.Admin;

    /// <inheritdoc/>
    public async Task<ActionValidationResult?> Validate(CancellationToken token = default)
    {
        var series = FindSeries();
        if (series is null)
            return new ActionValidationResult("No missing episodes for this series in the last scan — run a scan first.");

        var (settings, resolvedTvdbId, error) = await ResolveAsync(series, token).ConfigureAwait(false);
        return error is null ? null : new ActionValidationResult(error);
    }

    /// <inheritdoc/>
    public override async Task Execute(CancellationToken token = default)
    {
        var series = FindSeries() ?? throw new InvalidOperationException("No missing episodes for this series in the last scan.");
        var (settings, tvdbId, error) = await ResolveAsync(series, token).ConfigureAwait(false);
        if (error is not null)
            throw new InvalidOperationException(error);

        var existing = await sonarrClient.GetExistingSeriesByTvdbIdAsync(settings, tvdbId, token).ConfigureAwait(false);
        if (!existing.Success || existing.Data!.Count == 0)
            throw new InvalidOperationException("Series is confirmed in Sonarr's lookup but not yet added — use the ShokoSonarr dashboard to add it first.");

        var anidbEpisodeIds = series.MissingEpisodes.Select(e => e.AnidbEpisodeId).ToList();
        var result = await matcher.MonitorAndSearchAsync(settings, series.ShokoSeriesId, existing.Data[0].Id, anidbEpisodeIds, series, token).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException(result.ErrorMessage);
    }

    /// <summary>Resolves this series' Sonarr TVDB ID, if it has a confirmed (not merely candidate) match.</summary>
    private async Task<(Config.SonarrSettings Settings, int TvdbId, string? Error)> ResolveAsync(SeriesMissingResult series, CancellationToken token)
    {
        var settings = cacheStore.GetSettings();
        if (series.TvdbId is null)
            return (settings, 0, "No Sonarr match for this series yet — use the ShokoSonarr dashboard to resolve or confirm one first.");

        var resolution = await matcher.ResolveAsync(settings, series, token).ConfigureAwait(false);
        return resolution.AutoResolved && resolution.TvdbId is { } tvdbId
            ? (settings, tvdbId, null)
            : (settings, 0, resolution.ErrorMessage ?? "This series isn't confirmed in Sonarr yet — use the ShokoSonarr dashboard to resolve or confirm a match first.");
    }

    private SeriesMissingResult? FindSeries() =>
        cacheStore.GetLastScan()?.Series.Find(s => s.ShokoSeriesId == Series.ID);
}
