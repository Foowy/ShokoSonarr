using ShokoSonarr.Config;
using ShokoSonarr.Services;
using Xunit;

namespace ShokoSonarr.Tests;

public class NotificationServiceTests
{
    private class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public async Task NotifyAsync_NoWebhookConfigured_DoesNotSendRequest()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        var service = new NotificationService(new HttpClient(handler));

        await service.NotifyAsync(new SonarrSettings(), "test message");

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NotifyAsync_WebhookConfigured_PostsMessageAsContent()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
        var service = new NotificationService(new HttpClient(handler));
        var settings = new SonarrSettings { NotificationWebhookUrl = "https://discord.example/webhooks/123/abc" };

        await service.NotifyAsync(settings, "test message");

        Assert.Single(handler.Requests);
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        Assert.Contains("test message", body);
        Assert.Equal("https://discord.example/webhooks/123/abc", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task NotifyAsync_WebhookUnreachable_DoesNotThrow()
    {
        var service = new NotificationService(new HttpClient(new FakeHandler(_ => throw new HttpRequestException("boom"))));
        var settings = new SonarrSettings { NotificationWebhookUrl = "https://discord.example/webhooks/123/abc" };

        var exception = await Record.ExceptionAsync(() => service.NotifyAsync(settings, "test message"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task NotifyAsync_WebhookReturnsError_DoesNotThrow()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest));
        var service = new NotificationService(new HttpClient(handler));
        var settings = new SonarrSettings { NotificationWebhookUrl = "https://discord.example/webhooks/123/abc" };

        var exception = await Record.ExceptionAsync(() => service.NotifyAsync(settings, "test message"));

        Assert.Null(exception);
    }
}
