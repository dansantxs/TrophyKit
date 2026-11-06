using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using TrophyKit.Http;
using TrophyKit.RetroAchievements.Internal;

namespace TrophyKit.RetroAchievements;

/// <summary>
/// Client for the RetroAchievements Web API (https://api-docs.retroachievements.org).
/// </summary>
/// <remarks>Thread-safe. Create one instance and reuse it.</remarks>
public sealed class RetroAchievementsClient : IDisposable
{
    private const int MaxPageSize = 500;

    private readonly HttpClient _http;
    private readonly RetroAchievementsClientOptions _options;
    private readonly IDisposable[] _owned;

    /// <summary>Creates a client without dependency injection.</summary>
    /// <param name="options">Settings; <see cref="RetroAchievementsClientOptions.ApiKey"/> is required.</param>
    /// <param name="primaryHandler">Innermost HTTP handler, for tests or proxies.</param>
    public RetroAchievementsClient(RetroAchievementsClientOptions options, HttpMessageHandler? primaryHandler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        var limiter = RateLimitHandler.CreateLimiter(options.Http);
        _http = TrophyKitHttp.CreateClient(options.Http, limiter, RaParsing.Platform, primaryHandler);
        _owned = limiter is null ? [] : [limiter];
        OwnsHttp = true;
    }

    /// <summary>Creates a client over an <see cref="HttpClient"/> with the TrophyKit pipeline (used by dependency injection).</summary>
    public RetroAchievementsClient(HttpClient httpClient, RetroAchievementsClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        _http = httpClient;
        _options = options;
        _owned = [];
    }

    private bool OwnsHttp { get; }

    /// <summary>Gets a user profile.</summary>
    /// <param name="user">User name or ULID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TrophyKitNotFoundException">The user does not exist.</exception>
    public async Task<RaUserProfile> GetUserProfileAsync(string user, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        var dto = await GetAsync<UserProfileDto>("API_GetUserProfile.php", cancellationToken, ("u", user)).ConfigureAwait(false);
        if (string.IsNullOrEmpty(dto.User))
        {
            throw new TrophyKitNotFoundException(RaParsing.Platform, $"RetroAchievements user '{user}' not found.");
        }

        return new RaUserProfile(
            dto.User,
            dto.Ulid,
            Media(dto.UserPic),
            RaParsing.ParseDate(dto.MemberSince),
            NullIfEmpty(dto.Motto),
            NullIfEmpty(dto.RichPresenceMsg),
            dto.LastGameId,
            dto.TotalPoints,
            dto.TotalSoftcorePoints,
            dto.TotalTruePoints);
    }

