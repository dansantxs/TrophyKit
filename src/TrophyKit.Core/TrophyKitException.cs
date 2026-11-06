using System.Net;

namespace TrophyKit;

/// <summary>
/// Base exception for every error raised by a TrophyKit client.
/// </summary>
public class TrophyKitException : Exception
{
    /// <summary>Creates a new exception.</summary>
    public TrophyKitException()
    {
        Platform = "unknown";
    }

    /// <summary>Creates a new exception.</summary>
    public TrophyKitException(string message)
        : base(message)
    {
        Platform = "unknown";
    }

    /// <summary>Creates a new exception.</summary>
    public TrophyKitException(string message, Exception? innerException)
        : base(message, innerException)
    {
        Platform = "unknown";
    }

    /// <summary>Creates a new exception.</summary>
    public TrophyKitException(
        string platform,
        string message,
        HttpStatusCode? statusCode = null,
        string? errorCode = null,
        string? responseBody = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Platform = platform;
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ResponseBody = responseBody;
    }

    /// <summary>The platform that produced the error (e.g. <c>PlayStation</c>, <c>Xbox</c>).</summary>
    public string Platform { get; }

    /// <summary>The HTTP status code returned by the platform, when there was a response.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>The platform-specific error code, when the platform returned one.</summary>
    public string? ErrorCode { get; }

    /// <summary>The raw response body, truncated, useful for diagnostics. May be <see langword="null"/>.</summary>
    public string? ResponseBody { get; }
}

/// <summary>
/// The credentials are missing, invalid or expired (e.g. an expired NPSSO or a revoked refresh token).
/// The user has to authenticate again.
/// </summary>
public class TrophyKitAuthenticationException : TrophyKitException
{
    /// <summary>Creates a new exception.</summary>
    public TrophyKitAuthenticationException() { }

    /// <summary>Creates a new exception.</summary>
    public TrophyKitAuthenticationException(string message) : base(message) { }

    /// <summary>Creates a new exception.</summary>
    public TrophyKitAuthenticationException(string message, Exception? innerException) : base(message, innerException) { }

    /// <summary>Creates a new exception.</summary>
    public TrophyKitAuthenticationException(
        string platform, string message, HttpStatusCode? statusCode = null, string? errorCode = null,
        string? responseBody = null, Exception? innerException = null)
        : base(platform, message, statusCode, errorCode, responseBody, innerException) { }
}

/// <summary>
/// The platform refused access to the resource, usually because the profile or its trophies/achievements are private.
/// </summary>
public class TrophyKitAccessDeniedException : TrophyKitException
{
    /// <summary>Creates a new exception.</summary>
    public TrophyKitAccessDeniedException() { }

    /// <summary>Creates a new exception.</summary>
    public TrophyKitAccessDeniedException(string message) : base(message) { }

    /// <summary>Creates a new exception.</summary>
    public TrophyKitAccessDeniedException(string message, Exception? innerException) : base(message, innerException) { }

    /// <summary>Creates a new exception.</summary>
    public TrophyKitAccessDeniedException(
        string platform, string message, HttpStatusCode? statusCode = null, string? errorCode = null,
        string? responseBody = null, Exception? innerException = null)
        : base(platform, message, statusCode, errorCode, responseBody, innerException) { }
}

/// <summary>
/// The requested user, title or resource does not exist.
/// </summary>
public class TrophyKitNotFoundException : TrophyKitException
{
    /// <summary>Creates a new exception.</summary>
    public TrophyKitNotFoundException() { }

    /// <summary>Creates a new exception.</summary>
    public TrophyKitNotFoundException(string message) : base(message) { }

    /// <summary>Creates a new exception.</summary>
    public TrophyKitNotFoundException(string message, Exception? innerException) : base(message, innerException) { }

    /// <summary>Creates a new exception.</summary>
    public TrophyKitNotFoundException(
        string platform, string message, HttpStatusCode? statusCode = null, string? errorCode = null,
        string? responseBody = null, Exception? innerException = null)
        : base(platform, message, statusCode, errorCode, responseBody, innerException) { }
}

/// <summary>
/// The platform rate limit was exceeded and the retries were exhausted, or the local request queue is full.
/// </summary>
public class TrophyKitRateLimitException : TrophyKitException
{
    /// <summary>Creates a new exception.</summary>
    public TrophyKitRateLimitException() { }

    /// <summary>Creates a new exception.</summary>
    public TrophyKitRateLimitException(string message) : base(message) { }

    /// <summary>Creates a new exception.</summary>
    public TrophyKitRateLimitException(string message, Exception? innerException) : base(message, innerException) { }

    /// <summary>Creates a new exception.</summary>
    public TrophyKitRateLimitException(
        string platform, string message, TimeSpan? retryAfter = null, HttpStatusCode? statusCode = null,
        string? errorCode = null, string? responseBody = null, Exception? innerException = null)
        : base(platform, message, statusCode, errorCode, responseBody, innerException)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>How long the platform asked to wait before trying again, when it said so.</summary>
    public TimeSpan? RetryAfter { get; }
}
