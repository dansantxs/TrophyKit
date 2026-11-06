using System.Text.Json;
using TrophyKit.Auth;
using TrophyKit.PlayStation.Internal;

namespace TrophyKit.PlayStation.Auth;

/// <summary>
/// Keeps a valid access token: loads it from the <see cref="ITokenStore"/>, refreshes it before it expires and
/// falls back to the NPSSO when the refresh token is no longer valid. Thread-safe.
/// </summary>
public sealed class PsnTokenManager : IDisposable
{
    private readonly PsnAuthenticator _authenticator;
    private readonly ITokenStore _store;
    private readonly PsnClientOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private PsnTokens? _tokens;
    private bool _loaded;

    /// <summary>Creates the manager.</summary>
    public PsnTokenManager(PsnAuthenticator authenticator, ITokenStore store, PsnClientOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        _authenticator = authenticator;
        _store = store;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Signs in with an NPSSO token and saves the resulting tokens, replacing any previous ones.
    /// Use it when the user provides a new NPSSO at runtime.
    /// </summary>
    public async Task<PsnTokens> SignInAsync(string npsso, CancellationToken cancellationToken = default)
    {
        var tokens = await _authenticator.ExchangeNpssoAsync(npsso, cancellationToken).ConfigureAwait(false);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveAsync(tokens, cancellationToken).ConfigureAwait(false);
            return tokens;
        }
        finally
        {
            _lock.Release();
        }
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

    /// <summary>The current tokens, if any were loaded or obtained. Useful to inspect expiration dates.</summary>
    public PsnTokens? Current => _tokens;

    /// <summary>Returns a valid access token, refreshing or signing in again if needed.</summary>
    /// <exception cref="TrophyKitAuthenticationException">No valid credentials are available.</exception>
    public async ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var tokens = _tokens;
        if (tokens is not null && IsAccessValid(tokens))
        {
            return tokens.AccessToken;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await EnsureTokensAsync(rejectedAccessToken: null, cancellationToken).ConfigureAwait(false)).AccessToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Called when the API rejected <paramref name="accessToken"/> (HTTP 401). Returns a new token, or the current
    /// one if another request already renewed it.
    /// </summary>
    internal async ValueTask<string> RenewAfterRejectionAsync(string accessToken, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await EnsureTokensAsync(accessToken, cancellationToken).ConfigureAwait(false)).AccessToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();

    private async Task<PsnTokens> EnsureTokensAsync(string? rejectedAccessToken, CancellationToken cancellationToken)
    {
        if (!_loaded)
        {
            _tokens = await LoadAsync(cancellationToken).ConfigureAwait(false);
            _loaded = true;
        }

        var tokens = _tokens;
        if (tokens is not null && tokens.AccessToken != rejectedAccessToken && IsAccessValid(tokens))
        {
            return tokens;
        }

        TrophyKitAuthenticationException? refreshError = null;
        if (tokens is not null && tokens.RefreshTokenExpiresAt > _timeProvider.GetUtcNow())
        {
            try
            {
                var refreshed = await _authenticator.RefreshAsync(tokens.RefreshToken, cancellationToken).ConfigureAwait(false);
                await SaveAsync(refreshed, cancellationToken).ConfigureAwait(false);
                return refreshed;
            }
            catch (TrophyKitAuthenticationException ex)
            {
                refreshError = ex;
            }
        }

        if (!string.IsNullOrWhiteSpace(_options.Npsso))
        {
            var fresh = await _authenticator.ExchangeNpssoAsync(_options.Npsso, cancellationToken).ConfigureAwait(false);
            await SaveAsync(fresh, cancellationToken).ConfigureAwait(false);
            return fresh;
        }

        throw new TrophyKitAuthenticationException(
            PsnEndpoints.Platform,
            tokens is null
                ? "No PlayStation credentials. Set PsnClientOptions.Npsso or call SignInAsync with an NPSSO token."
                : "The PlayStation session expired and no NPSSO is configured. Call SignInAsync with a new NPSSO token.",
            innerException: refreshError);
    }

    private bool IsAccessValid(PsnTokens tokens)
        => tokens.AccessTokenExpiresAt - _options.TokenRefreshSkew > _timeProvider.GetUtcNow();

    private async Task<PsnTokens?> LoadAsync(CancellationToken cancellationToken)
    {
        var json = await _store.GetAsync(_options.TokenStoreKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PsnTokens>(json);
        }
        catch (JsonException)
        {
            return null; // Corrupted entry: behave as if there were no tokens.
        }
    }

    private async Task SaveAsync(PsnTokens tokens, CancellationToken cancellationToken)
    {
        _tokens = tokens;
        _loaded = true;
        await _store.SetAsync(_options.TokenStoreKey, JsonSerializer.Serialize(tokens), cancellationToken).ConfigureAwait(false);
    }
}
