using Shoko.Abstractions.Actions;
using ShokoSonarr.Services;

namespace ShokoSonarr.Actions;

/// <summary>Runs a missing-episode scan immediately and persists the result as the current dashboard snapshot. Native equivalent of the dashboard's "Run Scan" button.</summary>
public class TriggerScanAction(MissingEpisodeScanner scanner, ScanCacheStore cacheStore) : IExecutableAction
{
    /// <inheritdoc/>
    public string Name => "Scan for Missing Episodes";

    /// <inheritdoc/>
    public string? Description => "Scans the Shoko collection for missing episodes and refreshes the ShokoSonarr dashboard.";

    /// <inheritdoc/>
    public ActionCategory Category => ActionCategory.PluginInferred;

    /// <inheritdoc/>
    public ActionPermission Permission => ActionPermission.Admin;

    /// <inheritdoc/>
    public async Task Execute(CancellationToken token = default)
    {
        var snapshot = await scanner.ScanAsync().ConfigureAwait(false);
        cacheStore.SaveScan(snapshot);
    }
}
