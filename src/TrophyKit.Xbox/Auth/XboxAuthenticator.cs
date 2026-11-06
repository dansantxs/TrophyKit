using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TrophyKit.Http;
using TrophyKit.Xbox.Internal;

namespace TrophyKit.Xbox.Auth;

/// <summary>
/// Low-level calls of the Xbox Live sign-in chain: Microsoft OAuth → Xbox user token → XSTS token.
/// Most applications use <see cref="XboxClient"/>, which calls this automatically.
/// </summary>
public sealed class XboxAuthenticator
{
    // Xbox Live auth expects the property names exactly as written (PascalCase).
    private static readonly System.Text.Json.JsonSerializerOptions PascalCase = new();

    private readonly Func<HttpClient> _httpClient;
    private readonly XboxClientOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the authenticator.</summary>
    public XboxAuthenticator(HttpClient httpClient, XboxClientOptions options, TimeProvider? timeProvider = null)
        : this(() => httpClient, options, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
    }

    internal XboxAuthenticator(Func<HttpClient> httpClient, XboxClientOptions options, TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        _httpClient = httpClient;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Builds the Microsoft sign-in URL. Send the user there; after consent Microsoft redirects to
    /// <see cref="XboxClientOptions.RedirectUri"/> with a <c>code</c> query parameter.
    /// </summary>
    /// <param name="state">Opaque value echoed back in the redirect, to protect against CSRF.</param>
    public Uri GetAuthorizationUrl(string? state = null)
    {
        var redirect = RequireRedirectUri();
        var url = XboxEndpoints.AuthorizeUrl +
            "?client_id=" + Uri.EscapeDataString(RequireClientId()) +
            "&response_type=code" +
            "&approval_prompt=auto" +
            "&scope=" + Uri.EscapeDataString(XboxEndpoints.Scope) +
            "&redirect_uri=" + Uri.EscapeDataString(redirect) +
            (state is null ? string.Empty : "&state=" + Uri.EscapeDataString(state));
        return new Uri(url);
    }

    /// <summary>Exchanges the authorization code received on the redirect URI for Microsoft tokens.</summary>
    public Task<MicrosoftTokens> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return RequestMicrosoftTokensAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code.Trim(),
                ["redirect_uri"] = RequireRedirectUri(),
            },
            cancellationToken);
    }

    /// <summary>Gets new Microsoft tokens from a refresh token.</summary>
    public Task<MicrosoftTokens> RefreshMicrosoftTokensAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        return RequestMicrosoftTokensAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            },
            cancellationToken);
    }

    /// <summary>Exchanges a Microsoft access token for an Xbox Live user token.</summary>
    public async Task<XboxUserToken> GetUserTokenAsync(string microsoftAccessToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(microsoftAccessToken);
        var body = new
        {
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT",
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = "d=" + microsoftAccessToken,
            },
        };
        var dto = await PostXboxAuthAsync(XboxEndpoints.UserAuthenticateUrl, body, cancellationToken).ConfigureAwait(false);
        return new XboxUserToken(dto.Token!, dto.NotAfter ?? _timeProvider.GetUtcNow().AddHours(12));
    }

    /// <summary>Exchanges an Xbox Live user token for an XSTS token for the <c>http://xboxlive.com</c> relying party.</summary>
    public async Task<XstsToken> GetXstsTokenAsync(string userToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userToken);
        var body = new
        {
            RelyingParty = "http://xboxlive.com",
            TokenType = "JWT",
            Properties = new
            {
                UserTokens = new[] { userToken },
                SandboxId = "RETAIL",
            },
        };
        var dto = await PostXboxAuthAsync(XboxEndpoints.XstsAuthorizeUrl, body, cancellationToken).ConfigureAwait(false);
        var claims = dto.DisplayClaims?.Xui?.FirstOrDefault();
        if (string.IsNullOrEmpty(claims?.Uhs))
        {
            throw new TrophyKitAuthenticationException(XboxEndpoints.Platform, "Xbox Live returned an XSTS token without a user hash.");
        }

        return new XstsToken(dto.Token!, dto.NotAfter ?? _timeProvider.GetUtcNow().AddHours(16), claims.Uhs, claims.Xid, claims.Gtg);
    }

    /// <summary>Runs the whole Xbox Live part of the chain: user token, then XSTS token.</summary>
    public async Task<XstsToken> GetXstsTokenFromMicrosoftAsync(string microsoftAccessToken, CancellationToken cancellationToken = default)
    {
        var user = await GetUserTokenAsync(microsoftAccessToken, cancellationToken).ConfigureAwait(false);
        return await GetXstsTokenAsync(user.Token, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MicrosoftTokens> RequestMicrosoftTokensAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        form["client_id"] = RequireClientId();
        form["scope"] = XboxEndpoints.Scope;
        if (!string.IsNullOrEmpty(_options.ClientSecret))
        {
            form["client_secret"] = _options.ClientSecret;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, XboxEndpoints.TokenUrl) { Content = new FormUrlEncodedContent(form) };
        var now = _timeProvider.GetUtcNow();
        using var response = await _httpClient().SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            var error = await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
            throw new TrophyKitAuthenticationException(
                XboxEndpoints.Platform,
                "Microsoft rejected the sign-in: " + (error.Message ?? "invalid grant") + ". The user must sign in again.",
                response.StatusCode,
                error.Code);
        }

        var dto = await HttpJson.ReadAsync<MicrosoftTokenResponse>(response, XboxEndpoints.Platform, XboxErrors.Parse, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(dto.AccessToken) || string.IsNullOrEmpty(dto.RefreshToken))
        {
            throw new TrophyKitAuthenticationException(
                XboxEndpoints.Platform,
                "Microsoft returned no access or refresh token. Check that the scope includes Xboxlive.offline_access.");
        }

        return new MicrosoftTokens(dto.AccessToken, now.AddSeconds(dto.ExpiresIn > 0 ? dto.ExpiresIn : 3600), dto.RefreshToken);
    }

    private async Task<XboxAuthResponse> PostXboxAuthAsync(string url, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: PascalCase) };
        request.Headers.Add("x-xbl-contract-version", "1");
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await _httpClient().SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var error = await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
            throw new TrophyKitAuthenticationException(
                XboxEndpoints.Platform,
                "Xbox Live refused to authorize the account" + (error.Message is null ? "." : ": " + error.Message),
                response.StatusCode,
                error.Code);
        }

        var dto = await HttpJson.ReadAsync<XboxAuthResponse>(response, XboxEndpoints.Platform, XboxErrors.Parse, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(dto.Token))
        {
            throw new TrophyKitAuthenticationException(XboxEndpoints.Platform, "Xbox Live returned no token.");
        }

        return dto;
    }

    private static async Task<PlatformError> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            // XSTS sometimes sends the error code only in a header.
            return response.Headers.TryGetValues("x-err", out var values)
                ? new PlatformError(values.First(), XboxErrors.DescribeXErr(values.First()))
                : default;
        }

        try
        {
            return XboxErrors.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            return default;
        }
    }

    private string RequireClientId() => string.IsNullOrWhiteSpace(_options.ClientId)
        ? throw new InvalidOperationException("XboxClientOptions.ClientId is required (the Application id of your Azure app registration).")
        : _options.ClientId;

    private string RequireRedirectUri() => string.IsNullOrWhiteSpace(_options.RedirectUri)
        ? throw new InvalidOperationException("XboxClientOptions.RedirectUri is required for the authorization code flow.")
        : _options.RedirectUri;

#pragma warning disable CA1812 // Instantiated by System.Text.Json.
    private sealed class MicrosoftTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public long ExpiresIn { get; set; }
    }

    private sealed class XboxAuthResponse
    {
        public string? Token { get; set; }
        public DateTimeOffset? NotAfter { get; set; }
        public DisplayClaimsDto? DisplayClaims { get; set; }
    }

    private sealed class DisplayClaimsDto
    {
        public List<XuiDto>? Xui { get; set; }
    }

    private sealed class XuiDto
    {
        public string? Uhs { get; set; }
        public string? Xid { get; set; }
        public string? Gtg { get; set; }
    }
#pragma warning restore CA1812
}
