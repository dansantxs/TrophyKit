using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TrophyKit.Auth;
using TrophyKit.Http;
using TrophyKit.PlayStation;
using TrophyKit.PlayStation.Auth;
using TrophyKit.PlayStation.Internal;

#pragma warning disable IDE0130 // Extension methods live in the DI namespace by convention.
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130

/// <summary>
/// Registers <see cref="PsnClient"/> in the service collection.
/// </summary>
public static class PsnServiceCollectionExtensions
{
    /// <summary>The name of the <see cref="HttpClient"/> used for API calls.</summary>
    public const string ApiClientName = "TrophyKit.PlayStation";

    /// <summary>The name of the <see cref="HttpClient"/> used for the OAuth endpoints.</summary>
    public const string AuthClientName = "TrophyKit.PlayStation.Auth";

    /// <summary>
    /// Adds <see cref="PsnClient"/> (transient), <see cref="PsnTokenManager"/> (singleton) and their HTTP clients.
    /// Registers <see cref="InMemoryTokenStore"/> as <see cref="ITokenStore"/> unless another store was registered.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddTrophyKitPlayStation(o => o.Npsso = configuration["Psn:Npsso"]);
    /// </code>
    /// </example>
    public static IServiceCollection AddTrophyKitPlayStation(this IServiceCollection services, Action<PsnClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var optionsBuilder = services.AddOptions<PsnClientOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.TryAddSingleton<ITokenStore, InMemoryTokenStore>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<PsnRateLimiter>();

        services.AddTrophyKitHttpClient(
            AuthClientName,
            PsnEndpoints.Platform,
            sp => Options(sp).Http,
            _ => null);

        services.AddTrophyKitHttpClient(
            ApiClientName,
            PsnEndpoints.Platform,
            sp => Options(sp).Http,
            sp => sp.GetRequiredService<PsnRateLimiter>().Limiter,
            sp => new PsnAuthHandler(sp.GetRequiredService<PsnTokenManager>()));

        services.TryAddSingleton(sp => new PsnTokenManager(
            new PsnAuthenticator(
                () => sp.GetRequiredService<IHttpClientFactory>().CreateClient(AuthClientName),
                sp.GetRequiredService<TimeProvider>()),
            sp.GetRequiredService<ITokenStore>(),
            Options(sp),
            sp.GetRequiredService<TimeProvider>()));

        services.TryAddTransient(sp => new PsnClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(ApiClientName),
            sp.GetRequiredService<PsnTokenManager>(),
            Options(sp)));

        return services;
    }

    private static PsnClientOptions Options(IServiceProvider sp) => sp.GetRequiredService<IOptions<PsnClientOptions>>().Value;

    /// <summary>Holds the rate limiter shared by every <see cref="PsnClient"/> resolved from the container.</summary>
    internal sealed class PsnRateLimiter(IOptions<PsnClientOptions> options) : IDisposable
    {
        public RateLimiter? Limiter { get; } = RateLimitHandler.CreateLimiter(options.Value.Http);

        public void Dispose() => Limiter?.Dispose();
    }
}
