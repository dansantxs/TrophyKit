#pragma warning disable CA1812 // Instantiated by System.Text.Json.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrophyKit.RetroAchievements.Internal;

internal sealed class UserProfileDto
{
    public string? User { get; set; }
    [JsonPropertyName("ULID")]
    public string? Ulid { get; set; }
    public string? UserPic { get; set; }
    public string? MemberSince { get; set; }
    public string? Motto { get; set; }
    public string? RichPresenceMsg { get; set; }
    [JsonPropertyName("LastGameID")]
    public int? LastGameId { get; set; }
    public int TotalPoints { get; set; }
    public int TotalSoftcorePoints { get; set; }
    public int TotalTruePoints { get; set; }
}

internal sealed class CompletionProgressDto
{
    public int Count { get; set; }
    public int Total { get; set; }
    public List<CompletionGameDto>? Results { get; set; }
}

internal sealed class CompletionGameDto
{
    [JsonPropertyName("GameID")]
    public int GameId { get; set; }
    public string? Title { get; set; }
    public string? ImageIcon { get; set; }
    [JsonPropertyName("ConsoleID")]
    public int ConsoleId { get; set; }
    public string? ConsoleName { get; set; }
    public int MaxPossible { get; set; }
    public int NumAwarded { get; set; }
    public int NumAwardedHardcore { get; set; }
    public string? MostRecentAwardedDate { get; set; }
    public string? HighestAwardKind { get; set; }
    public string? HighestAwardDate { get; set; }
}

internal sealed class GameInfoDto
{
    [JsonPropertyName("ID")]
    public int Id { get; set; }
    public string? Title { get; set; }
    [JsonPropertyName("ConsoleID")]
    public int ConsoleId { get; set; }
    public string? ConsoleName { get; set; }
    public string? ImageIcon { get; set; }
    public int NumDistinctPlayers { get; set; }
    public int NumAwardedToUser { get; set; }
    public int NumAwardedToUserHardcore { get; set; }
    public string? HighestAwardKind { get; set; }
    public string? HighestAwardDate { get; set; }

    // An object keyed by id, or an empty array when the game has no achievements.
    public JsonElement Achievements { get; set; }
}

internal sealed class AchievementDto
{
    [JsonPropertyName("ID")]
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public int Points { get; set; }
    public int TrueRatio { get; set; }
    public string? BadgeName { get; set; }
    public int DisplayOrder { get; set; }
    public int NumAwarded { get; set; }
    public int NumAwardedHardcore { get; set; }
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    public string? DateEarned { get; set; }
    public string? DateEarnedHardcore { get; set; }
}

internal sealed class UserAwardsDto
{
    public int TotalAwardsCount { get; set; }
    public int MasteryAwardsCount { get; set; }
    public int CompletionAwardsCount { get; set; }
    public int BeatenHardcoreAwardsCount { get; set; }
    public int BeatenSoftcoreAwardsCount { get; set; }
    public List<UserAwardDto>? VisibleUserAwards { get; set; }
}

internal sealed class UserAwardDto
{
    public string? AwardedAt { get; set; }
    public string? AwardType { get; set; }
    public int? AwardData { get; set; }
    public int? AwardDataExtra { get; set; }
    public string? Title { get; set; }
    public string? ConsoleName { get; set; }
    public string? ImageIcon { get; set; }
}
