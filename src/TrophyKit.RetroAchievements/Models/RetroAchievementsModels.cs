namespace TrophyKit.RetroAchievements;

/// <summary>The highest award a user earned in a game.</summary>
public enum RaAwardKind
{
    /// <summary>Beaten in softcore mode (core progression achievements, save states allowed).</summary>
    BeatenSoftcore,

    /// <summary>Beaten in hardcore mode.</summary>
    BeatenHardcore,

    /// <summary>Every achievement earned in softcore mode.</summary>
    Completed,

    /// <summary>Every achievement earned in hardcore mode (the RetroAchievements equivalent of a platinum).</summary>
    Mastered,
}

/// <summary>A RetroAchievements user profile.</summary>
/// <param name="User">User name.</param>
/// <param name="Ulid">Stable unique id. Use it as the key, the user name can change.</param>
/// <param name="AvatarUrl">Avatar URL.</param>
/// <param name="MemberSince">Registration date (UTC).</param>
/// <param name="Motto">Profile motto.</param>
/// <param name="RichPresence">What the user is doing now, as reported by the emulator.</param>
/// <param name="LastGameId">Id of the last game played.</param>
/// <param name="TotalPoints">Hardcore points.</param>
/// <param name="TotalSoftcorePoints">Softcore points.</param>
/// <param name="TotalTruePoints">"RetroPoints": points weighted by rarity.</param>
public sealed record RaUserProfile(
    string User,
    string? Ulid,
    string? AvatarUrl,
    DateTimeOffset? MemberSince,
    string? Motto,
    string? RichPresence,
    int? LastGameId,
    int TotalPoints,
    int TotalSoftcorePoints,
    int TotalTruePoints);

/// <summary>A game in the user's completion progress list.</summary>
/// <param name="GameId">Game id.</param>
/// <param name="Title">Game title.</param>
/// <param name="IconUrl">Game icon.</param>
/// <param name="ConsoleId">Console id.</param>
/// <param name="ConsoleName">Console name.</param>
/// <param name="MaxPossible">Number of achievements in the game.</param>
/// <param name="NumAwarded">Achievements earned (softcore or hardcore).</param>
/// <param name="NumAwardedHardcore">Achievements earned in hardcore mode.</param>
/// <param name="MostRecentAwardedAt">When the user last earned an achievement in the game.</param>
/// <param name="HighestAward">Highest award, if any.</param>
/// <param name="HighestAwardAt">When the highest award was earned.</param>
public sealed record RaGameProgress(
    int GameId,
    string Title,
    string? IconUrl,
    int ConsoleId,
    string? ConsoleName,
    int MaxPossible,
    int NumAwarded,
    int NumAwardedHardcore,
    DateTimeOffset? MostRecentAwardedAt,
    RaAwardKind? HighestAward,
    DateTimeOffset? HighestAwardAt)
{
    /// <summary>Whether the game is mastered (all achievements in hardcore).</summary>
    public bool IsMastered => HighestAward == RaAwardKind.Mastered;
}

/// <summary>A page of the completion progress list.</summary>
/// <param name="Games">Games in this page.</param>
/// <param name="Total">Total number of games.</param>
public sealed record RaGameProgressPage(IReadOnlyList<RaGameProgress> Games, int Total);

/// <summary>An achievement with the user's status.</summary>
/// <param name="Id">Achievement id.</param>
/// <param name="Title">Title.</param>
/// <param name="Description">Description.</param>
/// <param name="Points">Points.</param>
/// <param name="TrueRatio">RetroPoints (points weighted by rarity).</param>
/// <param name="Type">Type: <c>progression</c>, <c>win_condition</c>, <c>missable</c> or <see langword="null"/>.</param>
/// <param name="BadgeUrl">Badge image.</param>
/// <param name="BadgeLockedUrl">Greyed-out badge image.</param>
/// <param name="DisplayOrder">Order defined by the set author.</param>
/// <param name="NumAwarded">Players who earned it.</param>
/// <param name="NumAwardedHardcore">Players who earned it in hardcore.</param>
/// <param name="EarnedAt">When the user earned it (any mode).</param>
/// <param name="EarnedHardcoreAt">When the user earned it in hardcore.</param>
public sealed record RaAchievement(
    int Id,
    string Title,
    string? Description,
    int Points,
    int TrueRatio,
    string? Type,
    string? BadgeUrl,
    string? BadgeLockedUrl,
    int DisplayOrder,
    int NumAwarded,
    int NumAwardedHardcore,
    DateTimeOffset? EarnedAt,
    DateTimeOffset? EarnedHardcoreAt)
{
    /// <summary>Whether the user earned it in any mode.</summary>
    public bool IsEarned => EarnedAt is not null || EarnedHardcoreAt is not null;

    /// <summary>Whether the user earned it in hardcore mode.</summary>
    public bool IsEarnedHardcore => EarnedHardcoreAt is not null;
}

/// <summary>A game with its achievements and the user's progress.</summary>
/// <param name="GameId">Game id.</param>
/// <param name="Title">Game title.</param>
/// <param name="ConsoleId">Console id.</param>
/// <param name="ConsoleName">Console name.</param>
/// <param name="IconUrl">Game icon.</param>
/// <param name="NumDistinctPlayers">Players who played the game.</param>
/// <param name="NumAwardedToUser">Achievements the user earned.</param>
/// <param name="NumAwardedToUserHardcore">Achievements the user earned in hardcore.</param>
/// <param name="HighestAward">Highest award, if any.</param>
/// <param name="HighestAwardAt">When it was earned.</param>
/// <param name="Achievements">Achievements in display order.</param>
public sealed record RaGameDetails(
    int GameId,
    string Title,
    int ConsoleId,
    string? ConsoleName,
    string? IconUrl,
    int NumDistinctPlayers,
    int NumAwardedToUser,
    int NumAwardedToUserHardcore,
    RaAwardKind? HighestAward,
    DateTimeOffset? HighestAwardAt,
    IReadOnlyList<RaAchievement> Achievements);

/// <summary>An award shown on the user's profile.</summary>
/// <param name="AwardedAt">When it was awarded.</param>
/// <param name="AwardType">Type as reported by the API, e.g. <c>Mastery/Completion</c>, <c>Game Beaten</c>, <c>Event</c>.</param>
/// <param name="GameId">Game id, for game awards.</param>
/// <param name="IsHardcore">Whether it was earned in hardcore (mastery rather than completion; hardcore beaten).</param>
/// <param name="Title">Game or award title.</param>
/// <param name="ConsoleName">Console name, for game awards.</param>
/// <param name="ImageUrl">Image.</param>
public sealed record RaAward(
    DateTimeOffset? AwardedAt,
    string? AwardType,
    int? GameId,
    bool IsHardcore,
    string? Title,
    string? ConsoleName,
    string? ImageUrl);

/// <summary>The awards of a user.</summary>
/// <param name="TotalAwardsCount">Total awards.</param>
/// <param name="MasteryAwardsCount">Mastered games.</param>
/// <param name="CompletionAwardsCount">Completed (softcore) games.</param>
/// <param name="BeatenHardcoreAwardsCount">Games beaten in hardcore.</param>
/// <param name="BeatenSoftcoreAwardsCount">Games beaten in softcore.</param>
/// <param name="Awards">Visible awards.</param>
public sealed record RaUserAwards(
    int TotalAwardsCount,
    int MasteryAwardsCount,
    int CompletionAwardsCount,
    int BeatenHardcoreAwardsCount,
    int BeatenSoftcoreAwardsCount,
    IReadOnlyList<RaAward> Awards);
