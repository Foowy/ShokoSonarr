using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Enums;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Abstractions.Metadata.Shoko;
using Shoko.Abstractions.Video;
using ShokoSonarr.Controllers;
using ShokoSonarr.Controllers.Api;
using ShokoSonarr.Models;
using ShokoSonarr.Services;
using Xunit;

namespace ShokoSonarr.Tests;

public class ScanControllerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScanCacheStore _cacheStore;

    public ScanControllerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "shoko-sonarr-scan-controller-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _cacheStore = new ScanCacheStore(_tempDir);
    }

    public void Dispose()
    {
        _cacheStore.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }

    private static Mock<IShokoEpisode> MakeEpisode(int anidbId, int number, EpisodeType type, bool hidden, int videoCount, DateOnly? airDate = null)
    {
        var ep = new Mock<IShokoEpisode>();
        ep.Setup(e => e.AnidbEpisodeID).Returns(anidbId);
        ep.Setup(e => e.EpisodeNumber).Returns(number);
        ep.Setup(e => e.Type).Returns(type);
        ep.Setup(e => e.IsHidden).Returns(hidden);
        ep.Setup(e => e.Videos).Returns(videoCount == 0 ? [] : [Mock.Of<IVideo>()]);
        ep.Setup(e => e.AirDate).Returns(airDate);
        return ep;
    }

    private static ScanController MakeController(Mock<IMetadataService> metadataService, ScanCacheStore cacheStore)
    {
        var httpClient = new HttpClient();
        var sonarrClient = new SonarrClient(httpClient);
        var scanner = new MissingEpisodeScanner(metadataService.Object, cacheStore, sonarrClient, new NotificationService(httpClient));
        return new ScanController(scanner, cacheStore, metadataService.Object, sonarrClient, new RelatedSeriesFinder(metadataService.Object))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private static ScanSnapshot SnapshotOf(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ShokoSonarrBaseController.ApiResponse<object>>(ok.Value);
        return Assert.IsType<ScanSnapshot>(response.Data);
    }

    private void SeedTwoSeriesSnapshot() =>
        _cacheStore.SaveScan(new ScanSnapshot
        {
            ScannedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Series =
            [
                new SeriesMissingResult { ShokoSeriesId = 1, Title = "Untouched", MissingEpisodes = [new MissingEpisodeInfo { AnidbEpisodeId = 11, EpisodeNumber = 1 }] },
                new SeriesMissingResult { ShokoSeriesId = 2, Title = "Target", MissingEpisodes = [new MissingEpisodeInfo { AnidbEpisodeId = 21, EpisodeNumber = 1 }, new MissingEpisodeInfo { AnidbEpisodeId = 22, EpisodeNumber = 2 }] },
            ],
        });

    private static Mock<IShokoSeries> MakeTargetSeries(params Mock<IShokoEpisode>[] episodes)
    {
        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(2);
        series.Setup(s => s.Title).Returns("Target");
        series.Setup(s => s.Episodes).Returns([.. episodes.Select(e => e.Object)]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });
        return series;
    }

    [Fact]
    public async Task SetSeriesSpecials_ExcludingSpecials_DropsSeriesFromSnapshotWithoutFullScan()
    {
        SeedTwoSeriesSnapshot();
        var special = MakeEpisode(anidbId: 200, number: 1, type: EpisodeType.Special, hidden: false, videoCount: 0);
        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetShokoSeriesByID(2)).Returns(MakeTargetSeries(special).Object);

        var controller = MakeController(metadataService, _cacheStore);
        var snapshot = SnapshotOf(await controller.SetSeriesSpecials(2, new SetSeriesSpecialsRequest(IncludeSpecials: false)));

        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), snapshot.ScannedAtUtc);
        Assert.Contains(snapshot.Series, s => s.ShokoSeriesId == 1 && s.Title == "Untouched");
        Assert.DoesNotContain(snapshot.Series, s => s.ShokoSeriesId == 2);
        metadataService.Verify(m => m.GetAllShokoSeries(), Times.Never);
    }

    [Fact]
    public async Task SetSeriesSpecials_IncludingSpecials_ReplacesOnlyTargetSeries()
    {
        SeedTwoSeriesSnapshot();
        var special = MakeEpisode(anidbId: 200, number: 1, type: EpisodeType.Special, hidden: false, videoCount: 0);
        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetShokoSeriesByID(2)).Returns(MakeTargetSeries(special).Object);

        var controller = MakeController(metadataService, _cacheStore);
        var snapshot = SnapshotOf(await controller.SetSeriesSpecials(2, new SetSeriesSpecialsRequest(IncludeSpecials: true)));

        var target = Assert.Single(snapshot.Series, s => s.ShokoSeriesId == 2);
        Assert.Equal(200, Assert.Single(target.MissingEpisodes).AnidbEpisodeId);
        Assert.True(target.IncludeSpecialsOverride);
        Assert.Contains(snapshot.Series, s => s.ShokoSeriesId == 1);
        Assert.Equal(_cacheStore.GetLastScan()!.Series.Count, snapshot.Series.Count);
        metadataService.Verify(m => m.GetAllShokoSeries(), Times.Never);
    }

    [Fact]
    public async Task SetSeriesSonarrOverride_PatchesOverridesOntoTargetSeriesOnly()
    {
        SeedTwoSeriesSnapshot();
        var missing = MakeEpisode(anidbId: 21, number: 1, type: EpisodeType.Episode, hidden: false, videoCount: 0);
        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetShokoSeriesByID(2)).Returns(MakeTargetSeries(missing).Object);

        var controller = MakeController(metadataService, _cacheStore);
        var snapshot = SnapshotOf(await controller.SetSeriesSonarrOverride(2, new SetSeriesSonarrOverrideRequest(QualityProfileId: 7, RootFolderPath: "/anime")));

        var target = Assert.Single(snapshot.Series, s => s.ShokoSeriesId == 2);
        Assert.Equal(7, target.QualityProfileIdOverride);
        Assert.Equal("/anime", target.RootFolderPathOverride);
        Assert.Contains(snapshot.Series, s => s.ShokoSeriesId == 1 && s.QualityProfileIdOverride is null);
        metadataService.Verify(m => m.GetAllShokoSeries(), Times.Never);
    }

    [Fact]
    public async Task SetSeriesSpecials_UnknownSeries_ReturnsNotFound()
    {
        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetShokoSeriesByID(999)).Returns((IShokoSeries?)null);

        var controller = MakeController(metadataService, _cacheStore);
        var result = await controller.SetSeriesSpecials(999, new SetSeriesSpecialsRequest(IncludeSpecials: true));

        Assert.IsType<NotFoundObjectResult>(result);
    }
}
