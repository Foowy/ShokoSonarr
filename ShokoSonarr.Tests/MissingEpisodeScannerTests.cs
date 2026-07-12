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

    private class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

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

    private static Mock<IShokoEpisode> MakeEpisode(int anidbId, int number, EpisodeType type, bool hidden, int videoCount, DateOnly? airDate = null)
    {
        var ep = new Mock<IShokoEpisode>();
        ep.Setup(e => e.AnidbEpisodeID).Returns(anidbId);
        ep.Setup(e => e.EpisodeNumber).Returns(number);
        ep.Setup(e => e.Type).Returns(type);
        ep.Setup(e => e.IsHidden).Returns(hidden);
        ep.Setup(e => e.VideoList).Returns(videoCount == 0 ? [] : [Mock.Of<IVideo>()]);
        ep.Setup(e => e.AirDate).Returns(airDate);
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

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, new SonarrClient(new HttpClient()), new NotificationService(new HttpClient()));
        var snapshot = await scanner.ScanAsync();

        Assert.Single(snapshot.Series);
        Assert.Single(snapshot.Series[0].MissingEpisodes);
        Assert.Equal(5, snapshot.Series[0].MissingEpisodes[0].EpisodeNumber);
        Assert.Equal(1001, snapshot.Series[0].MissingEpisodes[0].AnidbEpisodeId);
    }

    [Fact]
    public async Task Scan_SeriesWithGroup_PopulatesGroupTitle()
    {
        var missingEp = MakeEpisode(anidbId: 5001, number: 1, type: EpisodeType.Episode, hidden: false, videoCount: 0);

        var group = new Mock<IShokoGroup>();
        group.Setup(g => g.Title).Returns("One Piece Franchise");

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(50);
        series.Setup(s => s.Episodes).Returns([missingEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });
        series.Setup(s => s.ParentGroup).Returns(group.Object);

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, new SonarrClient(new HttpClient()), new NotificationService(new HttpClient()));
        var snapshot = await scanner.ScanAsync();

        Assert.Single(snapshot.Series);
        Assert.Equal("One Piece Franchise", snapshot.Series[0].GroupTitle);
    }

    [Fact]
    public async Task Scan_SeriesWithNoParentGroup_LeavesGroupTitleNull()
    {
        var missingEp = MakeEpisode(anidbId: 5002, number: 1, type: EpisodeType.Episode, hidden: false, videoCount: 0);

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(51);
        series.Setup(s => s.Episodes).Returns([missingEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, new SonarrClient(new HttpClient()), new NotificationService(new HttpClient()));
        var snapshot = await scanner.ScanAsync();

        Assert.Single(snapshot.Series);
        Assert.Null(snapshot.Series[0].GroupTitle);
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

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, new SonarrClient(new HttpClient()), new NotificationService(new HttpClient()));
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

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, new SonarrClient(new HttpClient()), new NotificationService(new HttpClient()));
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

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, new SonarrClient(new HttpClient()), new NotificationService(new HttpClient()));
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

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, new SonarrClient(new HttpClient()), new NotificationService(new HttpClient()));
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

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, new SonarrClient(new HttpClient()), new NotificationService(new HttpClient()));
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

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, new SonarrClient(new HttpClient()), new NotificationService(new HttpClient()));
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

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, new SonarrClient(new HttpClient()), new NotificationService(new HttpClient()));
        var snapshot = await scanner.ScanAsync();

        Assert.Empty(snapshot.Series);
    }

    [Fact]
    public async Task Scan_PendingSearchNoLongerMissing_UnmonitorsAndClearsPending()
    {
        var ownedEp = MakeEpisode(anidbId: 9000, number: 1, type: EpisodeType.Episode, hidden: false, videoCount: 1); // now has a file — no longer missing

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(20);
        series.Setup(s => s.Episodes).Returns([ownedEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);
        _cacheStore.SaveSettings(new Config.SonarrSettings { BaseUrl = "http://sonarr.local:8989", ApiKey = "key" });
        _cacheStore.AddPendingSearch(new PendingSearch { ShokoSeriesId = 20, SeriesTitle = "Some Series", AnidbEpisodeId = 9000, EpisodeTitle = "Episode 1", SonarrSeriesId = 55, SonarrEpisodeId = 777, TriggeredAtUtc = DateTime.UtcNow });

        var handler = new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Accepted) { Content = new StringContent("{}") });
        var sonarrClient = new SonarrClient(new HttpClient(handler));

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, sonarrClient, new NotificationService(new HttpClient()));
        await scanner.ScanAsync();

        Assert.Empty(_cacheStore.GetPendingSearches());
        Assert.Single(handler.Requests);
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        Assert.Contains("\"monitored\":false", body);
        Assert.Contains("777", body);

        var history = _cacheStore.GetHistory();
        Assert.Single(history);
        Assert.Equal(SearchHistoryOutcome.Imported, history[0].Outcome);
        Assert.Equal("Some Series", history[0].SeriesTitle);
        Assert.Equal("Episode 1", history[0].EpisodeTitle);
    }

    [Fact]
    public async Task Scan_PendingSearchStillMissing_LeavesPendingAndDoesNotCallSonarr()
    {
        var stillMissingEp = MakeEpisode(anidbId: 9001, number: 1, type: EpisodeType.Episode, hidden: false, videoCount: 0);
        var ownedEp = MakeEpisode(anidbId: 9002, number: 2, type: EpisodeType.Episode, hidden: false, videoCount: 1);

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(21);
        series.Setup(s => s.Episodes).Returns([stillMissingEp.Object, ownedEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);
        _cacheStore.SaveSettings(new Config.SonarrSettings { BaseUrl = "http://sonarr.local:8989", ApiKey = "key" });
        _cacheStore.AddPendingSearch(new PendingSearch { ShokoSeriesId = 21, AnidbEpisodeId = 9001, SonarrSeriesId = 55, SonarrEpisodeId = 778, TriggeredAtUtc = DateTime.UtcNow });

        var handler = new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Accepted) { Content = new StringContent("{}") });
        var sonarrClient = new SonarrClient(new HttpClient(handler));

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, sonarrClient, new NotificationService(new HttpClient()));
        var snapshot = await scanner.ScanAsync();

        Assert.Single(_cacheStore.GetPendingSearches());
        Assert.Empty(handler.Requests);
        Assert.Equal("search-triggered", snapshot.Series[0].MissingEpisodes[0].ActionStatus);
    }

    [Fact]
    public async Task Scan_HideUnaired_FutureAirDate_IsExcludedFromResults()
    {
        var futureEp = MakeEpisode(anidbId: 9100, number: 1, type: EpisodeType.Episode, hidden: false, videoCount: 0, airDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)));
        var ownedEp = MakeEpisode(anidbId: 9101, number: 2, type: EpisodeType.Episode, hidden: false, videoCount: 1);

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(30);
        series.Setup(s => s.Episodes).Returns([futureEp.Object, ownedEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);
        _cacheStore.SaveSettings(new Config.SonarrSettings { HideUnaired = true });

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, new SonarrClient(new HttpClient()), new NotificationService(new HttpClient()));
        var snapshot = await scanner.ScanAsync();

        Assert.Empty(snapshot.Series);
    }

    [Fact]
    public async Task Scan_HideUnaired_NoAirDate_IsExcludedFromResults()
    {
        var unknownDateEp = MakeEpisode(anidbId: 9102, number: 1, type: EpisodeType.Episode, hidden: false, videoCount: 0, airDate: null);
        var ownedEp = MakeEpisode(anidbId: 9103, number: 2, type: EpisodeType.Episode, hidden: false, videoCount: 1);

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(31);
        series.Setup(s => s.Episodes).Returns([unknownDateEp.Object, ownedEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);
        _cacheStore.SaveSettings(new Config.SonarrSettings { HideUnaired = true });

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, new SonarrClient(new HttpClient()), new NotificationService(new HttpClient()));
        var snapshot = await scanner.ScanAsync();

        Assert.Empty(snapshot.Series);
    }

    [Fact]
    public async Task Scan_HideUnaired_PastAirDate_IsIncluded()
    {
        var pastEp = MakeEpisode(anidbId: 9104, number: 1, type: EpisodeType.Episode, hidden: false, videoCount: 0, airDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)));
        var ownedEp = MakeEpisode(anidbId: 9105, number: 2, type: EpisodeType.Episode, hidden: false, videoCount: 1);

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(32);
        series.Setup(s => s.Episodes).Returns([pastEp.Object, ownedEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);
        _cacheStore.SaveSettings(new Config.SonarrSettings { HideUnaired = true });

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, new SonarrClient(new HttpClient()), new NotificationService(new HttpClient()));
        var snapshot = await scanner.ScanAsync();

        Assert.Single(snapshot.Series);
        Assert.Single(snapshot.Series[0].MissingEpisodes);
    }

    [Fact]
    public async Task Scan_HideUnaired_DoesNotFalselyReconcilePendingSearch()
    {
        // Episode still hasn't aired, so it's hidden from the dashboard, but a pending search from before
        // HideUnaired was enabled must not be treated as "no longer missing" and unmonitored.
        var futureEp = MakeEpisode(anidbId: 9106, number: 1, type: EpisodeType.Episode, hidden: false, videoCount: 0, airDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)));

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(33);
        series.Setup(s => s.Episodes).Returns([futureEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);
        _cacheStore.SaveSettings(new Config.SonarrSettings { HideUnaired = true, BaseUrl = "http://sonarr.local:8989", ApiKey = "key" });
        _cacheStore.AddPendingSearch(new PendingSearch { ShokoSeriesId = 33, AnidbEpisodeId = 9106, SonarrSeriesId = 55, SonarrEpisodeId = 999, TriggeredAtUtc = DateTime.UtcNow });

        var handler = new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Accepted) { Content = new StringContent("{}") });
        var sonarrClient = new SonarrClient(new HttpClient(handler));

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, sonarrClient, new NotificationService(new HttpClient()));
        await scanner.ScanAsync();

        Assert.Empty(handler.Requests);
        Assert.Single(_cacheStore.GetPendingSearches());
    }

    [Fact]
    public async Task Scan_PendingSearchExcludedBySpecialsOverride_UnmonitorsAndClearsPending()
    {
        // Still missing from Shoko's perspective (no file), but a per-series override now excludes specials from scan scope.
        var specialEp = MakeEpisode(anidbId: 9500, number: 1, type: EpisodeType.Special, hidden: false, videoCount: 0);

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(23);
        series.Setup(s => s.Episodes).Returns([specialEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);
        _cacheStore.SaveSettings(new Config.SonarrSettings { BaseUrl = "http://sonarr.local:8989", ApiKey = "key" });
        _cacheStore.SetSeriesOverride(23, includeSpecials: false);
        _cacheStore.AddPendingSearch(new PendingSearch { ShokoSeriesId = 23, AnidbEpisodeId = 9500, SonarrSeriesId = 55, SonarrEpisodeId = 780, TriggeredAtUtc = DateTime.UtcNow });

        var handler = new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Accepted) { Content = new StringContent("{}") });
        var sonarrClient = new SonarrClient(new HttpClient(handler));

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, sonarrClient, new NotificationService(new HttpClient()));
        await scanner.ScanAsync();

        Assert.Empty(_cacheStore.GetPendingSearches());
        Assert.Single(handler.Requests);
    }

    private class ThrowingSonarrClient : SonarrClient
    {
        public ThrowingSonarrClient() : base(new HttpClient()) { }

        public override Task<Models.SonarrActionResult<bool>> UnmonitorEpisodesAsync(Config.SonarrSettings settings, List<int> sonarrEpisodeIds, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task Scan_UnmonitorThrows_LeavesPendingAndDoesNotThrow()
    {
        var ownedEp = MakeEpisode(anidbId: 9004, number: 1, type: EpisodeType.Episode, hidden: false, videoCount: 1);

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(24);
        series.Setup(s => s.Episodes).Returns([ownedEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);
        _cacheStore.SaveSettings(new Config.SonarrSettings { BaseUrl = "http://sonarr.local:8989", ApiKey = "key" });
        _cacheStore.AddPendingSearch(new PendingSearch { ShokoSeriesId = 24, AnidbEpisodeId = 9004, SonarrSeriesId = 55, SonarrEpisodeId = 781, TriggeredAtUtc = DateTime.UtcNow });

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, new ThrowingSonarrClient(), new NotificationService(new HttpClient()));
        var exception = await Record.ExceptionAsync(() => scanner.ScanAsync());

        Assert.Null(exception);
        Assert.Single(_cacheStore.GetPendingSearches());
    }

    [Fact]
    public async Task Scan_UnmonitorFailsPastMaxAge_DropsPendingEntry()
    {
        var ownedEp = MakeEpisode(anidbId: 9005, number: 1, type: EpisodeType.Episode, hidden: false, videoCount: 1);

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(25);
        series.Setup(s => s.Episodes).Returns([ownedEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);
        _cacheStore.SaveSettings(new Config.SonarrSettings { BaseUrl = "http://sonarr.local:8989", ApiKey = "key" });
        _cacheStore.AddPendingSearch(new PendingSearch { ShokoSeriesId = 25, SeriesTitle = "Expiring Series", AnidbEpisodeId = 9005, EpisodeTitle = "Episode 1", SonarrSeriesId = 55, SonarrEpisodeId = 782, TriggeredAtUtc = DateTime.UtcNow.AddDays(-15) });

        var handler = new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError) { Content = new StringContent("boom") });
        var sonarrClient = new SonarrClient(new HttpClient(handler));

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, sonarrClient, new NotificationService(new HttpClient()));
        var exception = await Record.ExceptionAsync(() => scanner.ScanAsync());

        Assert.Null(exception);
        Assert.Empty(_cacheStore.GetPendingSearches());

        var history = _cacheStore.GetHistory();
        Assert.Single(history);
        Assert.Equal(SearchHistoryOutcome.Expired, history[0].Outcome);
        Assert.Equal("Expiring Series", history[0].SeriesTitle);
    }

    [Fact]
    public async Task Scan_UnmonitorFails_LeavesPendingAndDoesNotThrow()
    {
        var ownedEp = MakeEpisode(anidbId: 9003, number: 1, type: EpisodeType.Episode, hidden: false, videoCount: 1);

        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(22);
        series.Setup(s => s.Episodes).Returns([ownedEp.Object]);
        series.Setup(s => s.LocalEpisodeCounts).Returns(new EpisodeCounts { Episodes = 1 });

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([series.Object]);
        _cacheStore.SaveSettings(new Config.SonarrSettings { BaseUrl = "http://sonarr.local:8989", ApiKey = "key" });
        _cacheStore.AddPendingSearch(new PendingSearch { ShokoSeriesId = 22, AnidbEpisodeId = 9003, SonarrSeriesId = 55, SonarrEpisodeId = 779, TriggeredAtUtc = DateTime.UtcNow });

        var handler = new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError) { Content = new StringContent("boom") });
        var sonarrClient = new SonarrClient(new HttpClient(handler));

        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, sonarrClient, new NotificationService(new HttpClient()));
        var exception = await Record.ExceptionAsync(() => scanner.ScanAsync());

        Assert.Null(exception);
        Assert.Single(_cacheStore.GetPendingSearches());
    }
}
