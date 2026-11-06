using System.Globalization;
using System.Runtime.CompilerServices;
using TrophyKit.Auth;
using TrophyKit.Http;
using TrophyKit.PlayStation.Auth;
using TrophyKit.PlayStation.Internal;

namespace TrophyKit.PlayStation;

/// <summary>
/// Client for the PlayStation Network trophy and profile endpoints.
/// </summary>
/// <remarks>
/// <para>
/// Uses the same private endpoints as the PlayStation App. They are not documented by Sony and may change
/// without notice. This project is not affiliated with Sony Interactive Entertainment.
/// </para>
/// <para>
/// Methods that take an <c>accountId</c> accept <c>"me"</c> for the authenticated account. Other accounts can
/// be read only when their trophy list is visible to the authenticated account (usually: public).
/// </para>
/// <para>Thread-safe. Create one instance and reuse it.</para>
/// </remarks>
public sealed class PsnClient : IDisposable
{
    /// <summary>Use as <c>accountId</c> to refer to the authenticated account.</summary>
    public const string Me = "me";

    private const int MaxPageSize = 800;

    private readonly HttpClient _http;
    private readonly PsnClientOptions _options;
    private readonly IDisposable[] _owned;
    private (string? RefreshToken, string AccountId)? _me;

    /// <summary>
    /// Creates a client without dependency injection.
    /// </summary>
    /// <param name="options">Settings; at least <see cref="PsnClientOptions.Npsso"/> or stored tokens are needed.</param>
    /// <param name="tokenStore">Where tokens are kept between runs. Default: in memory.</param>
    /// <param name="primaryHandler">Innermost HTTP handler, for tests or proxies. Must not follow redirects.</param>
    public PsnClient(PsnClientOptions options, ITokenStore? tokenStore = null, HttpMessageHandler? primaryHandler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;

        var limiter = RateLimitHandler.CreateLimiter(options.Http);
        // Two clients share the handler chain below the auth handler; the token endpoints are not rate limited.
        var authHttp = TrophyKitHttp.CreateClient(options.Http, null, PsnEndpoints.Platform, primaryHandler is null ? null : new NonDisposingHandler(primaryHandler));
        Tokens = new PsnTokenManager(new PsnAuthenticator(authHttp), tokenStore ?? new InMemoryTokenStore(), options);
        _http = TrophyKitHttp.CreateClient(options.Http, limiter, PsnEndpoints.Platform, primaryHandler, new PsnAuthHandler(Tokens));
        _owned = limiter is null ? [authHttp, Tokens] : [authHttp, Tokens, limiter];
    }

    /// <summary>
    /// Creates a client over an <see cref="HttpClient"/> whose pipeline already contains a
    /// <see cref="PsnAuthHandler"/> (this is what the dependency injection registration does).
    /// </summary>
    public PsnClient(HttpClient httpClient, PsnTokenManager tokens, PsnClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(options);
        _http = httpClient;
        Tokens = tokens;
        _options = options;
        _owned = [];
    }

    /// <summary>Token management: sign in with a new NPSSO, sign out, inspect expiration.</summary>
    public PsnTokenManager Tokens { get; }

    /// <summary>Signs in with an NPSSO token and stores the tokens. Shortcut for <see cref="PsnTokenManager.SignInAsync"/>.</summary>
    public Task<PsnTokens> SignInAsync(string npsso, CancellationToken cancellationToken = default)
        => Tokens.SignInAsync(npsso, cancellationToken);

