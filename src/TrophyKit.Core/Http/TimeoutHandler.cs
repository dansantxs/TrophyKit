namespace TrophyKit.Http;

/// <summary>
/// Applies a timeout to each individual attempt and turns it into a <see cref="TimeoutException"/>, so it can be
/// told apart from a cancellation requested by the caller.
/// </summary>
/// <remarks>
/// <see cref="HttpClient.Timeout"/> covers the whole pipeline, including time spent waiting for the rate limiter
/// and between retries, so TrophyKit sets it to infinite and uses this handler instead.
/// </remarks>
public sealed class TimeoutHandler : DelegatingHandler
{
    private readonly TimeSpan _timeout;

    /// <summary>Creates the handler.</summary>
    public TimeoutHandler(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_timeout == Timeout.InfiniteTimeSpan)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);
        try
        {
            return await base.SendAsync(request, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The request to {request.RequestUri?.Host} timed out after {_timeout.TotalSeconds:0.#}s.", ex);
        }
    }
}
