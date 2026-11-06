using System.Text.Json;
using TrophyKit.Http;

namespace TrophyKit.Xbox.Internal;

internal static class XboxErrors
{
    /// <summary>Parses XSTS (<c>XErr</c>), OAuth (<c>error</c>) and service (<c>code</c>/<c>description</c>) error bodies.</summary>
    public static PlatformError Parse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        if (root.TryGetProperty("XErr", out var xerr))
        {
            var code = xerr.ToString();
            return new PlatformError(code, DescribeXErr(code) ?? (root.TryGetProperty("Message", out var m) ? m.GetString() : null));
        }

        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
        {
            return new PlatformError(error.GetString(), root.TryGetProperty("error_description", out var d) ? d.GetString() : error.GetString());
        }

        var serviceCode = root.TryGetProperty("code", out var c) ? c.ToString() : null;
        var description = root.TryGetProperty("description", out var desc) ? desc.GetString()
            : root.TryGetProperty("message", out var msg) ? msg.GetString() : null;
        return new PlatformError(serviceCode, description);
    }

    public static string? DescribeXErr(string? code) => code switch
    {
        "2148916227" => "The account is banned from Xbox Live.",
        "2148916229" => "The account is restricted by parental controls.",
        "2148916233" => "The Microsoft account has no Xbox profile. Sign in once at xbox.com to create one.",
        "2148916234" => "The account has not accepted the Xbox terms of use. Sign in at xbox.com to accept them.",
        "2148916235" => "Xbox Live is not available in the account's country.",
        "2148916236" or "2148916237" => "The account requires adult verification (South Korea).",
        "2148916238" => "The account belongs to a child and must be added to a Microsoft family.",
        _ => null,
    };
}
