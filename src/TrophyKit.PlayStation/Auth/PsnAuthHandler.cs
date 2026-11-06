using System.Net;
using System.Net.Http.Headers;

namespace TrophyKit.PlayStation.Auth;

/// <summary>
/// Adds the bearer token to API requests and, on a 401, renews the token and retries once.
/// </summary>
public sealed class PsnAuthHandler : DelegatingHandler
{
    private readonly PsnTokenManager _tokens;

    /// <summary>Creates the handler.</summary>
    public PsnAuthHandler(PsnTokenManager tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        _tokens = tokens;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var token = await _tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        token = await _tokens.RenewAfterRejectionAsync(token, cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
