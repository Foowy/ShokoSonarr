using LiteDB;
using ShokoSonarr.Config;
using ShokoSonarr.Models;

namespace ShokoSonarr.Services;

/// <summary>LiteDB-backed persistence for plugin settings, scan snapshots, and per-episode action status.
/// Registered as a DI singleton; holds one shared <see cref="LiteDatabase"/> connection for the plugin's
/// lifetime, since LiteDB's direct-mode connections take an exclusive file lock per instance and opening
/// one per call would throw <see cref="IOException"/> under concurrent access (scheduler + API requests).</summary>
public class ScanCacheStore : IDisposable
{
    private const string SettingsCollectionName = "settings";
    private const string ScanCollectionName = "scans";
    private const string SeriesOverridesCollectionName = "seriesOverrides";
    private const string PendingSearchesCollectionName = "pendingSearches";
    private const int SettingsDocumentId = 1;
    private const int ScanDocumentId = 1;

    private readonly LiteDatabase _db;

    /// <summary>Creates a store rooted at the given data directory, creating the directory if needed.</summary>
    /// <param name="dataPath">The host's data directory (typically <see cref="Shoko.Abstractions.Plugin.IApplicationPaths.DataPath"/>).</param>
    public ScanCacheStore(string dataPath)
    {
        var pluginDir = Path.Combine(dataPath, "plugins", ShokoSonarrConstants.PluginDataSubfolder);
        Directory.CreateDirectory(pluginDir);
        _db = new LiteDatabase(Path.Combine(pluginDir, ShokoSonarrConstants.LiteDbFileName));
    }

    /// <inheritdoc/>
    public void Dispose() => _db.Dispose();

    /// <summary>Gets the current Sonarr settings, or defaults if none have been saved.</summary>
    public SonarrSettings GetSettings()
    {
        var col = _db.GetCollection<SettingsDocument>(SettingsCollectionName);
        var doc = col.FindById(SettingsDocumentId);
        return doc?.Settings ?? new SonarrSettings();
    }

    /// <summary>Persists Sonarr settings, replacing any previously saved settings.</summary>
    public void SaveSettings(SonarrSettings settings)
    {
        var col = _db.GetCollection<SettingsDocument>(SettingsCollectionName);
        col.Upsert(new SettingsDocument { Id = SettingsDocumentId, Settings = settings });
    }

    /// <summary>Gets the most recent scan snapshot, or null if no scan has run yet.</summary>
    public ScanSnapshot? GetLastScan()
    {
        var col = _db.GetCollection<ScanDocument>(ScanCollectionName);
        return col.FindById(ScanDocumentId)?.Snapshot;
    }

    /// <summary>Persists a scan snapshot, replacing any previous snapshot wholesale.</summary>
    public void SaveScan(ScanSnapshot snapshot)
    {
        var col = _db.GetCollection<ScanDocument>(ScanCollectionName);
        col.Upsert(new ScanDocument { Id = ScanDocumentId, Snapshot = snapshot });
    }

    /// <summary>Gets the specials override for a series, or null if no override is set (inherit the global default).</summary>
    public SeriesOverride? GetSeriesOverride(int shokoSeriesId)
    {
        var col = _db.GetCollection<SeriesOverride>(SeriesOverridesCollectionName);
        return col.Find(o => o.ShokoSeriesId == shokoSeriesId).FirstOrDefault();
    }

    /// <summary>Sets (or clears, when <paramref name="includeSpecials"/> is null) the specials override for a series.</summary>
    public void SetSeriesOverride(int shokoSeriesId, bool? includeSpecials)
    {
        var col = _db.GetCollection<SeriesOverride>(SeriesOverridesCollectionName);
        col.DeleteMany(o => o.ShokoSeriesId == shokoSeriesId);
        if (includeSpecials is not null)
            col.Insert(new SeriesOverride { ShokoSeriesId = shokoSeriesId, IncludeSpecials = includeSpecials });
    }

    /// <summary>Records that an episode's search was triggered in Sonarr, replacing any existing pending entry for the same episode.</summary>
    public void AddPendingSearch(PendingSearch entry)
    {
        var col = _db.GetCollection<PendingSearch>(PendingSearchesCollectionName);
        col.DeleteMany(p => p.ShokoSeriesId == entry.ShokoSeriesId && p.AnidbEpisodeId == entry.AnidbEpisodeId);
        col.Insert(entry);
    }

    /// <summary>Gets all episodes currently pending reconciliation with Sonarr.</summary>
    public List<PendingSearch> GetPendingSearches()
    {
        var col = _db.GetCollection<PendingSearch>(PendingSearchesCollectionName);
        return col.FindAll().ToList();
    }

    /// <summary>Removes a pending search entry once it's been reconciled (or is being replaced — see <see cref="AddPendingSearch"/>).</summary>
    public void RemovePendingSearch(int shokoSeriesId, int anidbEpisodeId)
    {
        var col = _db.GetCollection<PendingSearch>(PendingSearchesCollectionName);
        col.DeleteMany(p => p.ShokoSeriesId == shokoSeriesId && p.AnidbEpisodeId == anidbEpisodeId);
    }

    private class SettingsDocument
    {
        public int Id { get; set; }
        public SonarrSettings Settings { get; set; } = new();
    }

    private class ScanDocument
    {
        public int Id { get; set; }
        public ScanSnapshot Snapshot { get; set; } = new();
    }
}