    /// <summary>
    /// Finds a user by online id (PSN user name), returning the account id needed by the other methods.
    /// </summary>
    /// <exception cref="TrophyKitNotFoundException">No user with that online id.</exception>
    public async Task<PsnUserProfile> GetProfileByOnlineIdAsync(string onlineId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(onlineId);
        var url = PsnEndpoints.LegacyProfileBase + Uri.EscapeDataString(onlineId.Trim()) +
            "/profile2?fields=" + Uri.EscapeDataString(PsnEndpoints.LegacyProfileFields);
        var dto = await GetAsync<LegacyProfileResponseDto>(url, cancellationToken).ConfigureAwait(false);
        var p = dto.Profile ?? throw new TrophyKitNotFoundException(PsnEndpoints.Platform, $"PlayStation user '{onlineId}' not found.");

        var avatar = p.AvatarUrls?
            .OrderByDescending(a => AvatarRank(a.Size))
            .Select(a => a.AvatarUrl)
            .FirstOrDefault(u => !string.IsNullOrEmpty(u));
        return new PsnUserProfile(
            p.AccountId ?? throw Unexpected("accountId"),
            p.OnlineId ?? onlineId,
            NullIfEmpty(p.AboutMe),
            avatar,
            p.Plus != 0,
            p.IsOfficiallyVerified);
    }

    /// <summary>Gets the profile of an account.</summary>
    /// <param name="accountId">Account id, or <see cref="Me"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<PsnUserProfile> GetProfileAsync(string accountId = Me, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        if (accountId == Me)
        {
            // The profile endpoint needs the numeric id; it does not understand "me".
            accountId = await GetMyAccountIdAsync(cancellationToken).ConfigureAwait(false);
        }

