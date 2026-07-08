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
    public void MarkEpisodeActionStatus_UpdatesStatusInStoredSnapshot()
    {
        var snapshot = new ScanSnapshot
        {
            ScannedAtUtc = DateTime.UtcNow,
            Series =
            [
                new SeriesMissingResult
                {
                    ShokoSeriesId = 42,
                    Title = "One Piece",
                    MissingEpisodes = [new MissingEpisodeInfo { AnidbEpisodeId = 1001, EpisodeNumber = 1085 }],
                },
            ],
        };
        _store.SaveScan(snapshot);

        _store.MarkEpisodeActionStatus(shokoSeriesId: 42, anidbEpisodeId: 1001, status: "search-triggered");

        var loaded = _store.GetLastScan();
        Assert.Equal("search-triggered", loaded!.Series[0].MissingEpisodes[0].ActionStatus);
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
}
