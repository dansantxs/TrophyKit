using System.Globalization;
using TrophyKit.Auth;
using TrophyKit.Http;
using TrophyKit.Xbox.Auth;
using TrophyKit.Xbox.Internal;

namespace TrophyKit.Xbox;

/// <summary>
/// Client for the Xbox Live profile, title history and achievement services.
/// </summary>
/// <remarks>
/// <para>
/// Uses Xbox Live web services that are not publicly documented for third parties. They may change without
/// notice. This project is not affiliated with Microsoft.
/// </para>
/// <para>
/// Any profile can be read by xuid or gamertag, subject to the user's privacy settings. Xbox 360 achievements
/// are not supported yet: <see cref="XboxTitle.IsXbox360"/> tells which titles use them.
/// </para>
/// <para>Thread-safe. Create one instance and reuse it.</para>
/// </remarks>
public sealed class XboxClient : IDisposable
{
    private const int AchievementPageSize = 1000;

    private readonly HttpClient _http;
    private readonly XboxClientOptions _options;
    private readonly IDisposable[] _owned;

    /// <summary>Creates a client without dependency injection.</summary>
    /// <param name="options">Settings; <see cref="XboxClientOptions.ClientId"/> is required.</param>
    /// <param name="tokenStore">Where tokens are kept between runs. Default: in memory.</param>
    /// <param name="primaryHandler">Innermost HTTP handler, for tests or proxies.</param>
    public XboxClient(XboxClientOptions options, ITokenStore? tokenStore = null, HttpMessageHandler? primaryHandler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;

        var limiter = RateLimitHandler.CreateLimiter(options.Http);
        var authHttp = TrophyKitHttp.CreateClient(options.Http, null, XboxEndpoints.Platform, primaryHandler is null ? null : new NonDisposingHandler(primaryHandler));
        Tokens = new XboxTokenManager(new XboxAuthenticator(authHttp, options), tokenStore ?? new InMemoryTokenStore(), options);
        _http = TrophyKitHttp.CreateClient(options.Http, limiter, XboxEndpoints.Platform, primaryHandler, new XboxAuthHandler(Tokens));
        _owned = limiter is null ? [authHttp, Tokens] : [authHttp, Tokens, limiter];
    }

    /// <summary>
    /// Creates a client over an <see cref="HttpClient"/> whose pipeline already contains an
    /// <see cref="XboxAuthHandler"/> (this is what the dependency injection registration does).
    /// </summary>
    public XboxClient(HttpClient httpClient, XboxTokenManager tokens, XboxClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(options);
        _http = httpClient;
        Tokens = tokens;
        _options = options;
        _owned = [];
    }

    /// <summary>Token management: sign-in URL, code exchange, sign out.</summary>
    public XboxTokenManager Tokens { get; }

    /// <summary>Builds the Microsoft sign-in URL to send the user to.</summary>
    public Uri GetAuthorizationUrl(string? state = null) => Tokens.GetAuthorizationUrl(state);

    /// <summary>Completes the sign-in with the code received on the redirect URI.</summary>
    public Task<XboxTokens> SignInWithCodeAsync(string code, CancellationToken cancellationToken = default)
        => Tokens.SignInWithCodeAsync(code, cancellationToken);

