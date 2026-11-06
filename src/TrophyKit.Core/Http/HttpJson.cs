using System.Text.Json;

namespace TrophyKit.Http;

/// <summary>
/// JSON helpers shared by the platform clients.
/// </summary>
public static class HttpJson
{
    /// <summary>Options used to read platform responses (case-insensitive, lenient with numbers in strings).</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// Ensures the response is successful (see <see cref="HttpErrors.EnsureSuccessAsync"/>) and deserializes its body.
    /// </summary>
    /// <exception cref="TrophyKitException">The response failed or the body is not the expected JSON.</exception>
    public static async Task<T> ReadAsync<T>(
        HttpResponseMessage response,
        string platform,
        Func<string, PlatformError>? parseError = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        await HttpErrors.EnsureSuccessAsync(response, platform, parseError, cancellationToken).ConfigureAwait(false);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            try
            {
                return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken).ConfigureAwait(false)
                    ?? throw new TrophyKitException(platform, $"{platform} returned an empty response.", response.StatusCode);
            }
            catch (JsonException ex)
            {
                throw new TrophyKitException(
                    platform,
                    $"{platform} returned a response in an unexpected format. The API may have changed.",
                    response.StatusCode,
                    innerException: ex);
            }
        }
    }
}
