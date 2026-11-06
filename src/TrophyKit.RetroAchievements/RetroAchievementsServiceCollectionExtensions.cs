using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TrophyKit.Http;
using TrophyKit.RetroAchievements;
using TrophyKit.RetroAchievements.Internal;

#pragma warning disable IDE0130 // Extension methods live in the DI namespace by convention.
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130

/// <summary>
/// Registers <see cref="RetroAchievementsClient"/> in the service collection.
/// </summary>
public static class RetroAchievementsServiceCollectionExtensions
{
    /// <summary>The name of the <see cref="HttpClient"/> used for API calls.</summary>
    public const string ClientName = "TrophyKit.RetroAchievements";

    /// <summary>Adds <see cref="RetroAchievementsClient"/> (transient) and its HTTP client.</summary>
    /// <example>
    /// <code>
    /// services.AddTrophyKitRetroAchievements(o => o.ApiKey = configuration["RetroAchievements:ApiKey"]!);
    /// </code>
    /// </example>
    public static IServiceCollection AddTrophyKitRetroAchievements(
        this IServiceCollection services, Action<RetroAchievementsClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var optionsBuilder = services.AddOptions<RetroAchievementsClientOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.TryAddSingleton<RaRateLimiter>();
        services.AddTrophyKitHttpClient(
            ClientName,
            RaParsing.Platform,
            sp => Options(sp).Http,
            sp => sp.GetRequiredService<RaRateLimiter>().Limiter);

        services.TryAddTransient(sp => new RetroAchievementsClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName),
            Options(sp)));

        return services;
    }

    private static RetroAchievementsClientOptions Options(IServiceProvider sp)
        => sp.GetRequiredService<IOptions<RetroAchievementsClientOptions>>().Value;

    internal sealed class RaRateLimiter(IOptions<RetroAchievementsClientOptions> options) : IDisposable
    {
        public RateLimiter? Limiter { get; } = RateLimitHandler.CreateLimiter(options.Value.Http);

        public void Dispose() => Limiter?.Dispose();
    }
}