    /// <summary>Gets the profile of the signed-in account.</summary>
    public async Task<XboxProfile> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var xsts = await Tokens.GetXstsTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(xsts.Xuid))
        {
            throw new TrophyKitException(XboxEndpoints.Platform, "The XSTS token does not contain the user's xuid.");
        }

        return await GetProfileAsync(xsts.Xuid, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets a profile by xuid.</summary>
    public Task<XboxProfile> GetProfileAsync(string xuid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xuid);
        return GetProfileCoreAsync($"xuid({Uri.EscapeDataString(xuid)})", xuid, cancellationToken);
    }

    /// <summary>Finds a profile by gamertag, returning the xuid needed by the other methods.</summary>
    /// <exception cref="TrophyKitNotFoundException">No account with that gamertag.</exception>
    public Task<XboxProfile> GetProfileByGamertagAsync(string gamertag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamertag);
        return GetProfileCoreAsync($"gt({Uri.EscapeDataString(gamertag.Trim())})", gamertag, cancellationToken);
    }

    /// <summary>Gets the titles the user played, with achievement progress, most recently played first.</summary>
    /// <param name="xuid">Xbox user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TrophyKitAccessDeniedException">The user's game history is private.</exception>
    public async Task<IReadOnlyList<XboxTitle>> GetTitleHistoryAsync(string xuid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xuid);
        var url = XboxEndpoints.TitleHubBase + $"xuid({Uri.EscapeDataString(xuid)})/titles/titlehistory/decoration/achievement,image";
        var dto = await GetAsync<TitleHistoryDto>(url, contractVersion: "2", cancellationToken).ConfigureAwait(false);
        return (dto.Titles ?? [])
            .Where(t => !string.IsNullOrEmpty(t.TitleId))
            .Select(ToModel)
            .OrderByDescending(t => t.LastTimePlayed)
            .ToList();
    }

    /// <summary>Gets the achievements of a title for a user.</summary>
    /// <exception cref="NotSupportedException">The title uses Xbox 360 achievements.</exception>
    public Task<IReadOnlyList<XboxAchievement>> GetAchievementsAsync(string xuid, XboxTitle title, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (title.IsXbox360)
        {
            throw new NotSupportedException($"'{title.Name}' uses Xbox 360 achievements, which are not supported yet.");
        }

        return GetAchievementsAsync(xuid, title.TitleId, cancellationToken);
    }

    /// <summary>Gets the achievements of a title (Xbox One, Series X|S and PC) for a user, following all pages.</summary>
    /// <param name="xuid">Xbox user id.</param>
    /// <param name="titleId">Title id (decimal string).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<XboxAchievement>> GetAchievementsAsync(string xuid, string titleId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xuid);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleId);

        var result = new List<XboxAchievement>();
        string? continuation = null;
        do
        {
            var url = XboxEndpoints.AchievementsBase +
                string.Create(CultureInfo.InvariantCulture, $"xuid({Uri.EscapeDataString(xuid)})/achievements?titleId={Uri.EscapeDataString(titleId)}&maxItems={AchievementPageSize}") +
                (continuation is null ? string.Empty : "&continuationToken=" + Uri.EscapeDataString(continuation));
            var page = await GetAsync<AchievementsResponseDto>(url, contractVersion: "2", cancellationToken).ConfigureAwait(false);
            var items = page.Achievements ?? [];
            result.AddRange(items.Select(ToModel));

            var next = page.PagingInfo?.ContinuationToken;
            continuation = string.IsNullOrEmpty(next) || next == continuation || items.Count == 0 ? null : next;
        }
        while (continuation is not null);

        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_owned.Length == 0)
        {
            return;
        }

        _http.Dispose();
        foreach (var d in _owned)
        {
            d.Dispose();
        }
    }

    private async Task<XboxProfile> GetProfileCoreAsync(string userSelector, string requested, CancellationToken cancellationToken)
    {
        var url = XboxEndpoints.ProfileBase + userSelector + "/profile/settings?settings=" + XboxEndpoints.ProfileSettings;
        var dto = await GetAsync<ProfileResponseDto>(url, contractVersion: "3", cancellationToken).ConfigureAwait(false);
        var user = dto.ProfileUsers?.FirstOrDefault()
            ?? throw new TrophyKitNotFoundException(XboxEndpoints.Platform, $"Xbox user '{requested}' not found.");
        var settings = (user.Settings ?? [])
            .Where(s => s.Id is not null)
            .GroupBy(s => s.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);

        return new XboxProfile(
            user.Id ?? throw new TrophyKitException(XboxEndpoints.Platform, "Xbox profile response is missing the xuid."),
            settings.GetValueOrDefault("Gamertag") ?? requested,
            int.TryParse(settings.GetValueOrDefault("Gamerscore"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var score) ? score : 0,
            NullIfEmpty(settings.GetValueOrDefault("GameDisplayPicRaw")),
            NullIfEmpty(settings.GetValueOrDefault("AccountTier")),
            NullIfEmpty(settings.GetValueOrDefault("XboxOneRep")));
    }

    private async Task<T> GetAsync<T>(string url, string contractVersion, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-xbl-contract-version", contractVersion);
        request.Headers.TryAddWithoutValidation("Accept-Language", _options.AcceptLanguage);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        return await HttpJson.ReadAsync<T>(response, XboxEndpoints.Platform, XboxErrors.Parse, cancellationToken).ConfigureAwait(false);
    }

    private static XboxTitle ToModel(TitleDto t)
    {
        var a = t.Achievement;
        return new XboxTitle(
            t.TitleId!,
            t.Name ?? string.Empty,
            t.Type,
            t.Devices ?? [],
            NullIfEmpty(t.DisplayImage),
            a?.CurrentAchievements ?? 0,
            a?.TotalAchievements ?? 0,
            a?.CurrentGamerscore ?? 0,
            a?.TotalGamerscore ?? 0,
            (int)Math.Round(a?.ProgressPercentage ?? 0),
            t.TitleHistory?.LastTimePlayed,
            a?.SourceVersion == 1 || (t.Devices?.Contains("Xbox360", StringComparer.OrdinalIgnoreCase) == true && t.Devices.Count == 1));
    }

    private static XboxAchievement ToModel(AchievementDto a)
    {
        var state = a.ProgressState?.ToUpperInvariant() switch
        {
            "ACHIEVED" => XboxAchievementState.Achieved,
            "INPROGRESS" => XboxAchievementState.InProgress,
            _ => XboxAchievementState.NotStarted,
        };
        var unlockedAt = a.Progression?.TimeUnlocked;
        var gamerscore = a.Rewards?
            .Where(r => string.Equals(r.Type, "Gamerscore", StringComparison.OrdinalIgnoreCase))
            .Select(r => int.TryParse(r.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0)
            .FirstOrDefault() ?? 0;

        return new XboxAchievement(
            a.Id ?? string.Empty,
            a.Name ?? string.Empty,
            NullIfEmpty(a.Description),
            NullIfEmpty(a.LockedDescription),
            a.IsSecret,
            state,
            state == XboxAchievementState.Achieved && unlockedAt?.Year > 1 ? unlockedAt : null,
            gamerscore,
            a.MediaAssets?.FirstOrDefault(m => string.Equals(m.Type, "Icon", StringComparison.OrdinalIgnoreCase))?.Url
                ?? a.MediaAssets?.FirstOrDefault()?.Url,
            a.Rarity?.CurrentCategory,
            a.Rarity?.CurrentPercentage);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
