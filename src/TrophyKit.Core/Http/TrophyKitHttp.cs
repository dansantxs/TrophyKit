using System.Net;
using System.Reflection;
using System.Threading.RateLimiting;

namespace TrophyKit.Http;

/// <summary>
/// Builds the standard TrophyKit HTTP pipeline:
/// <c>[extra handlers] → retry → rate limit → per-attempt timeout → primary handler</c>.
/// </summary>
public static class TrophyKitHttp
{
    /// <summary>Default User-Agent: <c>TrophyKit/&lt;version&gt;</c>.</summary>
    public static string DefaultUserAgent { get; } =
        "TrophyKit/" + (typeof(TrophyKitHttp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0] ?? "0.0.0");

    /// <summary>
    /// Creates an <see cref="HttpClient"/> with the standard pipeline. Used when the client is created without
    /// dependency injection.
    /// </summary>
    /// <param name="options">Resilience options.</param>
    /// <param name="limiter">Shared rate limiter, or <see langword="null"/> for none.</param>
    /// <param name="platform">Platform name used in error messages.</param>
    /// <param name="primaryHandler">Innermost handler; defaults to a new <see cref="SocketsHttpHandler"/>.</param>
    /// <param name="outerHandlers">Handlers that run before retries (e.g. authentication), outermost first.</param>
    public static HttpClient CreateClient(
        TrophyKitHttpOptions options,
        RateLimiter? limiter,
        string platform,
        HttpMessageHandler? primaryHandler = null,
        params DelegatingHandler[] outerHandlers)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(outerHandlers);
        options.Validate();

        HttpMessageHandler pipeline = primaryHandler ?? CreatePrimaryHandler();
        pipeline = new TimeoutHandler(options.AttemptTimeout) { InnerHandler = pipeline };
        if (limiter is not null)
        {
            pipeline = new RateLimitHandler(limiter, platform) { InnerHandler = pipeline };
        }

        pipeline = new RetryHandler(options) { InnerHandler = pipeline };
        for (var i = outerHandlers.Length - 1; i >= 0; i--)
        {
            outerHandlers[i].InnerHandler = pipeline;
            pipeline = outerHandlers[i];
        }

        var client = new HttpClient(pipeline, disposeHandler: true);
        Configure(client, options);
        return client;
    }

    /// <summary>Applies timeout and User-Agent settings to a client (also used by the DI registration).</summary>
    public static void Configure(HttpClient client, TrophyKitHttpOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent ?? DefaultUserAgent);
    }

    /// <summary>The default primary handler: connection pooling with DNS refresh and automatic decompression.</summary>
    public static HttpMessageHandler CreatePrimaryHandler() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = false,
    };
}
