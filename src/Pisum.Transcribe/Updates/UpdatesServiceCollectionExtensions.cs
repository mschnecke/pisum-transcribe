using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pisum.Transcribe.Updates;

/// <summary>
/// Registers the update check feature.
/// </summary>
internal static class UpdatesServiceCollectionExtensions
{
    /// <summary>
    /// Adds the <see cref="UpdateCheckService"/>, which asks GitHub once a day whether a new version exists and adds
    /// its tray item. Register it last, so the item sits directly above <b>Exit</b>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddUpdates(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(UpdateCheckService.HttpClientName, client =>
            {
                client.Timeout = UpdateCheckService.RequestTimeout;

                // GitHub rejects a request without a user agent. It names the app without its version.
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Pisum-Transcribe");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            })

            // Redirects are followed, so installed copies keep finding releases after the repository is renamed or
            // transferred. No cookie is kept, so a redirect carries nothing that the first request didn't.
            .ConfigurePrimaryHttpMessageHandler(() =>
                new SocketsHttpHandler {AllowAutoRedirect = true, UseCookies = false})

            // The factory's own entries would add the URL; the service logs only versions, status codes and durations.
            .RemoveAllLoggers();
        services.AddHostedService<UpdateCheckService>();
        return services;
    }
}
