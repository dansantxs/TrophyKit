using System.Net;

namespace TrophyKit.Http;

/// <summary>
/// Retries transient failures (408, 429, 5xx, timeouts and network errors) with exponential backoff and jitter,
/// honouring the <c>Retry-After</c> header.
/// </summary>
public sealed class RetryHandler : DelegatingHandler
{
    private readonly TrophyKitHttpOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the handler.</summary>
    public RetryHandler(TrophyKitHttpOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken) && attempt < _options.MaxRetries)
            {
                await DelayAsync(BackoffDelay(attempt), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!IsTransient(response.StatusCode) || attempt >= _options.MaxRetries)
            {
                return response;
            }

            var retryAfter = HttpErrors.GetRetryAfter(response, _timeProvider);
            if (retryAfter > _options.MaxRetryAfter)
            {
                // Waiting that long would block the caller; let it decide.
                return response;
            }

            response.Dispose();
            await DelayAsync(retryAfter ?? BackoffDelay(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    internal static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private static bool IsTransient(Exception ex, CancellationToken cancellationToken)
        => !cancellationToken.IsCancellationRequested && ex is HttpRequestException or TimeoutException;

    private TimeSpan BackoffDelay(int attempt)
    {
        var exponential = _options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var jitter = 0.8 + (Random.Shared.NextDouble() * 0.4);
        var ms = Math.Min(exponential * jitter, _options.RetryMaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(ms);
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, _timeProvider, cancellationToken);
}
