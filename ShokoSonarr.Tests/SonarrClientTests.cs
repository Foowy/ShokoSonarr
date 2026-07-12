using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using ShokoSonarr.Config;
using ShokoSonarr.Services;
using Xunit;

namespace ShokoSonarr.Tests;

public class SonarrClientTests
{
    private static SonarrSettings TestSettings => new() { BaseUrl = "http://sonarr.local:8989", ApiKey = "testkey" };

    private class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    [Fact]
    public async Task TestConnectionAsync_OnSuccess_ReturnsSuccessTrue()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.TestConnectionAsync(TestSettings);

        Assert.True(result.Success);
        Assert.Equal("testkey", handler.LastRequest!.Headers.GetValues("X-Api-Key").Single());
    }

    [Fact]
    public async Task TestConnectionAsync_OnUnreachable_ReturnsFailureNotException()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("connection refused"));
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.TestConnectionAsync(TestSettings);

        Assert.False(result.Success);
        Assert.Contains("connection refused", result.ErrorMessage);
    }

    [Fact]
    public async Task LookupByTvdbIdAsync_SendsCorrectQueryTerm()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"tvdbId":81797,"title":"One Piece","year":1999}]"""),
        });
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.LookupByTvdbIdAsync(TestSettings, 81797);

        Assert.True(result.Success);
        Assert.Single(result.Data!);
        Assert.Equal("One Piece", result.Data![0].Title);
        Assert.Contains("term=tvdb%3A81797", handler.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task AddSeriesAsync_SendsMonitorNoneAndSearchFalse()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":55}"""),
        });
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.AddSeriesAsync(TestSettings, tvdbId: 81797, title: "One Piece", qualityProfileId: 4, rootFolderPath: "/anime");

        Assert.True(result.Success);
        Assert.Equal(55, result.Data);
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("none", body.RootElement.GetProperty("addOptions").GetProperty("monitor").GetString());
        Assert.False(body.RootElement.GetProperty("addOptions").GetProperty("searchForMissingEpisodes").GetBoolean());
        Assert.Equal(4, body.RootElement.GetProperty("qualityProfileId").GetInt32());
        Assert.Equal("/anime", body.RootElement.GetProperty("rootFolderPath").GetString());
    }

    [Fact]
    public async Task AddSeriesAsync_WithDiscoveryMode_SendsMonitorAllAndSearchTrue()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":56}"""),
        });
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.AddSeriesAsync(TestSettings, tvdbId: 81798, title: "Some Sequel", qualityProfileId: 4, rootFolderPath: "/anime", monitorMode: "all", searchOnAdd: true);

        Assert.True(result.Success);
        Assert.Equal(56, result.Data);
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("all", body.RootElement.GetProperty("addOptions").GetProperty("monitor").GetString());
        Assert.True(body.RootElement.GetProperty("addOptions").GetProperty("searchForMissingEpisodes").GetBoolean());
    }

    [Fact]
    public async Task AddSeriesAsync_WithTagIds_SendsTagsArray()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":57}"""),
        });
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.AddSeriesAsync(TestSettings, tvdbId: 81799, title: "Tagged Show", qualityProfileId: 4, rootFolderPath: "/anime", tagIds: [3, 5]);

        Assert.True(result.Success);
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var tags = body.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetInt32()).ToList();
        Assert.Equal([3, 5], tags);
    }

    [Fact]
    public async Task GetTagsAsync_ParsesTagList()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"id":1,"label":"anime"},{"id":2,"label":"One Piece"}]"""),
        });
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.GetTagsAsync(TestSettings);

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Count);
        Assert.Equal("One Piece", result.Data![1].Label);
    }

    [Fact]
    public async Task CreateTagAsync_SendsLabelAndParsesResponse()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":9,"label":"One Piece"}"""),
        });
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.CreateTagAsync(TestSettings, "One Piece");

        Assert.True(result.Success);
        Assert.Equal(9, result.Data!.Id);
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("One Piece", body.RootElement.GetProperty("label").GetString());
    }

    [Fact]
    public async Task EnsureTagIdAsync_ExistingTagCaseInsensitive_ReturnsExistingId()
    {
        var handler = new FakeHandler(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""[{"id":4,"label":"one piece"}]""") }
            : throw new InvalidOperationException("should not create a tag that already exists"));
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.EnsureTagIdAsync(TestSettings, "One Piece");

        Assert.True(result.Success);
        Assert.Equal(4, result.Data);
    }

    [Fact]
    public async Task EnsureTagIdAsync_NoExistingTag_CreatesAndReturnsNewId()
    {
        var handler = new FakeHandler(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") }
            : new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("""{"id":11,"label":"New Franchise"}""") });
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.EnsureTagIdAsync(TestSettings, "New Franchise");

        Assert.True(result.Success);
        Assert.Equal(11, result.Data);
    }

    [Fact]
    public async Task UpdateSeriesTagAsync_TagNotYetPresent_AddsItAndPutsFullResource()
    {
        var handler = new FakeHandler(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":42,"title":"Existing Show","tvdbId":81797,"tags":[1]}""") }
            : new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("""{"id":42,"title":"Existing Show","tvdbId":81797,"tags":[1,7]}""") });
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.UpdateSeriesTagAsync(TestSettings, sonarrSeriesId: 42, tagId: 7);

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Put, handler.LastRequest!.Method);
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var tags = body.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetInt32()).ToList();
        Assert.Contains(7, tags);
        Assert.Contains(1, tags);
        Assert.Equal("Existing Show", body.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task UpdateSeriesTagAsync_TagAlreadyPresent_StillSucceeds()
    {
        var handler = new FakeHandler(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":42,"title":"Existing Show","tags":[7]}""") }
            : new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("""{"id":42,"title":"Existing Show","tags":[7]}""") });
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.UpdateSeriesTagAsync(TestSettings, sonarrSeriesId: 42, tagId: 7);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task AddSeriesAsync_WhenIdMissingFromResponse_ReturnsFailureNotException()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}") });
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.AddSeriesAsync(TestSettings, tvdbId: 81797, title: "One Piece", qualityProfileId: 4, rootFolderPath: "/anime");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task GetQualityProfilesAsync_ParsesProfiles()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"id":4,"name":"HD-1080p"}]"""),
        });
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.GetQualityProfilesAsync(TestSettings);

        Assert.True(result.Success);
        Assert.Single(result.Data!);
        Assert.Equal("HD-1080p", result.Data![0].Name);
    }

    [Fact]
    public async Task GetRootFoldersAsync_ParsesRootFolders()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"id":1,"path":"/anime"}]"""),
        });
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.GetRootFoldersAsync(TestSettings);

        Assert.True(result.Success);
        Assert.Single(result.Data!);
        Assert.Equal("/anime", result.Data![0].Path);
    }

    [Fact]
    public async Task GetExistingSeriesByTvdbIdAsync_ParsesExistingSeries()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"id":55}]"""),
        });
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.GetExistingSeriesByTvdbIdAsync(TestSettings, 81797);

        Assert.True(result.Success);
        Assert.Single(result.Data!);
        Assert.Equal(55, result.Data![0].Id);
    }

    [Fact]
    public async Task MonitorEpisodesAsync_SendsCorrectEpisodeIdsAndMonitoredTrue()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{}") });
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.MonitorEpisodesAsync(TestSettings, [100, 101]);

        Assert.True(result.Success);
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal(2, body.RootElement.GetProperty("episodeIds").GetArrayLength());
        Assert.True(body.RootElement.GetProperty("monitored").GetBoolean());
    }

    [Fact]
    public async Task UnmonitorEpisodesAsync_SendsCorrectEpisodeIdsAndMonitoredFalse()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{}") });
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.UnmonitorEpisodesAsync(TestSettings, [100, 101]);

        Assert.True(result.Success);
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal(2, body.RootElement.GetProperty("episodeIds").GetArrayLength());
        Assert.False(body.RootElement.GetProperty("monitored").GetBoolean());
    }

    [Fact]
    public async Task TriggerEpisodeSearchAsync_SendsEpisodeSearchCommand()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}") });
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.TriggerEpisodeSearchAsync(TestSettings, [100, 101]);

        Assert.True(result.Success);
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("EpisodeSearch", body.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task NonSuccessStatusCode_ReturnsFailureWithStatusInMessage()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("Unauthorized") });
        var client = new SonarrClient(new HttpClient(handler));

        var result = await client.TestConnectionAsync(TestSettings);

        Assert.False(result.Success);
        Assert.Contains("401", result.ErrorMessage);
    }
}
