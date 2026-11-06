using System.Net;

namespace TrophyKit.Xbox.Auth;

/// <summary>
/// Adds the XBL3.0 authorization header and, on a 401, renews the XSTS token and retries once.
/// </summary>
public sealed class XboxAuthHandler : DelegatingHandler
{
    private readonly XboxTokenManager _tokens;

    /// <summary>Creates the handler.</summary>
    public XboxAuthHandler(XboxTokenManager tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        _tokens = tokens;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var xsts = await _tokens.GetXstsTokenAsync(cancellationToken).ConfigureAwait(false);
        SetAuthorization(request, xsts);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        xsts = await _tokens.RenewAfterRejectionAsync(xsts, cancellationToken).ConfigureAwait(false);
        SetAuthorization(request, xsts);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static void SetAuthorization(HttpRequestMessage request, XstsToken xsts)
    {
        request.Headers.Remove("Authorization");
        request.Headers.TryAddWithoutValidation("Authorization", xsts.AuthorizationHeader);
    }
}
