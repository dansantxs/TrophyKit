#pragma warning disable CA1812 // Instantiated by System.Text.Json.
namespace TrophyKit.PlayStation.Internal;

internal sealed class TrophyCountsDto
{
    public int Bronze { get; set; }
    public int Silver { get; set; }
    public int Gold { get; set; }
    public int Platinum { get; set; }

    public PsnTrophyCounts ToModel() => new(Bronze, Silver, Gold, Platinum);
}

internal sealed class TrophySummaryDto
{
    public string? AccountId { get; set; }
    public int TrophyLevel { get; set; }
    public int Progress { get; set; }
    public int Tier { get; set; }
    public TrophyCountsDto? EarnedTrophies { get; set; }
}

internal sealed class TrophyTitlesDto
{
    public List<TrophyTitleDto>? TrophyTitles { get; set; }
    public int TotalItemCount { get; set; }
    public int? NextOffset { get; set; }
}

internal sealed class TrophyTitleDto
{
    public string? NpServiceName { get; set; }
    public string? NpCommunicationId { get; set; }
    public string? TrophySetVersion { get; set; }
    public string? TrophyTitleName { get; set; }
    public string? TrophyTitleDetail { get; set; }
    public string? TrophyTitleIconUrl { get; set; }
    public string? TrophyTitlePlatform { get; set; }
    public bool HasTrophyGroups { get; set; }
    public TrophyCountsDto? DefinedTrophies { get; set; }
    public TrophyCountsDto? EarnedTrophies { get; set; }
    public int Progress { get; set; }
    public bool HiddenFlag { get; set; }
    public DateTimeOffset? LastUpdatedDateTime { get; set; }
}

internal sealed class TrophyListDto
{
    public List<TrophyDto>? Trophies { get; set; }
}

internal sealed class TrophyDto
{
    public int TrophyId { get; set; }
    public bool TrophyHidden { get; set; }
    public string? TrophyType { get; set; }
    public string? TrophyName { get; set; }
    public string? TrophyDetail { get; set; }
    public string? TrophyIconUrl { get; set; }
    public string? TrophyGroupId { get; set; }
    public string? TrophyProgressTargetValue { get; set; }

    // User-specific fields.
    public bool Earned { get; set; }
    public DateTimeOffset? EarnedDateTime { get; set; }
    public int? TrophyRare { get; set; }
    public string? TrophyEarnedRate { get; set; }
    public string? Progress { get; set; }
    public int? ProgressRate { get; set; }
}

internal sealed class LegacyProfileResponseDto
{
    public LegacyProfileDto? Profile { get; set; }
}

internal sealed class LegacyProfileDto
{
    public string? OnlineId { get; set; }
    public string? AccountId { get; set; }
    public string? AboutMe { get; set; }
    public List<LegacyAvatarDto>? AvatarUrls { get; set; }
    public int Plus { get; set; }
    public bool IsOfficiallyVerified { get; set; }
}

internal sealed class LegacyAvatarDto
{
    public string? Size { get; set; }
    public string? AvatarUrl { get; set; }
}

internal sealed class ProfileDto
{
    public string? OnlineId { get; set; }
    public string? AboutMe { get; set; }
    public List<AvatarDto>? Avatars { get; set; }
    public bool IsPlus { get; set; }
    public bool IsOfficiallyVerified { get; set; }
}

internal sealed class AvatarDto
{
    public string? Size { get; set; }
    public string? Url { get; set; }
}
