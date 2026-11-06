using System.Text.Json;
using TrophyKit.Auth;
using TrophyKit.Xbox.Internal;

namespace TrophyKit.Xbox.Auth;

/// <summary>
/// Keeps a valid XSTS token: loads tokens from the <see cref="ITokenStore"/>, refreshes the Microsoft token and
/// renews the XSTS token before they expire. Thread-safe.
/// </summary>
public sealed class XboxTokenManager : IDisposable
{
    private readonly XboxAuthenticator _authenticator;
    private readonly ITokenStore _store;
    private readonly XboxClientOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private XboxTokens? _tokens;
    private bool _loaded;

    /// <summary>Creates the manager.</summary>
    public XboxTokenManager(XboxAuthenticator authenticator, ITokenStore store, XboxClientOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        _authenticator = authenticator;
        _store = store;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The low-level authenticator, e.g. to build the sign-in URL.</summary>
    public XboxAuthenticator Authenticator => _authenticator;

    /// <summary>The current tokens, if any were loaded or obtained.</summary>
    public XboxTokens? Current => _tokens;

    /// <summary>Builds the Microsoft sign-in URL. Shortcut for <see cref="XboxAuthenticator.GetAuthorizationUrl"/>.</summary>
    public Uri GetAuthorizationUrl(string? state = null) => _authenticator.GetAuthorizationUrl(state);

    /// <summary>Completes the sign-in with the authorization code received on the redirect URI and stores the tokens.</summary>
    public async Task<XboxTokens> SignInWithCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var microsoft = await _authenticator.ExchangeCodeAsync(code, cancellationToken).ConfigureAwait(false);
        return await SignInAsync(microsoft, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Signs in with a Microsoft refresh token obtained elsewhere and stores the tokens.</summary>
    public async Task<XboxTokens> SignInWithRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var microsoft = await _authenticator.RefreshMicrosoftTokensAsync(refreshToken, cancellationToken).ConfigureAwait(false);
        return await SignInAsync(microsoft, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes the stored tokens.</summary>
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _tokens = null;
            _loaded = true;
            await _store.RemoveAsync(_options.TokenStoreKey, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Returns a valid XSTS token, renewing it if needed.</summary>
    /// <exception cref="TrophyKitAuthenticationException">No valid credentials are available.</exception>
    public async ValueTask<XstsToken> GetXstsTokenAsync(CancellationToken cancellationToken = default)
    {
        var xsts = _tokens?.Xsts;
        if (xsts is not null && IsValid(xsts.NotAfter))
        {
            return xsts;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await EnsureXstsAsync(rejected: null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Called after a 401: renews the XSTS token unless another request already did.</summary>
    internal async ValueTask<XstsToken> RenewAfterRejectionAsync(XstsToken rejected, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await EnsureXstsAsync(rejected.Token, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();

    private async Task<XboxTokens> SignInAsync(MicrosoftTokens microsoft, CancellationToken cancellationToken)
    {
        var xsts = await _authenticator.GetXstsTokenFromMicrosoftAsync(microsoft.AccessToken, cancellationToken).ConfigureAwait(false);
        var tokens = new XboxTokens(microsoft, xsts);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveAsync(tokens, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }

        return tokens;
    }

    private async Task<XstsToken> EnsureXstsAsync(string? rejected, CancellationToken cancellationToken)
    {
        if (!_loaded)
        {
            _tokens = await LoadAsync(cancellationToken).ConfigureAwait(false);
            _loaded = true;
        }

        var tokens = _tokens;
        if (tokens?.Xsts is { } current && current.Token != rejected && IsValid(current.NotAfter))
        {
            return current;
        }

        MicrosoftTokens microsoft;
        if (tokens is not null)
        {
            microsoft = IsValid(tokens.Microsoft.ExpiresAt)
                ? tokens.Microsoft
                : await _authenticator.RefreshMicrosoftTokensAsync(tokens.Microsoft.RefreshToken, cancellationToken).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(_options.RefreshToken))
        {
            microsoft = await _authenticator.RefreshMicrosoftTokensAsync(_options.RefreshToken, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new TrophyKitAuthenticationException(
                XboxEndpoints.Platform,
                "No Xbox credentials. Sign in with SignInWithCodeAsync (see GetAuthorizationUrl) or set XboxClientOptions.RefreshToken.");
        }

        // Persist the (possibly rotated) Microsoft refresh token before the XSTS step, so it is not lost if that fails.
        await SaveAsync(new XboxTokens(microsoft, null), cancellationToken).ConfigureAwait(false);
        var xsts = await _authenticator.GetXstsTokenFromMicrosoftAsync(microsoft.AccessToken, cancellationToken).ConfigureAwait(false);
        await SaveAsync(new XboxTokens(microsoft, xsts), cancellationToken).ConfigureAwait(false);
        return xsts;
    }

    private bool IsValid(DateTimeOffset expiresAt) => expiresAt - _options.TokenRefreshSkew > _timeProvider.GetUtcNow();

    private async Task<XboxTokens?> LoadAsync(CancellationToken cancellationToken)
    {
        var json = await _store.GetAsync(_options.TokenStoreKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<XboxTokens>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task SaveAsync(XboxTokens tokens, CancellationToken cancellationToken)
    {
        _tokens = tokens;
        _loaded = true;
        await _store.SetAsync(_options.TokenStoreKey, JsonSerializer.Serialize(tokens), cancellationToken).ConfigureAwait(false);
    }
}
