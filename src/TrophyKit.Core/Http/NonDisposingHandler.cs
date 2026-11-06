namespace TrophyKit.Http;

/// <summary>
/// Wraps a handler so that several pipelines can share it without disposing it more than once.
/// Disposing this wrapper leaves the inner handler alive.
/// </summary>
public sealed class NonDisposingHandler : HttpMessageHandler
{
    private readonly HttpMessageInvoker _invoker;

    /// <summary>Creates the wrapper.</summary>
    public NonDisposingHandler(HttpMessageHandler inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _invoker = new HttpMessageInvoker(inner, disposeHandler: false);
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => _invoker.SendAsync(request, cancellationToken);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _invoker.Dispose();
        }

        base.Dispose(disposing);
    }
}
