using Microsoft.Extensions.DependencyInjection;
using Shoko.Abstractions.Plugin;
using ShokoSonarr.BackgroundServices;
using ShokoSonarr.Services;

namespace ShokoSonarr;

/// <summary>Registers plugin services into the host's DI container.</summary>
public class ServiceRegistration : IPluginServiceRegistration
{
    /// <inheritdoc/>
    public static void RegisterServices(IServiceCollection serviceCollection, IApplicationPaths applicationPaths)
    {
        var clientName = ShokoSonarrConstants.Name.Replace(" ", "");
        serviceCollection
            .AddHttpClient(clientName, client =>
            {
                client.DefaultRequestHeaders.Add("User-Agent", $"{clientName}/{ShokoSonarrConstants.Version}");
                // A hung *arr instance would otherwise stall a scan up to the 100s default per call, across the whole reconcile loop.
                // A timeout surfaces as TaskCanceledException, which ArrClientBase already maps to a Fail result.
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        serviceCollection.AddSingleton(provider => provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName));
        serviceCollection.AddSingleton(new ScanCacheStore(applicationPaths.DataPath));
        serviceCollection.AddSingleton<MissingEpisodeScanner>();
        serviceCollection.AddSingleton<SonarrClient>();
        serviceCollection.AddSingleton<RadarrClient>();
        serviceCollection.AddSingleton<SeriesMatcher>();
        serviceCollection.AddSingleton<NotificationService>();
        serviceCollection.AddSingleton<RelatedSeriesFinder>();
        serviceCollection.AddHostedService<ScanSchedulerService>();
    }
}
