using LiteDB;
using ShokoSonarr.Config;
using ShokoSonarr.Models;
using ShokoSonarr.Services;
using Xunit;

namespace ShokoSonarr.Tests;

public class ScanCacheStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScanCacheStore _store;

    public ScanCacheStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "shoko-sonarr-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _store = new ScanCacheStore(_tempDir);
    }

    public void Dispose()
    {
        _store.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void GetSettings_WhenNoneSaved_ReturnsDefaults()
    {
        var settings = _store.GetSettings();

        Assert.Null(settings.BaseUrl);
        Assert.Equal(24, settings.ScanIntervalHours);
    }

    [Fact]
    public void SaveSettings_ThenGetSettings_RoundTrips()
    {
        _store.SaveSettings(new SonarrSettings { BaseUrl = "http://sonarr:8989", ApiKey = "abc123", ScanIntervalHours = 12 });

        var settings = _store.GetSettings();

        Assert.Equal("http://sonarr:8989", settings.BaseUrl);
        Assert.Equal("abc123", settings.ApiKey);
        Assert.Equal(12, settings.ScanIntervalHours);
    }

    [Fact]
    public void SaveSettings_ThenGetSettings_RoundTripsIncludeSpecials()
    {
        _store.SaveSettings(new SonarrSettings { IncludeSpecials = false });

        var settings = _store.GetSettings();

        Assert.False(settings.IncludeSpecials);
    }

    [Fact]
    public void SaveScan_ThenGetLastScan_RoundTrips()
    {
        var snapshot = new ScanSnapshot
        {
            ScannedAtUtc = new DateTime(2026, 7, 7, 12, 0, 0, DateTimeKind.Utc),
            Series =
            [
                new SeriesMissingResult
                {
                    ShokoSeriesId = 42,
                    Title = "One Piece",
                    MissingEpisodes = [new MissingEpisodeInfo { AnidbEpisodeId = 1001, EpisodeNumber = 1085, Title = "Ep 1085" }],
                },
            ],
        };

        _store.SaveScan(snapshot);
        var loaded = _store.GetLastScan();

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Series);
        Assert.Equal("One Piece", loaded.Series[0].Title);
        Assert.Equal(1085, loaded.Series[0].MissingEpisodes[0].EpisodeNumber);
    }

    [Fact]
    public void GetSeriesOverride_WhenNoneSet_ReturnsNull()
    {
        Assert.Null(_store.GetSeriesOverride(42));
    }

    [Fact]
    public void SetSeriesOverride_ThenGetSeriesOverride_RoundTrips()
    {
        _store.SetSeriesOverride(42, includeSpecials: false);

        var result = _store.GetSeriesOverride(42);

        Assert.NotNull(result);
        Assert.False(result!.IncludeSpecials);
    }

    [Fact]
    public void SetSeriesOverride_WithNull_ClearsAnyExistingOverride()
    {
        _store.SetSeriesOverride(42, includeSpecials: true);

        _store.SetSeriesOverride(42, includeSpecials: null);

        Assert.Null(_store.GetSeriesOverride(42));
    }

    [Fact]
    public void SetSeriesOverride_CalledTwice_ReplacesRatherThanDuplicates()
    {
        _store.SetSeriesOverride(42, includeSpecials: true);
        _store.SetSeriesOverride(42, includeSpecials: false);

        var result = _store.GetSeriesOverride(42);

        Assert.NotNull(result);
        Assert.False(result!.IncludeSpecials);
    }

    [Fact]
    public void GetSettings_PreUpgradeDocMissingIncludeSpecials_DefaultsToTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "shoko-sonarr-tests-" + Guid.NewGuid());
        var pluginDir = Path.Combine(tempDir, "plugins", ShokoSonarrConstants.PluginDataSubfolder);
        Directory.CreateDirectory(pluginDir);
        var dbPath = Path.Combine(pluginDir, ShokoSonarrConstants.LiteDbFileName);

        // Simulate a settings doc saved before IncludeSpecials existed — the field is simply absent, not null.
        using (var db = new LiteDatabase(dbPath))
        {
            var col = db.GetCollection("settings");
            col.Insert(new BsonDocument
            {
                ["_id"] = 1,
                ["Settings"] = new BsonDocument { ["BaseUrl"] = "http://sonarr:8989" },
            });
        }

        try
        {
            using var store = new ScanCacheStore(tempDir);
            Assert.True(store.GetSettings().IncludeSpecials);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void GetPendingSearches_WhenNoneAdded_ReturnsEmpty()
    {
        Assert.Empty(_store.GetPendingSearches());
    }

    [Fact]
    public void AddPendingSearch_ThenGetPendingSearches_RoundTrips()
    {
        _store.AddPendingSearch(new PendingSearch
        {
            ShokoSeriesId = 42,
            AnidbEpisodeId = 1001,
            SonarrSeriesId = 55,
            SonarrEpisodeId = 999,
            TriggeredAtUtc = new DateTime(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc),
        });

        var pending = _store.GetPendingSearches();

        Assert.Single(pending);
        Assert.Equal(42, pending[0].ShokoSeriesId);
        Assert.Equal(1001, pending[0].AnidbEpisodeId);
        Assert.Equal(55, pending[0].SonarrSeriesId);
        Assert.Equal(999, pending[0].SonarrEpisodeId);
    }

    [Fact]
    public void AddPendingSearch_CalledTwiceForSameKey_ReplacesRatherThanDuplicates()
    {
        _store.AddPendingSearch(new PendingSearch { ShokoSeriesId = 42, AnidbEpisodeId = 1001, SonarrSeriesId = 55, SonarrEpisodeId = 999, TriggeredAtUtc = DateTime.UtcNow });
        _store.AddPendingSearch(new PendingSearch { ShokoSeriesId = 42, AnidbEpisodeId = 1001, SonarrSeriesId = 55, SonarrEpisodeId = 1000, TriggeredAtUtc = DateTime.UtcNow });

        var pending = _store.GetPendingSearches();

        Assert.Single(pending);
        Assert.Equal(1000, pending[0].SonarrEpisodeId);
    }

    [Fact]
    public void RemovePendingSearch_RemovesMatchingEntry()
    {
        _store.AddPendingSearch(new PendingSearch { ShokoSeriesId = 42, AnidbEpisodeId = 1001, SonarrSeriesId = 55, SonarrEpisodeId = 999, TriggeredAtUtc = DateTime.UtcNow });

        _store.RemovePendingSearch(42, 1001);

        Assert.Empty(_store.GetPendingSearches());
    }

    [Fact]
    public void GetHistory_WhenNoneAdded_ReturnsEmpty()
    {
        Assert.Empty(_store.GetHistory());
    }

    [Fact]
    public void AddHistoryEntry_ThenGetHistory_RoundTrips()
    {
        _store.AddHistoryEntry(new SearchHistoryEntry
        {
            ShokoSeriesId = 42,
            SeriesTitle = "Some Series",
            AnidbEpisodeId = 1001,
            EpisodeTitle = "Episode 1",
            Outcome = SearchHistoryOutcome.Triggered,
            TimestampUtc = DateTime.UtcNow,
        });

        var history = _store.GetHistory();

        Assert.Single(history);
        Assert.Equal("Some Series", history[0].SeriesTitle);
        Assert.Equal(SearchHistoryOutcome.Triggered, history[0].Outcome);
    }

    [Fact]
    public void GetHistory_ReturnsNewestFirst()
    {
        var older = DateTime.UtcNow.AddMinutes(-10);
        var newer = DateTime.UtcNow;
        _store.AddHistoryEntry(new SearchHistoryEntry { ShokoSeriesId = 1, AnidbEpisodeId = 1, Outcome = SearchHistoryOutcome.Triggered, TimestampUtc = older });
        _store.AddHistoryEntry(new SearchHistoryEntry { ShokoSeriesId = 2, AnidbEpisodeId = 2, Outcome = SearchHistoryOutcome.Imported, TimestampUtc = newer });

        var history = _store.GetHistory();

        Assert.Equal(2, history[0].ShokoSeriesId);
        Assert.Equal(1, history[1].ShokoSeriesId);
    }

    [Fact]
    public void AddHistoryEntry_BeyondCap_TrimsOldestEntries()
    {
        var baseTime = DateTime.UtcNow.AddDays(-1);
        for (var i = 0; i < 505; i++)
        {
            _store.AddHistoryEntry(new SearchHistoryEntry
            {
                ShokoSeriesId = i,
                AnidbEpisodeId = i,
                Outcome = SearchHistoryOutcome.Triggered,
                TimestampUtc = baseTime.AddSeconds(i),
            });
        }

        var history = _store.GetHistory(limit: 1000);

        Assert.True(history.Count <= 500);
        // The oldest entries (lowest ShokoSeriesId, since they share the same insertion order as timestamp) should be gone.
        Assert.DoesNotContain(history, h => h.ShokoSeriesId == 0);
        Assert.Contains(history, h => h.ShokoSeriesId == 504);
    }
}