        var url = PsnEndpoints.ApiBase + $"userProfile/v1/internal/users/{Uri.EscapeDataString(accountId)}/profiles";
        var p = await GetAsync<ProfileDto>(url, cancellationToken).ConfigureAwait(false);
        var avatar = p.Avatars?
            .OrderByDescending(a => AvatarRank(a.Size))
            .Select(a => a.Url)
            .FirstOrDefault(u => !string.IsNullOrEmpty(u));
        return new PsnUserProfile(accountId, p.OnlineId ?? throw Unexpected("onlineId"), NullIfEmpty(p.AboutMe), avatar, p.IsPlus, p.IsOfficiallyVerified);
    }

    /// <summary>
    /// Returns the numeric account id of the signed-in account. The value is cached until the account's tokens change.
    /// </summary>
    public async Task<string> GetMyAccountIdAsync(CancellationToken cancellationToken = default)
    {
        if (_me is { } cached && cached.RefreshToken is not null && cached.RefreshToken == Tokens.Current?.RefreshToken)
        {
            return cached.AccountId;
        }

        var summary = await GetTrophySummaryAsync(Me, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(summary.AccountId) || summary.AccountId == Me)
        {
            throw Unexpected("accountId");
        }

        _me = (Tokens.Current?.RefreshToken, summary.AccountId);
        return summary.AccountId;
    }

    /// <summary>Gets the trophy level and trophy totals of an account.</summary>
    /// <param name="accountId">Account id, or <see cref="Me"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TrophyKitAccessDeniedException">The account's trophies are private.</exception>
    public async Task<PsnTrophySummary> GetTrophySummaryAsync(string accountId = Me, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        var url = PsnEndpoints.ApiBase + $"trophy/v1/users/{Uri.EscapeDataString(accountId)}/trophySummary";
        var dto = await GetAsync<TrophySummaryDto>(url, cancellationToken).ConfigureAwait(false);
        return new PsnTrophySummary(
            dto.AccountId ?? accountId,
            dto.TrophyLevel,
            dto.Progress,
            dto.Tier,
            dto.EarnedTrophies?.ToModel() ?? new PsnTrophyCounts(0, 0, 0, 0));
    }

    /// <summary>Gets one page of the account's trophy list, most recently updated first.</summary>
    /// <param name="accountId">Account id, or <see cref="Me"/>.</param>
    /// <param name="limit">Page size, 1–800.</param>
    /// <param name="offset">Number of titles to skip.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TrophyKitAccessDeniedException">The account's trophies are private.</exception>
    public async Task<PsnTrophyTitlePage> GetTrophyTitlesAsync(
        string accountId = Me, int limit = MaxPageSize, int offset = 0, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MaxPageSize);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        var url = PsnEndpoints.ApiBase +
            string.Create(CultureInfo.InvariantCulture, $"trophy/v1/users/{Uri.EscapeDataString(accountId)}/trophyTitles?limit={limit}&offset={offset}");
        var dto = await GetAsync<TrophyTitlesDto>(url, cancellationToken).ConfigureAwait(false);
        var titles = (dto.TrophyTitles ?? []).Select(ToModel).ToList();
        var next = dto.NextOffset is > 0 && dto.NextOffset < dto.TotalItemCount && titles.Count > 0 ? dto.NextOffset : null;
        return new PsnTrophyTitlePage(titles, dto.TotalItemCount, next);
    }

    /// <summary>Enumerates the whole trophy list of an account, fetching pages as needed.</summary>
    /// <param name="accountId">Account id, or <see cref="Me"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async IAsyncEnumerable<PsnTrophyTitle> GetAllTrophyTitlesAsync(
        string accountId = Me, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        int? offset = 0;
        while (offset is not null)
        {
            var page = await GetTrophyTitlesAsync(accountId, MaxPageSize, offset.Value, cancellationToken).ConfigureAwait(false);
            foreach (var title in page.Titles)
            {
                yield return title;
            }

            offset = page.NextOffset;
        }
    }

    /// <summary>
    /// Enumerates only the titles whose progress changed after <paramref name="since"/>, stopping as soon as an
    /// older title is reached. Use it for incremental synchronization: store the most recent
    /// <see cref="PsnTrophyTitle.LastUpdatedAt"/> and pass it on the next sync.
    /// </summary>
    /// <param name="since">Exclusive lower bound.</param>
    /// <param name="accountId">Account id, or <see cref="Me"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async IAsyncEnumerable<PsnTrophyTitle> GetTrophyTitlesUpdatedSinceAsync(
        DateTimeOffset since, string accountId = Me, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const int PageSize = 50;
        int? offset = 0;
        while (offset is not null)
        {
            var page = await GetTrophyTitlesAsync(accountId, PageSize, offset.Value, cancellationToken).ConfigureAwait(false);
            foreach (var title in page.Titles)
            {
                if (title.LastUpdatedAt is { } updated && updated <= since)
                {
                    yield break;
                }

                yield return title;
            }

            offset = page.NextOffset;
        }
    }

    /// <summary>Gets the trophies of a title for an account, merging definitions (name, icon) with the user's status.</summary>
    /// <param name="title">A title from the account's trophy list.</param>
    /// <param name="accountId">Account id, or <see cref="Me"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IReadOnlyList<PsnTrophy>> GetTitleTrophiesAsync(
        PsnTrophyTitle title, string accountId = Me, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        return GetTitleTrophiesAsync(accountId, title.NpCommunicationId, title.Service, "all", cancellationToken);
    }

    /// <summary>Gets the trophies of a title for an account, merging definitions (name, icon) with the user's status.</summary>
    /// <param name="accountId">Account id, or <see cref="Me"/>.</param>
    /// <param name="npCommunicationId">Trophy set id, e.g. <c>NPWR12345_00</c>.</param>
    /// <param name="service">Trophy service: <see cref="PsnTrophyService.Trophy2"/> for PS5, otherwise <see cref="PsnTrophyService.Trophy"/>.</param>
    /// <param name="trophyGroupId"><c>all</c>, <c>default</c> (base game) or a DLC group id such as <c>001</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TrophyKitNotFoundException">The account never played the title, or the id/service is wrong.</exception>
    public async Task<IReadOnlyList<PsnTrophy>> GetTitleTrophiesAsync(
        string accountId,
        string npCommunicationId,
        PsnTrophyService service,
        string trophyGroupId = "all",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(npCommunicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(trophyGroupId);

        var id = Uri.EscapeDataString(npCommunicationId);
        var group = Uri.EscapeDataString(trophyGroupId);
        var query = "?npServiceName=" + (service == PsnTrophyService.Trophy2 ? "trophy2" : "trophy");
        var definitionsUrl = PsnEndpoints.ApiBase + $"trophy/v1/npCommunicationIds/{id}/trophyGroups/{group}/trophies{query}";
        var earnedUrl = PsnEndpoints.ApiBase + $"trophy/v1/users/{Uri.EscapeDataString(accountId)}/npCommunicationIds/{id}/trophyGroups/{group}/trophies{query}";

        var definitionsTask = GetAsync<TrophyListDto>(definitionsUrl, cancellationToken);
        var earnedTask = GetAsync<TrophyListDto>(earnedUrl, cancellationToken);
        await Task.WhenAll(definitionsTask, earnedTask).ConfigureAwait(false);

        var earned = (earnedTask.Result.Trophies ?? []).ToDictionary(t => t.TrophyId);
        return (definitionsTask.Result.Trophies ?? [])
            .Select(d => Merge(d, earned.GetValueOrDefault(d.TrophyId)))
            .OrderBy(t => t.Id)
            .ToList();
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

    private async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept-Language", _options.AcceptLanguage);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        return await HttpJson.ReadAsync<T>(response, PsnEndpoints.Platform, PsnErrors.Parse, cancellationToken).ConfigureAwait(false);
    }

    private static PsnTrophyTitle ToModel(TrophyTitleDto t) => new(
        t.NpCommunicationId ?? throw Unexpected("npCommunicationId"),
        string.Equals(t.NpServiceName, "trophy2", StringComparison.OrdinalIgnoreCase) ? PsnTrophyService.Trophy2 : PsnTrophyService.Trophy,
        t.TrophyTitleName ?? string.Empty,
        NullIfEmpty(t.TrophyTitleDetail),
        NullIfEmpty(t.TrophyTitleIconUrl),
        (t.TrophyTitlePlatform ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        t.TrophySetVersion,
        t.HasTrophyGroups,
        t.DefinedTrophies?.ToModel() ?? new PsnTrophyCounts(0, 0, 0, 0),
        t.EarnedTrophies?.ToModel() ?? new PsnTrophyCounts(0, 0, 0, 0),
        t.Progress,
        t.HiddenFlag,
        t.LastUpdatedDateTime);

    private static PsnTrophy Merge(TrophyDto definition, TrophyDto? user) => new(
        definition.TrophyId,
        definition.TrophyGroupId ?? "default",
        ParseType(definition.TrophyType ?? user?.TrophyType),
        NullIfEmpty(definition.TrophyName),
        NullIfEmpty(definition.TrophyDetail),
        NullIfEmpty(definition.TrophyIconUrl),
        definition.TrophyHidden,
        user?.Earned ?? false,
        user?.Earned == true ? user.EarnedDateTime : null,
        user?.TrophyRare is >= 0 and <= 3 ? (PsnTrophyRarity)user.TrophyRare.Value : null,
        decimal.TryParse(user?.TrophyEarnedRate, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate) ? rate : null,
        int.TryParse(definition.TrophyProgressTargetValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var target) ? target : null,
        int.TryParse(user?.Progress, NumberStyles.Integer, CultureInfo.InvariantCulture, out var progress) ? progress : null,
        user?.ProgressRate);

    private static PsnTrophyType ParseType(string? type) => type?.ToUpperInvariant() switch
    {
        "PLATINUM" => PsnTrophyType.Platinum,
        "GOLD" => PsnTrophyType.Gold,
        "SILVER" => PsnTrophyType.Silver,
        _ => PsnTrophyType.Bronze,
    };

    private static int AvatarRank(string? size) => size?.ToUpperInvariant() switch
    {
        "XL" => 4,
        "L" => 3,
        "M" => 2,
        "S" => 1,
        _ => 0,
    };

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static TrophyKitException Unexpected(string field)
        => new(PsnEndpoints.Platform, $"PlayStation response is missing '{field}'. The API may have changed.");
}
