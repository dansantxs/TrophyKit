using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace TrophyKit.Http;

/// <summary>
/// Registers named <see cref="HttpClient"/>s with the standard TrophyKit pipeline through
/// <see cref="IHttpClientFactory"/>. Used by the platform packages' <c>AddTrophyKit*</c> extensions.
/// </summary>
public static class TrophyKitHttpClientBuilderExtensions
{
    /// <summary>
    /// Adds a named client whose pipeline is <c>[outer handlers] → retry → rate limit → timeout → primary</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The client name.</param>
    /// <param name="platform">Platform name used in error messages.</param>
    /// <param name="options">Resolves the resilience options.</param>
    /// <param name="limiter">Resolves the shared (singleton) rate limiter; may return <see langword="null"/>.</param>
    /// <param name="outerHandlers">Handlers that run before retries (e.g. authentication), outermost first.</param>
    public static IHttpClientBuilder AddTrophyKitHttpClient(
        this IServiceCollection services,
        string name,
        string platform,
        Func<IServiceProvider, TrophyKitHttpOptions> options,
        Func<IServiceProvider, RateLimiter?> limiter,
        params Func<IServiceProvider, DelegatingHandler>[] outerHandlers)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(limiter);
        ArgumentNullException.ThrowIfNull(outerHandlers);

        var builder = services
            .AddHttpClient(name, (sp, client) => TrophyKitHttp.Configure(client, options(sp)))
            .ConfigurePrimaryHttpMessageHandler(TrophyKitHttp.CreatePrimaryHandler);

        foreach (var outer in outerHandlers)
        {
            builder.AddHttpMessageHandler(outer);
        }

        return builder
            .AddHttpMessageHandler(sp => new RetryHandler(options(sp)))
            .AddHttpMessageHandler(sp => new RateLimitHandler(limiter(sp), platform))
            .AddHttpMessageHandler(sp => new TimeoutHandler(options(sp).AttemptTimeout));
    }
}
