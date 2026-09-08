using System.Net;
using System.Text.Json;
using ShokoSonarr.Config;
using ShokoSonarr.Models;
using ShokoSonarr.Services;
using Xunit;

namespace ShokoSonarr.Tests;

public class SonarrSearchServiceTests : IDisposable
{
    private readonly List<ScanCacheStore> _stores = [];

    public void Dispose()
    {
        foreach (var store in _stores)
            store.Dispose();
    }

    private static SonarrSettings TestSettings => new() { BaseUrl = "http://sonarr.local:8989", ApiKey = "testkey" };

    private class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private static SonarrClient MakeClient(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new FakeHandler(respond)));

    private SonarrSearchService MakeService(SonarrClient client)
    {
        var cacheStore = new ScanCacheStore(Path.Combine(Path.GetTempPath(), "shoko-sonarr-tests-" + Guid.NewGuid()));
        _stores.Add(cacheStore);
        var notificationService = new NotificationService(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));
        return new SonarrSearchService(client, cacheStore, notificationService);
    }

    private static SeriesMissingResult SeriesWith(params (int anidbId, int epNum, bool special, string title)[] eps) =>
        new()
        {
            ShokoSeriesId = 1,
            Title = "Test Anime",
            TvdbId = 100,
            MissingEpisodes = [.. eps.Select(e => new MissingEpisodeInfo
            {
                AnidbEpisodeId = e.anidbId, EpisodeNumber = e.epNum, IsSpecial = e.special, Title = e.title,
            })],
        };

    [Fact]
    public async Task MonitorAndSearch_MultiSeasonSonarrSeries_MapsNormalEpisodeByAbsoluteNumber()
    {
        var episodes = new List<object>();
        for (int i = 1; i <= 12; i++) episodes.Add(new { id = 100 + i, seasonNumber = 1, episodeNumber = i, absoluteEpisodeNumber = i });
        for (int i = 1; i <= 12; i++) episodes.Add(new { id = 200 + i, seasonNumber = 2, episodeNumber = i, absoluteEpisodeNumber = 12 + i });

        HttpRequestMessage? searchReq = null;
        var client = MakeClient(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/episode") && req.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(episodes)) };
            if (req.RequestUri.AbsolutePath.EndsWith("/command")) { searchReq = req; }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });
        var service = MakeService(client);
        var series = SeriesWith((anidbId: 5014, epNum: 14, special: false, title: "Episode 14"));

        var result = await service.MonitorAndSearchAsync(TestSettings, shokoSeriesId: 1, sonarrSeriesId: 9, anidbEpisodeIds: [5014], series);

        Assert.True(result.Success);
        var body = await searchReq!.Content!.ReadAsStringAsync();
        Assert.Contains("202", body);            // mapped to the season-2 episode (abs 14)
        Assert.DoesNotContain("102", body);       // not the season-1 episode
    }

    [Fact]
    public async Task MonitorAndSearch_SpecialStillMapsViaSeasonZero()
    {
        var episodes = new List<object>
        {
            new { id = 300, seasonNumber = 0, episodeNumber = 1, absoluteEpisodeNumber = (int?)null },
            new { id = 301, seasonNumber = 1, episodeNumber = 1, absoluteEpisodeNumber = (int?)1 },
        };
        HttpRequestMessage? searchReq = null;
        var client = MakeClient(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/episode") && req.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(episodes)) };
            if (req.RequestUri.AbsolutePath.EndsWith("/command")) searchReq = req;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });
        var service = MakeService(client);
        var series = SeriesWith((anidbId: 5900, epNum: 1, special: true, title: "OVA 1"));

        var result = await service.MonitorAndSearchAsync(TestSettings, 1, 9, [5900], series);

        Assert.True(result.Success);
        Assert.Contains("300", await searchReq!.Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task MonitorAndSearch_LegacyStandardSeries_FallsBackToSeasonOneMatch()
    {
        var episodes = new List<object>
        {
            new { id = 401, seasonNumber = 1, episodeNumber = 1, absoluteEpisodeNumber = (int?)null },
            new { id = 402, seasonNumber = 1, episodeNumber = 2, absoluteEpisodeNumber = (int?)null },
        };
        HttpRequestMessage? searchReq = null;
        var client = MakeClient(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/episode") && req.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(episodes)) };
            if (req.RequestUri.AbsolutePath.EndsWith("/command")) searchReq = req;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });
        var service = MakeService(client);
        var series = SeriesWith((anidbId: 5002, epNum: 2, special: false, title: "Episode 2"));

        var result = await service.MonitorAndSearchAsync(TestSettings, 1, 9, [5002], series);

        Assert.True(result.Success);
        Assert.Contains("402", await searchReq!.Content!.ReadAsStringAsync());
    }
}
