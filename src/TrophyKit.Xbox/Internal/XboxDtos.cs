#pragma warning disable CA1812 // Instantiated by System.Text.Json.
namespace TrophyKit.Xbox.Internal;

internal sealed class ProfileResponseDto
{
    public List<ProfileUserDto>? ProfileUsers { get; set; }
}

internal sealed class ProfileUserDto
{
    public string? Id { get; set; }
    public List<ProfileSettingDto>? Settings { get; set; }
}

internal sealed class ProfileSettingDto
{
    public string? Id { get; set; }
    public string? Value { get; set; }
}

internal sealed class TitleHistoryDto
{
    public List<TitleDto>? Titles { get; set; }
}

internal sealed class TitleDto
{
    public string? TitleId { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public List<string>? Devices { get; set; }
    public string? DisplayImage { get; set; }
    public TitleAchievementDto? Achievement { get; set; }
    public TitleHistoryInfoDto? TitleHistory { get; set; }
}

internal sealed class TitleAchievementDto
{
    public int CurrentAchievements { get; set; }
    public int TotalAchievements { get; set; }
    public int CurrentGamerscore { get; set; }
    public int TotalGamerscore { get; set; }
    public double ProgressPercentage { get; set; }
    public int SourceVersion { get; set; }
}

internal sealed class TitleHistoryInfoDto
{
    public DateTimeOffset? LastTimePlayed { get; set; }
}

internal sealed class AchievementsResponseDto
{
    public List<AchievementDto>? Achievements { get; set; }
    public PagingInfoDto? PagingInfo { get; set; }
}

internal sealed class PagingInfoDto
{
    public string? ContinuationToken { get; set; }
    public int TotalRecords { get; set; }
}

internal sealed class AchievementDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? LockedDescription { get; set; }
    public bool IsSecret { get; set; }
    public string? ProgressState { get; set; }
    public ProgressionDto? Progression { get; set; }
    public List<MediaAssetDto>? MediaAssets { get; set; }
    public List<RewardDto>? Rewards { get; set; }
    public RarityDto? Rarity { get; set; }
}

internal sealed class ProgressionDto
{
    public DateTimeOffset? TimeUnlocked { get; set; }
}

internal sealed class MediaAssetDto
{
    public string? Type { get; set; }
    public string? Url { get; set; }
}

internal sealed class RewardDto
{
    public string? Type { get; set; }
    public string? Value { get; set; }
}

internal sealed class RarityDto
{
    public string? CurrentCategory { get; set; }
    public decimal? CurrentPercentage { get; set; }
}
