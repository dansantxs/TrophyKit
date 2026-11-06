using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using TrophyKit.Http;
using TrophyKit.PlayStation.Internal;

namespace TrophyKit.PlayStation.Auth;

/// <summary>
/// Low-level calls to Sony's OAuth endpoints: NPSSO → authorization code → tokens, and token refresh.
/// Most applications use <see cref="PsnClient"/>, which calls this automatically.
/// </summary>
public sealed class PsnAuthenticator
{
    private readonly Func<HttpClient> _httpClient;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the authenticator. The client must not follow redirects automatically.</summary>
    public PsnAuthenticator(HttpClient httpClient, TimeProvider? timeProvider = null)
        : this(() => httpClient, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
    }

    internal PsnAuthenticator(Func<HttpClient> httpClient, TimeProvider? timeProvider)
    {
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Exchanges an NPSSO token for access and refresh tokens.</summary>
    /// <exception cref="TrophyKitAuthenticationException">The NPSSO is invalid or expired.</exception>
    public async Task<PsnTokens> ExchangeNpssoAsync(string npsso, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(npsso);
        var code = await GetAuthorizationCodeAsync(npsso.Trim(), cancellationToken).ConfigureAwait(false);
        return await RequestTokensAsync(
            new Dictionary<string, string>
            {
                ["code"] = code,
                ["redirect_uri"] = PsnEndpoints.RedirectUri,
                ["grant_type"] = "authorization_code",
                ["token_format"] = "jwt",
            },
            previousRefreshToken: null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets a new access token using a refresh token.</summary>
    /// <exception cref="TrophyKitAuthenticationException">The refresh token is invalid or expired.</exception>
    public Task<PsnTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        return RequestTokensAsync(
            new Dictionary<string, string>
            {
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token",
                ["token_format"] = "jwt",
                ["scope"] = PsnEndpoints.Scope,
            },
            refreshToken,
            cancellationToken);
    }

    private async Task<string> GetAuthorizationCodeAsync(string npsso, CancellationToken cancellationToken)
    {
        var url = PsnEndpoints.AuthBase + "authorize" +
            "?access_type=offline" +
            "&client_id=" + PsnEndpoints.ClientId +
            "&redirect_uri=" + Uri.EscapeDataString(PsnEndpoints.RedirectUri) +
            "&response_type=code" +
            "&scope=" + Uri.EscapeDataString(PsnEndpoints.Scope);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", "npsso=" + npsso);

        using var response = await _httpClient().SendAsync(request, cancellationToken).ConfigureAwait(false);
        var location = response.Headers.Location?.OriginalString;
        var code = location is null ? null : QueryValue(location, "code");
        if (code is not null)
        {
            return code;
        }

        if ((int)response.StatusCode is >= 300 and < 400 || response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var error = location is null ? null : QueryValue(location, "error");
            throw new TrophyKitAuthenticationException(
                PsnEndpoints.Platform,
                "PlayStation rejected the NPSSO token: it is invalid or expired. Sign in at playstation.com and get a new one" +
                (error is null ? "." : $" ({error})."),
                response.StatusCode,
                error);
        }

        await HttpErrors.EnsureSuccessAsync(response, PsnEndpoints.Platform, PsnErrors.Parse, cancellationToken).ConfigureAwait(false);
        throw new TrophyKitAuthenticationException(
            PsnEndpoints.Platform,
            "PlayStation did not return an authorization code. The login flow may have changed.",
            response.StatusCode);
    }

    private async Task<PsnTokens> RequestTokensAsync(
        Dictionary<string, string> form, string? previousRefreshToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PsnEndpoints.AuthBase + "token")
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", PsnEndpoints.ClientBasicAuth);

        var now = _timeProvider.GetUtcNow();
        using var response = await _httpClient().SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            PlatformError error;
            try
            {
                error = PsnErrors.Parse(body);
            }
            catch (System.Text.Json.JsonException)
            {
                error = default;
            }

            throw new TrophyKitAuthenticationException(
                PsnEndpoints.Platform,
                "PlayStation rejected the token request: " + (error.Message ?? "invalid grant") + ".",
                response.StatusCode,
                error.Code);
        }

        var dto = await HttpJson.ReadAsync<TokenResponse>(response, PsnEndpoints.Platform, PsnErrors.Parse, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(dto.AccessToken))
        {
            throw new TrophyKitAuthenticationException(PsnEndpoints.Platform, "PlayStation returned no access token.");
        }

        var refreshToken = string.IsNullOrEmpty(dto.RefreshToken) ? previousRefreshToken : dto.RefreshToken;
        if (refreshToken is null)
        {
            throw new TrophyKitAuthenticationException(PsnEndpoints.Platform, "PlayStation returned no refresh token.");
        }

        return new PsnTokens(
            dto.AccessToken,
            now.AddSeconds(dto.ExpiresIn > 0 ? dto.ExpiresIn : 3600),
            refreshToken,
            now.AddSeconds(dto.RefreshTokenExpiresIn > 0 ? dto.RefreshTokenExpiresIn : 5_183_999));
    }

    private static string? QueryValue(string url, string name)
    {
        var q = url.IndexOf('?', StringComparison.Ordinal);
        if (q < 0)
        {
            return null;
        }

        foreach (var part in url[(q + 1)..].Split('&'))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && part[..eq] == name)
            {
                return Uri.UnescapeDataString(part[(eq + 1)..]);
            }
        }

        return null;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public long ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("refresh_token_expires_in")]
        public long RefreshTokenExpiresIn { get; set; }
    }
}
