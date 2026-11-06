using System.Globalization;
using System.Text.Json;
using TrophyKit.Http;

namespace TrophyKit.RetroAchievements.Internal;

internal static class RaParsing
{
    public const string Platform = "RetroAchievements";

    private static readonly string[] DateFormats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-dd"];

    /// <summary>Parses ISO 8601 dates and the API's "yyyy-MM-dd HH:mm:ss" dates, which are UTC.</summary>
    public static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var plain))
        {
            return plain;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var iso) ? iso : null;
    }

    public static RaAwardKind? ParseAward(string? value) => value?.ToUpperInvariant() switch
    {
        "MASTERED" => RaAwardKind.Mastered,
        "COMPLETED" => RaAwardKind.Completed,
        "BEATEN-HARDCORE" => RaAwardKind.BeatenHardcore,
        "BEATEN-SOFTCORE" => RaAwardKind.BeatenSoftcore,
        _ => null,
    };

    /// <summary>Parses <c>{"message":"...","errors":{"u":["..."]}}</c>.</summary>
    public static PlatformError ParseError(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        var message = root.TryGetProperty("message", out var m) ? m.GetString()
            : root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        string? code = null;
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
        {
            code = string.Join(",", errors.EnumerateObject().Select(p => p.Name));
        }

        return new PlatformError(code, message);
    }
}
