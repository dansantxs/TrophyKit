using TrophyKit.Http;

namespace TrophyKit.PlayStation;

/// <summary>
/// Settings for <see cref="PsnClient"/>.
/// </summary>
public sealed class PsnClientOptions
{
    /// <summary>
    /// The NPSSO token of the account used to call the API (64 characters). Get it by signing in at
    /// playstation.com and opening <c>https://ca.account.sony.com/api/v1/ssocookie</c>.
    /// </summary>
    /// <remarks>
    /// Optional when valid tokens are already in the <see cref="TrophyKit.Auth.ITokenStore"/>. When set, it is used whenever
    /// the stored tokens are missing or the refresh token expired. Treat it like a password.
    /// </remarks>
    public string? Npsso { get; set; }

    /// <summary>
    /// Key under which tokens are saved in the token store. Use a different key per account when you run several
    /// accounts in the same store. Default: <c>trophykit:psn</c>.
    /// </summary>
    public string TokenStoreKey { get; set; } = "trophykit:psn";

    /// <summary>
    /// The access token is refreshed when it expires in less than this. Default: 2 minutes.
    /// </summary>
    public TimeSpan TokenRefreshSkew { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Value of the <c>Accept-Language</c> header, which controls the language of trophy names and descriptions.
    /// Default: <c>en-US</c>.
    /// </summary>
    public string AcceptLanguage { get; set; } = "en-US";

    /// <summary>
    /// Resilience settings. By default the client allows 300 requests per 15 minutes, the limit reported by the
    /// community (Sony does not document it).
    /// </summary>
    public TrophyKitHttpOptions Http { get; set; } = new()
    {
        RateLimitPermits = 300,
        RateLimitWindow = TimeSpan.FromMinutes(15),
    };
}
