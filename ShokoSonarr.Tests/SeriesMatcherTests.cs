using System.Net;
using ShokoSonarr.Config;
using ShokoSonarr.Models;
using ShokoSonarr.Services;
using Xunit;

namespace ShokoSonarr.Tests;

public class SeriesMatcherTests
{
    private static SonarrSettings TestSettings => new() { BaseUrl = "http://sonarr.local:8989", ApiKey = "testkey" };

    private class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private static SonarrClient MakeClient(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new FakeHandler(respond)));

    private static SeriesMatcher MakeMatcher(SonarrClient client)
    {
        var cacheStore = new ScanCacheStore(Path.Combine(Path.GetTempPath(), "shoko-sonarr-tests-" + Guid.NewGuid()));
        var notificationService = new NotificationService(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));
        return new SeriesMatcher(client, cacheStore, notificationService);
    }

    [Fact]
    public async Task ResolveAsync_SeriesWithKnownTvdbId_AutoResolvesWhenSonarrConfirms()
    {
        var client = MakeClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"tvdbId":81797,"title":"One Piece","year":1999}]"""),
        });
        var matcher = MakeMatcher(client);
        var series = new SeriesMissingResult { ShokoSeriesId = 1, Title = "One Piece", TvdbId = 81797 };

        var resolution = await matcher.ResolveAsync(TestSettings, series);

        Assert.True(resolution.AutoResolved);
        Assert.Equal(81797, resolution.TvdbId);
    }

    [Fact]
    public async Task ResolveAsync_SeriesWithNoTvdbId_FallsBackToTitleSearchCandidates()
    {
        var client = MakeClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"tvdbId":12345,"title":"Frieren","year":2023},{"tvdbId":67890,"title":"Frieren: Beyond Journey's End","year":2023}]"""),
        });
        var matcher = MakeMatcher(client);
        var series = new SeriesMissingResult { ShokoSeriesId = 2, Title = "Frieren", TvdbId = null };

        var resolution = await matcher.ResolveAsync(TestSettings, series);

        Assert.False(resolution.AutoResolved);
        Assert.Equal(2, resolution.Candidates.Count);
        Assert.Null(resolution.ErrorMessage);
    }

    [Fact]
    public async Task ResolveAsync_NoTvdbIdAndNoTitleMatches_ReturnsNoMatchError()
    {
        var client = MakeClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        var matcher = MakeMatcher(client);
        var series = new SeriesMissingResult { ShokoSeriesId = 3, Title = "Some Obscure Anime", TvdbId = null };

        var resolution = await matcher.ResolveAsync(TestSettings, series);

        Assert.False(resolution.AutoResolved);
        Assert.Empty(resolution.Candidates);
        Assert.Equal("no Sonarr match available", resolution.ErrorMessage);
    }
}
