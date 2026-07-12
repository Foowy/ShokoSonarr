using System.Linq;
using System.Net;
using System.Text.Json;
using ShokoSonarr.Config;
using ShokoSonarr.Services;
using Xunit;

namespace ShokoSonarr.Tests;

public class RadarrClientTests
{
    private static RadarrSettings TestSettings => new() { BaseUrl = "http://radarr.local:7878", ApiKey = "testkey" };

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
        var client = new RadarrClient(new HttpClient(handler));

        var result = await client.TestConnectionAsync(TestSettings);

        Assert.True(result.Success);
        Assert.Equal("testkey", handler.LastRequest!.Headers.GetValues("X-Api-Key").Single());
    }

    [Fact]
    public async Task LookupByTitleAsync_SendsCorrectQueryTerm()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"tmdbId":129,"title":"Spirited Away","year":2001}]"""),
        });
        var client = new RadarrClient(new HttpClient(handler));

        var result = await client.LookupByTitleAsync(TestSettings, "Spirited Away");

        Assert.True(result.Success);
        Assert.Single(result.Data!);
        Assert.Equal("Spirited Away", result.Data![0].Title);
        Assert.Contains("term=Spirited", handler.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task AddMovieAsync_SendsMonitoredAndSearchFlag()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":10}"""),
        });
        var client = new RadarrClient(new HttpClient(handler));

        var result = await client.AddMovieAsync(TestSettings, tmdbId: 129, title: "Spirited Away", qualityProfileId: 3, rootFolderPath: "/movies", searchOnAdd: true);

        Assert.True(result.Success);
        Assert.Equal(10, result.Data);
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.True(body.RootElement.GetProperty("monitored").GetBoolean());
        Assert.True(body.RootElement.GetProperty("addOptions").GetProperty("searchForMovie").GetBoolean());
        Assert.Equal(3, body.RootElement.GetProperty("qualityProfileId").GetInt32());
        Assert.Equal("/movies", body.RootElement.GetProperty("rootFolderPath").GetString());
    }

    [Fact]
    public async Task AddMovieAsync_WhenIdMissingFromResponse_ReturnsFailureNotException()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}") });
        var client = new RadarrClient(new HttpClient(handler));

        var result = await client.AddMovieAsync(TestSettings, tmdbId: 129, title: "Spirited Away", qualityProfileId: 3, rootFolderPath: "/movies", searchOnAdd: true);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task GetQualityProfilesAsync_ParsesProfiles()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"id":1,"name":"HD-1080p"}]"""),
        });
        var client = new RadarrClient(new HttpClient(handler));

        var result = await client.GetQualityProfilesAsync(TestSettings);

        Assert.True(result.Success);
        Assert.Equal("HD-1080p", result.Data![0].Name);
    }
}
