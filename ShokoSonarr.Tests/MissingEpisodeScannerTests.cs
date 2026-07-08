using Moq;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Enums;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Abstractions.Metadata.Shoko;
using Shoko.Abstractions.Video;
using ShokoSonarr.Models;
using ShokoSonarr.Services;
using Xunit;

namespace ShokoSonarr.Tests;

public class MissingEpisodeScannerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScanCacheStore _cacheStore;

    public MissingEpisodeScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "shoko-sonarr-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _cacheStore = new ScanCacheStore(_tempDir);
    }

    public void Dispose()
    {
        _cacheStore.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }

    private static Mock<IShokoEpisode> MakeEpisode(int anidbId, int number, EpisodeType type, bool hidden, int videoCount)
    {
        var ep = new Mock<IShokoEpisode>();
        ep.Setup(e => e.AnidbEpisodeID).Returns(anidbId);
        ep.Setup(e => e.EpisodeNumber).Returns(number);
        ep.Setup(e => e.Type).Returns(type);
        ep.Setup(e => e.IsHidden).Returns(hidden);
        ep.Setup(e => e.VideoList).Returns(videoCount == 0 ? [] : [Mock.Of<IVideo>()]);
        ep.Setup(e => e.AirDate).Returns((DateOnly?)null);
        return ep;
    }

    [Fact]
    public async Task Scan_SeriesWithOneMissingNormalEpisode_ReturnsThatEpisode()
    {
        var missingEp = MakeEpisode(anidbId: 1001, number: 5, type: EpisodeType.Episode, hidden: false, videoCount: 0);
        var ownedEp = MakeEpisode(anidbId: 1000, number: 4, type: EpisodeType.Episode, hidden: false, videoCount: 1);

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(42);
        series.Setup(s => s.Episodes).Returns([ownedEp.Object, missingEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore);
        var snapshot = await scanner.ScanAsync();

        Assert.Single(snapshot.Series);
        Assert.Single(snapshot.Series[0].MissingEpisodes);
        Assert.Equal(5, snapshot.Series[0].MissingEpisodes[0].EpisodeNumber);
        Assert.Equal(1001, snapshot.Series[0].MissingEpisodes[0].AnidbEpisodeId);
    }

    [Fact]
    public async Task Scan_SeriesWithNoFilesAtAll_IsExcluded()
    {
        var unownedEp = MakeEpisode(anidbId: 2001, number: 1, type: EpisodeType.Episode, hidden: false, videoCount: 0);

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(99);
        series.Setup(s => s.Episodes).Returns([unownedEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 0 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore);
        var snapshot = await scanner.ScanAsync();

        Assert.Empty(snapshot.Series);
    }

    [Fact]
    public async Task Scan_HiddenMissingEpisode_IsExcluded()
    {
        var hiddenEp = MakeEpisode(anidbId: 3001, number: 6, type: EpisodeType.Episode, hidden: true, videoCount: 0);
        var ownedEp = MakeEpisode(anidbId: 3000, number: 5, type: EpisodeType.Episode, hidden: false, videoCount: 1);

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(7);
        series.Setup(s => s.Episodes).Returns([ownedEp.Object, hiddenEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore);
        var snapshot = await scanner.ScanAsync();

        Assert.Empty(snapshot.Series);
    }

    [Fact]
    public async Task Scan_CreditsTypeEpisodeMissing_IsExcluded()
    {
        var creditsEp = MakeEpisode(anidbId: 4001, number: 1, type: EpisodeType.Credits, hidden: false, videoCount: 0);
        var ownedEp = MakeEpisode(anidbId: 4000, number: 1, type: EpisodeType.Episode, hidden: false, videoCount: 1);

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(8);
        series.Setup(s => s.Episodes).Returns([ownedEp.Object, creditsEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore);
        var snapshot = await scanner.ScanAsync();

        Assert.Empty(snapshot.Series);
    }

    [Fact]
    public async Task Scan_SpecialMissing_GlobalIncludeSpecialsTrue_IsIncluded()
    {
        var specialEp = MakeEpisode(anidbId: 5001, number: 1, type: EpisodeType.Special, hidden: false, videoCount: 0);
        var ownedEp = MakeEpisode(anidbId: 5000, number: 1, type: EpisodeType.Episode, hidden: false, videoCount: 1);

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(10);
        series.Setup(s => s.Episodes).Returns([ownedEp.Object, specialEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);
        _cacheStore.SaveSettings(new Config.SonarrSettings { IncludeSpecials = true });

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore);
        var snapshot = await scanner.ScanAsync();

        Assert.Single(snapshot.Series);
        Assert.Single(snapshot.Series[0].MissingEpisodes);
        Assert.True(snapshot.Series[0].MissingEpisodes[0].IsSpecial);
    }

    [Fact]
    public async Task Scan_SpecialMissing_GlobalIncludeSpecialsFalse_IsExcluded()
    {
        var specialEp = MakeEpisode(anidbId: 6001, number: 1, type: EpisodeType.Special, hidden: false, videoCount: 0);
        var ownedEp = MakeEpisode(anidbId: 6000, number: 1, type: EpisodeType.Episode, hidden: false, videoCount: 1);

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(11);
        series.Setup(s => s.Episodes).Returns([ownedEp.Object, specialEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);
        _cacheStore.SaveSettings(new Config.SonarrSettings { IncludeSpecials = false });

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore);
        var snapshot = await scanner.ScanAsync();

        Assert.Empty(snapshot.Series);
    }

    [Fact]
    public async Task Scan_SpecialMissing_PerSeriesOverrideTrue_OverridesGlobalFalse()
    {
        var specialEp = MakeEpisode(anidbId: 7001, number: 1, type: EpisodeType.Special, hidden: false, videoCount: 0);
        var ownedEp = MakeEpisode(anidbId: 7000, number: 1, type: EpisodeType.Episode, hidden: false, videoCount: 1);

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(12);
        series.Setup(s => s.Episodes).Returns([ownedEp.Object, specialEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);
        _cacheStore.SaveSettings(new Config.SonarrSettings { IncludeSpecials = false });
        _cacheStore.SetSeriesOverride(12, includeSpecials: true);

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore);
        var snapshot = await scanner.ScanAsync();

        Assert.Single(snapshot.Series);
        Assert.Single(snapshot.Series[0].MissingEpisodes);
        Assert.True(snapshot.Series[0].IncludeSpecialsOverride);
    }

    [Fact]
    public async Task Scan_SpecialMissing_PerSeriesOverrideFalse_OverridesGlobalTrue()
    {
        var specialEp = MakeEpisode(anidbId: 8001, number: 1, type: EpisodeType.Special, hidden: false, videoCount: 0);
        var ownedEp = MakeEpisode(anidbId: 8000, number: 1, type: EpisodeType.Episode, hidden: false, videoCount: 1);

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(13);
        series.Setup(s => s.Episodes).Returns([ownedEp.Object, specialEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);
        _cacheStore.SaveSettings(new Config.SonarrSettings { IncludeSpecials = true });
        _cacheStore.SetSeriesOverride(13, includeSpecials: false);

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore);
        var snapshot = await scanner.ScanAsync();

        Assert.Empty(snapshot.Series);
    }
}