    /// <summary>Gets one page of the games the user played, with progress and highest award.</summary>
    /// <param name="user">User name or ULID.</param>
    /// <param name="count">Page size, 1–500.</param>
    /// <param name="offset">Number of games to skip.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RaGameProgressPage> GetCompletionProgressAsync(
        string user, int count = MaxPageSize, int offset = 0, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaxPageSize);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        var dto = await GetAsync<CompletionProgressDto>(
            "API_GetUserCompletionProgress.php",
            cancellationToken,
            ("u", user),
            ("c", count.ToString(CultureInfo.InvariantCulture)),
            ("o", offset.ToString(CultureInfo.InvariantCulture))).ConfigureAwait(false);

        var games = (dto.Results ?? []).Select(g => new RaGameProgress(
            g.GameId,
            g.Title ?? string.Empty,
            Media(g.ImageIcon),
            g.ConsoleId,
            g.ConsoleName,
            g.MaxPossible,
            g.NumAwarded,
            g.NumAwardedHardcore,
            RaParsing.ParseDate(g.MostRecentAwardedDate),
            RaParsing.ParseAward(g.HighestAwardKind),
            RaParsing.ParseDate(g.HighestAwardDate))).ToList();
        return new RaGameProgressPage(games, dto.Total);
    }

    /// <summary>Enumerates every game the user played, fetching pages as needed.</summary>
    /// <param name="user">User name or ULID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async IAsyncEnumerable<RaGameProgress> GetAllCompletionProgressAsync(
        string user, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var offset = 0;
        while (true)
        {
            var page = await GetCompletionProgressAsync(user, MaxPageSize, offset, cancellationToken).ConfigureAwait(false);
            foreach (var game in page.Games)
            {
                yield return game;
            }

            offset += page.Games.Count;
            if (page.Games.Count == 0 || offset >= page.Total)
            {
                yield break;
            }
        }
    }

    /// <summary>Gets a game with all its achievements and the user's progress in each.</summary>
    /// <param name="user">User name or ULID.</param>
    /// <param name="gameId">Game id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RaGameDetails> GetGameProgressAsync(string user, int gameId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        ArgumentOutOfRangeException.ThrowIfLessThan(gameId, 1);

        var dto = await GetAsync<GameInfoDto>(
            "API_GetGameInfoAndUserProgress.php",
            cancellationToken,
            ("g", gameId.ToString(CultureInfo.InvariantCulture)),
            ("u", user),
            ("a", "1")).ConfigureAwait(false);
        if (dto.Id == 0)
        {
            throw new TrophyKitNotFoundException(RaParsing.Platform, $"RetroAchievements game {gameId} not found.");
        }

        var achievements = new List<RaAchievement>();
        if (dto.Achievements.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in dto.Achievements.EnumerateObject())
            {
                var a = property.Value.Deserialize<AchievementDto>(HttpJson.Options);
                if (a is null)
                {
                    continue;
                }

                achievements.Add(new RaAchievement(
                    a.Id,
                    a.Title ?? string.Empty,
                    NullIfEmpty(a.Description),
                    a.Points,
                    a.TrueRatio,
                    NullIfEmpty(a.Type),
                    a.BadgeName is null ? null : Media($"/Badge/{a.BadgeName}.png"),
                    a.BadgeName is null ? null : Media($"/Badge/{a.BadgeName}_lock.png"),
                    a.DisplayOrder,
                    a.NumAwarded,
                    a.NumAwardedHardcore,
                    RaParsing.ParseDate(a.DateEarned),
                    RaParsing.ParseDate(a.DateEarnedHardcore)));
            }
        }

        return new RaGameDetails(
            dto.Id,
            dto.Title ?? string.Empty,
            dto.ConsoleId,
            dto.ConsoleName,
            Media(dto.ImageIcon),
            dto.NumDistinctPlayers,
            dto.NumAwardedToUser,
            dto.NumAwardedToUserHardcore,
            RaParsing.ParseAward(dto.HighestAwardKind),
            RaParsing.ParseDate(dto.HighestAwardDate),
            achievements.OrderBy(a => a.DisplayOrder).ThenBy(a => a.Id).ToList());
    }

    /// <summary>Gets the awards (masteries, completions, beaten games, events) shown on the user's profile.</summary>
    /// <param name="user">User name or ULID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RaUserAwards> GetUserAwardsAsync(string user, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        var dto = await GetAsync<UserAwardsDto>("API_GetUserAwards.php", cancellationToken, ("u", user)).ConfigureAwait(false);
        var awards = (dto.VisibleUserAwards ?? []).Select(a => new RaAward(
            RaParsing.ParseDate(a.AwardedAt),
            a.AwardType,
            a.AwardData,
            a.AwardDataExtra == 1,
            a.Title,
            a.ConsoleName,
            Media(a.ImageIcon))).ToList();
        return new RaUserAwards(
            dto.TotalAwardsCount,
            dto.MasteryAwardsCount,
            dto.CompletionAwardsCount,
            dto.BeatenHardcoreAwardsCount,
            dto.BeatenSoftcoreAwardsCount,
            awards);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!OwnsHttp)
        {
            return;
        }

        _http.Dispose();
        foreach (var d in _owned)
        {
            d.Dispose();
        }
    }

    private async Task<T> GetAsync<T>(string endpoint, CancellationToken cancellationToken, params (string Name, string Value)[] query)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new TrophyKitAuthenticationException(RaParsing.Platform, "RetroAchievementsClientOptions.ApiKey is required.");
        }

        var url = new StringBuilder(new Uri(_options.BaseUrl, endpoint).AbsoluteUri).Append("?y=").Append(Uri.EscapeDataString(_options.ApiKey));
        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            url.Append("&z=").Append(Uri.EscapeDataString(_options.Username));
        }

        foreach (var (name, value) in query)
        {
            url.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url.ToString());
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Validation errors, e.g. "The selected u is invalid." for an unknown user.
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var error = TryParseError(body);
            throw new TrophyKitNotFoundException(
                RaParsing.Platform,
                $"RetroAchievements rejected the request to {endpoint}: {error.Message ?? "invalid parameters"}",
                response.StatusCode,
                error.Code,
                body);
        }

        return await HttpJson.ReadAsync<T>(response, RaParsing.Platform, RaParsing.ParseError, cancellationToken).ConfigureAwait(false);
    }

    private static Http.PlatformError TryParseError(string body)
    {
        try
        {
            return RaParsing.ParseError(body);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private string? Media(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? path
            : new Uri(_options.MediaBaseUrl, path).AbsoluteUri;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
