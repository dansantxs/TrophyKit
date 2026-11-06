using System.Threading.RateLimiting;

namespace TrophyKit.Http;

/// <summary>
/// Waits for a permit from a shared <see cref="RateLimiter"/> before each request, so the client stays under the
/// platform limits instead of hitting 429s.
/// </summary>
public sealed class RateLimitHandler : DelegatingHandler
{
    private readonly RateLimiter? _limiter;
    private readonly string _platform;

    /// <summary>
    /// Creates the handler. The limiter is not disposed by the handler, so it can be shared.
    /// A <see langword="null"/> limiter makes the handler a pass-through.
    /// </summary>
    public RateLimitHandler(RateLimiter? limiter, string platform)
    {
        _limiter = limiter;
        _platform = platform;
    }

    /// <summary>
    /// Builds the default sliding-window limiter for the given options, or <see langword="null"/> when rate
    /// limiting is disabled.
    /// </summary>
    public static RateLimiter? CreateLimiter(TrophyKitHttpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.RateLimitPermits is not > 0)
        {
            return null;
        }

        return new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = options.RateLimitPermits.Value,
            Window = options.RateLimitWindow,
            SegmentsPerWindow = 10,
            QueueLimit = options.RateLimitQueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_limiter is null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        using var lease = await _limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            TimeSpan? retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var value) ? value : null;
            throw new TrophyKitRateLimitException(
                _platform,
                $"{_platform}: local rate limit queue is full. Reduce concurrency or raise RateLimitQueueLimit.",
                retryAfter);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
