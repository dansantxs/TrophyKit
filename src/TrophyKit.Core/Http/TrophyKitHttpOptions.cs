namespace TrophyKit.Http;

/// <summary>
/// Resilience settings shared by every platform client.
/// </summary>
public sealed class TrophyKitHttpOptions
{
    /// <summary>
    /// Maximum time for a single HTTP attempt (not counting the time spent waiting for the rate limiter).
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How many times a failed request is retried (429, 5xx, timeouts, network errors). Default: 3.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>First retry delay; doubles on each attempt, with jitter. Default: 1 second.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Upper bound for the delay between retries. Default: 30 seconds.</summary>
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// If the platform asks to wait longer than this (<c>Retry-After</c>), the client stops retrying and throws
    /// <see cref="TrophyKitRateLimitException"/> instead of blocking. Default: 60 seconds.
    /// </summary>
    public TimeSpan MaxRetryAfter { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Requests allowed per <see cref="RateLimitWindow"/>. <see langword="null"/> or 0 disables local rate limiting.
    /// Each platform sets its own default.
    /// </summary>
    public int? RateLimitPermits { get; set; }

    /// <summary>Length of the sliding rate limit window.</summary>
    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How many requests may wait for a permit. When the queue is full new requests fail immediately with
    /// <see cref="TrophyKitRateLimitException"/>. Default: 1000.
    /// </summary>
    public int RateLimitQueueLimit { get; set; } = 1000;

    /// <summary>User-Agent header sent with every request. <see langword="null"/> keeps the platform default.</summary>
    public string? UserAgent { get; set; }

    internal void Validate()
    {
        if (AttemptTimeout <= TimeSpan.Zero && AttemptTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(AttemptTimeout), "AttemptTimeout must be positive.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(MaxRetries);
        ArgumentOutOfRangeException.ThrowIfNegative(RateLimitQueueLimit);
        if (RateLimitPermits is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RateLimitPermits), "RateLimitPermits cannot be negative.");
        }

        if (RateLimitPermits is > 0 && RateLimitWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RateLimitWindow), "RateLimitWindow must be positive.");
        }
    }
}
