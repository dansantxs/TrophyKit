using TrophyKit.Http;

namespace TrophyKit.RetroAchievements;

/// <summary>
/// Settings for <see cref="RetroAchievementsClient"/>.
/// </summary>
public sealed class RetroAchievementsClientOptions
{
    /// <summary>
    /// Web API key, from retroachievements.org → Settings → Keys. Required. Treat it like a password.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>User name that owns the API key. Optional; sent as <c>z</c> when set.</summary>
    public string? Username { get; set; }

    /// <summary>Base URL of the Web API. Default: <c>https://retroachievements.org/API/</c>.</summary>
    public Uri BaseUrl { get; set; } = new("https://retroachievements.org/API/");

    /// <summary>Base URL of images and badges. Default: <c>https://media.retroachievements.org</c>.</summary>
    public Uri MediaBaseUrl { get; set; } = new("https://media.retroachievements.org");

    /// <summary>Resilience settings. By default the client allows 60 requests per minute.</summary>
    public TrophyKitHttpOptions Http { get; set; } = new()
    {
        RateLimitPermits = 60,
        RateLimitWindow = TimeSpan.FromMinutes(1),
    };
}
