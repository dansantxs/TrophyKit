using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TrophyKit.Auth;
using TrophyKit.Http;
using TrophyKit.Xbox;
using TrophyKit.Xbox.Auth;
using TrophyKit.Xbox.Internal;

#pragma warning disable IDE0130 // Extension methods live in the DI namespace by convention.
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130

/// <summary>
/// Registers <see cref="XboxClient"/> in the service collection.
/// </summary>
public static class XboxServiceCollectionExtensions
{
    /// <summary>The name of the <see cref="HttpClient"/> used for API calls.</summary>
    public const string ApiClientName = "TrophyKit.Xbox";

    /// <summary>The name of the <see cref="HttpClient"/> used for the OAuth endpoints.</summary>
    public const string AuthClientName = "TrophyKit.Xbox.Auth";

    /// <summary>
    /// Adds <see cref="XboxClient"/> (transient), <see cref="XboxTokenManager"/> (singleton) and their HTTP clients.
    /// Registers <see cref="InMemoryTokenStore"/> as <see cref="ITokenStore"/> unless another store was registered.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddTrophyKitXbox(o => o.ClientId = configuration["Xbox:ClientId"]);
    /// </code>
    /// </example>
    public static IServiceCollection AddTrophyKitXbox(this IServiceCollection services, Action<XboxClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var optionsBuilder = services.AddOptions<XboxClientOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.TryAddSingleton<ITokenStore, InMemoryTokenStore>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<XboxRateLimiter>();

        services.AddTrophyKitHttpClient(
            AuthClientName,
            XboxEndpoints.Platform,
            sp => Options(sp).Http,
            _ => null);

        services.AddTrophyKitHttpClient(
            ApiClientName,
            XboxEndpoints.Platform,
            sp => Options(sp).Http,
            sp => sp.GetRequiredService<XboxRateLimiter>().Limiter,
            sp => new XboxAuthHandler(sp.GetRequiredService<XboxTokenManager>()));

        services.TryAddSingleton(sp => new XboxTokenManager(
            new XboxAuthenticator(
                () => sp.GetRequiredService<IHttpClientFactory>().CreateClient(AuthClientName),
                Options(sp),
                sp.GetRequiredService<TimeProvider>()),
            sp.GetRequiredService<ITokenStore>(),
            Options(sp),
            sp.GetRequiredService<TimeProvider>()));

        services.TryAddTransient(sp => new XboxClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(ApiClientName),
            sp.GetRequiredService<XboxTokenManager>(),
            Options(sp)));

        return services;
    }

    private static XboxClientOptions Options(IServiceProvider sp) => sp.GetRequiredService<IOptions<XboxClientOptions>>().Value;

    /// <summary>Holds the rate limiter shared by every <see cref="XboxClient"/> resolved from the container.</summary>
    internal sealed class XboxRateLimiter(IOptions<XboxClientOptions> options) : IDisposable
    {
        public RateLimiter? Limiter { get; } = RateLimitHandler.CreateLimiter(options.Value.Http);

        public void Dispose() => Limiter?.Dispose();
    }
}
