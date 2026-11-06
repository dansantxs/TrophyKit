namespace TrophyKit.Xbox.Auth;

/// <summary>Microsoft account OAuth tokens.</summary>
/// <param name="AccessToken">Access token used to obtain Xbox Live tokens.</param>
/// <param name="ExpiresAt">When the access token expires (about 1 hour).</param>
/// <param name="RefreshToken">Refresh token (long-lived; rotated on each refresh).</param>
public sealed record MicrosoftTokens(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken);

/// <summary>The Xbox Live user token, an intermediate step to get the XSTS token.</summary>
/// <param name="Token">The token.</param>
/// <param name="NotAfter">Expiration.</param>
public sealed record XboxUserToken(string Token, DateTimeOffset NotAfter);

/// <summary>The XSTS token that authorizes calls to the Xbox Live services.</summary>
/// <param name="Token">The token.</param>
/// <param name="NotAfter">Expiration (about 16 hours).</param>
/// <param name="UserHash">User hash (<c>uhs</c>), part of the authorization header.</param>
/// <param name="Xuid">Xbox user id of the signed-in account.</param>
/// <param name="Gamertag">Gamertag of the signed-in account.</param>
public sealed record XstsToken(string Token, DateTimeOffset NotAfter, string UserHash, string? Xuid, string? Gamertag)
{
    /// <summary>The <c>Authorization</c> header value: <c>XBL3.0 x={uhs};{token}</c>.</summary>
    public string AuthorizationHeader => $"XBL3.0 x={UserHash};{Token}";
}

/// <summary>Everything the client keeps in the token store.</summary>
/// <param name="Microsoft">Microsoft account tokens.</param>
/// <param name="Xsts">Current XSTS token, if one was obtained.</param>
public sealed record XboxTokens(MicrosoftTokens Microsoft, XstsToken? Xsts);
