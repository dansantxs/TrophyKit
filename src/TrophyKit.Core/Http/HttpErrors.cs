using System.Net;

namespace TrophyKit.Http;

/// <summary>
/// The error code and message a platform put in an error response body.
/// </summary>
/// <param name="Code">Platform-specific error code.</param>
/// <param name="Message">Human-readable message.</param>
public readonly record struct PlatformError(string? Code, string? Message);

/// <summary>
/// Turns unsuccessful responses into typed <see cref="TrophyKitException"/>s.
/// </summary>
public static class HttpErrors
{
    private const int MaxBodyLength = 2048;

    /// <summary>
    /// Throws the matching <see cref="TrophyKitException"/> when <paramref name="response"/> is not successful.
    /// </summary>
    /// <param name="response">The response to inspect.</param>
    /// <param name="platform">Platform name used in messages.</param>
    /// <param name="parseError">Extracts the platform error from the body; may return <see langword="default"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string platform,
        Func<string, PlatformError>? parseError = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        PlatformError error = default;
        if (parseError is not null && !string.IsNullOrWhiteSpace(body))
        {
            try
            {
                error = parseError(body);
            }
#pragma warning disable CA1031 // A malformed error body must not hide the original HTTP error.
            catch (Exception)
#pragma warning restore CA1031
            {
                error = default;
            }
        }

        throw Create(response, platform, error, Truncate(body));
    }

    /// <summary>Builds the exception for an unsuccessful response without reading its body.</summary>
    public static TrophyKitException Create(HttpResponseMessage response, string platform, PlatformError error, string? body)
    {
        ArgumentNullException.ThrowIfNull(response);
        var status = response.StatusCode;
        var path = response.RequestMessage?.RequestUri?.AbsolutePath;
        var detail = error.Message ?? response.ReasonPhrase ?? status.ToString();
        var message = $"{platform} returned {(int)status} for {path}: {detail}";

        return status switch
        {
            HttpStatusCode.Unauthorized => new TrophyKitAuthenticationException(platform, message, status, error.Code, body),
            HttpStatusCode.Forbidden => new TrophyKitAccessDeniedException(platform, message, status, error.Code, body),
            HttpStatusCode.NotFound => new TrophyKitNotFoundException(platform, message, status, error.Code, body),
            HttpStatusCode.TooManyRequests => new TrophyKitRateLimitException(
                platform, message, GetRetryAfter(response, TimeProvider.System), status, error.Code, body),
            _ => new TrophyKitException(platform, message, status, error.Code, body),
        };
    }

    /// <summary>Reads the <c>Retry-After</c> header as a delay, if present.</summary>
    public static TimeSpan? GetRetryAfter(HttpResponseMessage response, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(timeProvider);
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        if (header?.Date is { } date)
        {
            var wait = date - timeProvider.GetUtcNow();
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }

    private static string? Truncate(string? body)
        => body is null || body.Length <= MaxBodyLength ? body : body[..MaxBodyLength] + "…";
}
