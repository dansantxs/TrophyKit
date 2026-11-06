using System.Text.Json;
using TrophyKit.Http;

namespace TrophyKit.PlayStation.Internal;

internal static class PsnErrors
{
    /// <summary>Parses <c>{"error":{"code":2240526,"message":"..."}}</c> and the OAuth <c>{"error":"...","error_description":"..."}</c>.</summary>
    public static PlatformError Parse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error", out var error))
        {
            return default;
        }

        if (error.ValueKind == JsonValueKind.String)
        {
            var description = root.TryGetProperty("error_description", out var d) ? d.GetString() : null;
            return new PlatformError(error.GetString(), description ?? error.GetString());
        }

        if (error.ValueKind == JsonValueKind.Object)
        {
            var code = error.TryGetProperty("code", out var c) ? c.ToString() : null;
            var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
            return new PlatformError(code, message);
        }

        return default;
    }
}
