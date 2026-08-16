using System.Net;
using System.Reflection;
using Moq;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Abstractions.Metadata.Shoko;
using ShokoSonarr.Actions;
using ShokoSonarr.Config;
using ShokoSonarr.Models;
using ShokoSonarr.Services;
using Xunit;

namespace ShokoSonarr.Tests;

public class ActionsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "shoko-sonarr-tests-" + Guid.NewGuid());
    private readonly ScanCacheStore _cacheStore;

    public ActionsTests() => _cacheStore = new ScanCacheStore(_tempDir);

    public void Dispose()
    {
        _cacheStore.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private static SonarrClient MakeSonarrClient(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new FakeHandler(respond)));

    [Fact]
    public async Task TriggerScanAction_Execute_SavesScanToCacheStore()
    {
        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([]);
        var scanner = new MissingEpisodeScanner(metadataService.Object, _cacheStore, MakeSonarrClient(_ => new HttpResponseMessage(HttpStatusCode.OK)), new NotificationService(new HttpClient()));
        var action = new TriggerScanAction(scanner, _cacheStore);

        Assert.Null(_cacheStore.GetLastScan());
        await action.Execute();

        Assert.NotNull(_cacheStore.GetLastScan());
    }

    /// <summary>Sets a SeriesAction's protected Series context via reflection — the real setter is internal to Shoko.Abstractions (see IScopedAction), only settable by the framework at runtime.</summary>
    private static void SetSeriesContext(SearchMissingEpisodesAction action, IShokoSeries series) =>
        typeof(SearchMissingEpisodesAction).BaseType!
            .GetProperty("Series", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(action, series);

    private static Mock<IShokoSeries> MakeSeries(int id)
    {
        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(id);
        return series;
    }

    [Fact]
    public async Task SearchMissingEpisodesAction_Validate_NoScanDataForSeries_ReturnsRejection()
    {
        var matcher = new SeriesMatcher(MakeSonarrClient(_ => new HttpResponseMessage(HttpStatusCode.OK)), _cacheStore, new NotificationService(new HttpClient()));
        var action = new SearchMissingEpisodesAction(matcher, MakeSonarrClient(_ => new HttpResponseMessage(HttpStatusCode.OK)), _cacheStore);
        SetSeriesContext(action, MakeSeries(1).Object);

        var result = await action.Validate();

        Assert.NotNull(result);
        Assert.Contains("run a scan first", result!.Reason);
    }

    [Fact]
    public async Task SearchMissingEpisodesAction_Validate_SeriesWithConfirmedTvdbMatch_ReturnsNull()
    {
        _cacheStore.SaveSettings(new SonarrSettings { BaseUrl = "http://sonarr.local:8989", ApiKey = "testkey" });
        _cacheStore.SaveScan(new ScanSnapshot
        {
            Series = [new SeriesMissingResult
            {
                ShokoSeriesId = 1,
                Title = "One Piece",
                TvdbId = 81797,
                MissingEpisodes = [new MissingEpisodeInfo { AnidbEpisodeId = 10, EpisodeNumber = 1, IsSpecial = false, Title = "Ep 1" }],
            }],
        });
        var sonarrClient = MakeSonarrClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"tvdbId":81797,"title":"One Piece","year":1999}]"""),
        });
        var matcher = new SeriesMatcher(sonarrClient, _cacheStore, new NotificationService(new HttpClient()));
        var action = new SearchMissingEpisodesAction(matcher, sonarrClient, _cacheStore);
        SetSeriesContext(action, MakeSeries(1).Object);

        var result = await action.Validate();

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchMissingEpisodesAction_Execute_NoConfirmedMatch_Throws()
    {
        _cacheStore.SaveScan(new ScanSnapshot
        {
            Series = [new SeriesMissingResult { ShokoSeriesId = 1, Title = "Some Obscure Anime", TvdbId = null }],
        });
        var sonarrClient = MakeSonarrClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        var matcher = new SeriesMatcher(sonarrClient, _cacheStore, new NotificationService(new HttpClient()));
        var action = new SearchMissingEpisodesAction(matcher, sonarrClient, _cacheStore);
        SetSeriesContext(action, MakeSeries(1).Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => action.Execute());
    }
}
