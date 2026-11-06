namespace TrophyKit.Xbox;

/// <summary>An Xbox Live profile.</summary>
/// <param name="Xuid">Xbox user id. Stable: use it as the key, the gamertag can change.</param>
/// <param name="Gamertag">Gamertag.</param>
/// <param name="Gamerscore">Total gamerscore.</param>
/// <param name="GamerPicUrl">Gamerpic URL.</param>
/// <param name="AccountTier">Account tier (e.g. <c>Gold</c>, <c>Silver</c>).</param>
/// <param name="Reputation">Reputation (e.g. <c>GoodPlayer</c>).</param>
public sealed record XboxProfile(
    string Xuid,
    string Gamertag,
    int Gamerscore,
    string? GamerPicUrl,
    string? AccountTier,
    string? Reputation);

/// <summary>A title in the user's history, with achievement progress.</summary>
/// <param name="TitleId">Title id (decimal string). Pass it to <see cref="XboxClient.GetAchievementsAsync(string, string, CancellationToken)"/>.</param>
/// <param name="Name">Title name.</param>
/// <param name="Type">Title type (e.g. <c>Game</c>, <c>App</c>).</param>
/// <param name="Devices">Devices the title runs on (e.g. <c>XboxSeries</c>, <c>XboxOne</c>, <c>PC</c>, <c>Xbox360</c>).</param>
/// <param name="ImageUrl">Display image.</param>
/// <param name="CurrentAchievements">Achievements unlocked by the user.</param>
/// <param name="TotalAchievements">Achievements available.</param>
/// <param name="CurrentGamerscore">Gamerscore earned by the user.</param>
/// <param name="TotalGamerscore">Gamerscore available.</param>
/// <param name="ProgressPercentage">Completion percentage, 0–100.</param>
/// <param name="LastTimePlayed">Last time the user played the title.</param>
/// <param name="IsXbox360">Whether achievements use the Xbox 360 format (not supported by <see cref="XboxClient.GetAchievementsAsync(string, XboxTitle, CancellationToken)"/> yet).</param>
public sealed record XboxTitle(
    string TitleId,
    string Name,
    string? Type,
    IReadOnlyList<string> Devices,
    string? ImageUrl,
    int CurrentAchievements,
    int TotalAchievements,
    int CurrentGamerscore,
    int TotalGamerscore,
    int ProgressPercentage,
    DateTimeOffset? LastTimePlayed,
    bool IsXbox360)
{
    /// <summary>Whether every achievement was unlocked (the Xbox equivalent of a platinum).</summary>
    public bool IsCompleted => TotalAchievements > 0 && CurrentAchievements >= TotalAchievements;
}

/// <summary>Progress state of an achievement.</summary>
public enum XboxAchievementState
{
    /// <summary>No progress.</summary>
    NotStarted,

    /// <summary>Partially completed.</summary>
    InProgress,

    /// <summary>Unlocked.</summary>
    Achieved,
}

/// <summary>An achievement with the user's status.</summary>
/// <param name="Id">Achievement id within the title.</param>
/// <param name="Name">Name.</param>
/// <param name="Description">Description shown once unlocked.</param>
/// <param name="LockedDescription">Description shown while locked.</param>
/// <param name="IsSecret">Whether it is a secret achievement.</param>
/// <param name="State">Progress state.</param>
/// <param name="UnlockedAt">When it was unlocked (UTC).</param>
/// <param name="Gamerscore">Gamerscore awarded.</param>
/// <param name="IconUrl">Icon.</param>
/// <param name="RarityCategory">Rarity category (<c>Rare</c> or <c>Common</c>).</param>
/// <param name="RarityPercentage">Percentage of players who unlocked it.</param>
public sealed record XboxAchievement(
    string Id,
    string Name,
    string? Description,
    string? LockedDescription,
    bool IsSecret,
    XboxAchievementState State,
    DateTimeOffset? UnlockedAt,
    int Gamerscore,
    string? IconUrl,
    string? RarityCategory,
    decimal? RarityPercentage)
{
    /// <summary>Whether the achievement is unlocked.</summary>
    public bool IsUnlocked => State == XboxAchievementState.Achieved;
}
