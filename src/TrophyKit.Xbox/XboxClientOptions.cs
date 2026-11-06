using TrophyKit.Http;

namespace TrophyKit.Xbox;

/// <summary>
/// Settings for <see cref="XboxClient"/>.
/// </summary>
/// <remarks>
/// Xbox Live requires a Microsoft account sign-in through an app you register in the Azure portal
/// (App registrations → "Personal Microsoft accounts"). See the README for the step-by-step.
/// </remarks>
public sealed class XboxClientOptions
{
    /// <summary>Application (client) id of your Azure app registration. Required.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Client secret, only for confidential (web) apps. Leave empty for public clients.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Redirect URI registered in the app. Required to exchange an authorization code.</summary>
    public string? RedirectUri { get; set; }

    /// <summary>
    /// A Microsoft refresh token used when no tokens are in the token store. Lets servers and CI start without the
    /// interactive login. Optional.
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>Key under which tokens are saved in the token store. Default: <c>trophykit:xbox</c>.</summary>
    public string TokenStoreKey { get; set; } = "trophykit:xbox";

    /// <summary>Tokens are renewed when they expire in less than this. Default: 5 minutes.</summary>
    public TimeSpan TokenRefreshSkew { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Language of achievement names and descriptions. Default: <c>en-US</c>.</summary>
    public string AcceptLanguage { get; set; } = "en-US";

    /// <summary>
    /// Resilience settings. By default the client allows 10 requests per 15 seconds, the burst limit documented
    /// for Xbox Live services; 429 responses are retried honouring <c>Retry-After</c>.
    /// </summary>
    public TrophyKitHttpOptions Http { get; set; } = new()
    {
        RateLimitPermits = 10,
        RateLimitWindow = TimeSpan.FromSeconds(15),
    };
}
