namespace TrophyKit.PlayStation.Auth;

/// <summary>
/// The OAuth tokens of a PSN account.
/// </summary>
/// <param name="AccessToken">Bearer token sent to the API (valid for about 1 hour).</param>
/// <param name="AccessTokenExpiresAt">When the access token expires.</param>
/// <param name="RefreshToken">Token used to get a new access token (valid for about 2 months).</param>
/// <param name="RefreshTokenExpiresAt">When the refresh token expires. After that a new NPSSO is required.</param>
public sealed record PsnTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
