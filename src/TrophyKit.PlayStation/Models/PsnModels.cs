namespace TrophyKit.PlayStation;

/// <summary>Which trophy service a title uses. PS5 titles use <see cref="Trophy2"/>; PS3, PS4 and Vita use <see cref="Trophy"/>.</summary>
public enum PsnTrophyService
{
    /// <summary>PS3, PS4 and PS Vita (<c>npServiceName=trophy</c>).</summary>
    Trophy,

    /// <summary>PS5 (<c>npServiceName=trophy2</c>).</summary>
    Trophy2,
}

/// <summary>Trophy grade.</summary>
public enum PsnTrophyType
{
    /// <summary>Bronze.</summary>
    Bronze,

    /// <summary>Silver.</summary>
    Silver,

    /// <summary>Gold.</summary>
    Gold,

    /// <summary>Platinum.</summary>
    Platinum,
}

/// <summary>Rarity bucket computed by Sony from the share of players who earned the trophy.</summary>
public enum PsnTrophyRarity
{
    /// <summary>Earned by 5% of players or fewer.</summary>
    UltraRare = 0,

    /// <summary>Earned by more than 5% and up to 10%.</summary>
    VeryRare = 1,

    /// <summary>Earned by more than 10% and up to 50%.</summary>
    Rare = 2,

    /// <summary>Earned by more than 50%.</summary>
    Common = 3,
}

/// <summary>Number of trophies per grade.</summary>
/// <param name="Bronze">Bronze trophies.</param>
/// <param name="Silver">Silver trophies.</param>
/// <param name="Gold">Gold trophies.</param>
/// <param name="Platinum">Platinum trophies.</param>
public sealed record PsnTrophyCounts(int Bronze, int Silver, int Gold, int Platinum)
{
    /// <summary>Sum of all grades.</summary>
    public int Total => Bronze + Silver + Gold + Platinum;
}

/// <summary>A PSN user profile.</summary>
/// <param name="AccountId">Numeric account id. Stable: use it as the key, the online id can change.</param>
/// <param name="OnlineId">The user name (PSN ID).</param>
/// <param name="AboutMe">Profile bio.</param>
/// <param name="AvatarUrl">URL of the largest avatar available.</param>
/// <param name="IsPlus">Whether the user has PlayStation Plus.</param>
/// <param name="IsOfficiallyVerified">Whether the account is verified by Sony.</param>
public sealed record PsnUserProfile(
    string AccountId,
    string OnlineId,
    string? AboutMe,
    string? AvatarUrl,
    bool IsPlus,
    bool IsOfficiallyVerified);

/// <summary>Trophy level and totals of an account.</summary>
/// <param name="AccountId">Account id.</param>
/// <param name="TrophyLevel">Trophy level (1–999).</param>
/// <param name="Progress">Progress to the next level, 0–100.</param>
/// <param name="Tier">Level tier (1–10), which defines the level icon.</param>
/// <param name="EarnedTrophies">Earned trophies per grade.</param>
public sealed record PsnTrophySummary(
    string AccountId,
    int TrophyLevel,
    int Progress,
    int Tier,
    PsnTrophyCounts EarnedTrophies);

/// <summary>A game (trophy set) in a user's trophy list, with the user's progress.</summary>
/// <param name="NpCommunicationId">Trophy set id (e.g. <c>NPWR12345_00</c>). Pass it to <see cref="PsnClient.GetTitleTrophiesAsync(string, string, PsnTrophyService, string, CancellationToken)"/>.</param>
/// <param name="Service">Trophy service the set belongs to.</param>
/// <param name="Name">Game name.</param>
/// <param name="Detail">Short description; usually empty for PS5 titles.</param>
/// <param name="IconUrl">Game icon.</param>
/// <param name="Platforms">Platforms, e.g. <c>["PS5"]</c> or <c>["PS4"]</c>; cross-buy sets list several.</param>
/// <param name="TrophySetVersion">Version of the trophy set; changes when DLC trophies are added.</param>
/// <param name="HasTrophyGroups">Whether the set has DLC groups besides the base game (<c>default</c>).</param>
/// <param name="DefinedTrophies">Trophies available in the set.</param>
/// <param name="EarnedTrophies">Trophies earned by the user.</param>
/// <param name="Progress">Completion percentage, 0–100, weighted by grade as shown on the console.</param>
/// <param name="IsHidden">Whether the user hid the game from their trophy list.</param>
/// <param name="LastUpdatedAt">Last time the user's progress in this set changed.</param>
public sealed record PsnTrophyTitle(
    string NpCommunicationId,
    PsnTrophyService Service,
    string Name,
    string? Detail,
    string? IconUrl,
    IReadOnlyList<string> Platforms,
    string? TrophySetVersion,
    bool HasTrophyGroups,
    PsnTrophyCounts DefinedTrophies,
    PsnTrophyCounts EarnedTrophies,
    int Progress,
    bool IsHidden,
    DateTimeOffset? LastUpdatedAt)
{
    /// <summary>Whether the platinum trophy was earned.</summary>
    public bool HasPlatinum => EarnedTrophies.Platinum > 0;
}

/// <summary>A page of trophy titles.</summary>
/// <param name="Titles">Titles in this page, most recently updated first.</param>
/// <param name="TotalItemCount">Total number of titles in the user's list.</param>
/// <param name="NextOffset">Offset of the next page, or <see langword="null"/> when this is the last one.</param>
public sealed record PsnTrophyTitlePage(IReadOnlyList<PsnTrophyTitle> Titles, int TotalItemCount, int? NextOffset);

/// <summary>A trophy, combining its definition with the user's status.</summary>
/// <param name="Id">Trophy id within the set (0 is usually the platinum).</param>
/// <param name="GroupId">Group id: <c>default</c> for the base game, <c>001</c>, <c>002</c>… for DLC.</param>
/// <param name="Type">Grade.</param>
/// <param name="Name">Name. Hidden trophies may have a placeholder name until earned.</param>
/// <param name="Detail">Description.</param>
/// <param name="IconUrl">Icon.</param>
/// <param name="IsHidden">Whether it is a hidden (secret) trophy.</param>
/// <param name="IsEarned">Whether the user earned it.</param>
/// <param name="EarnedAt">When it was earned (UTC).</param>
/// <param name="Rarity">Rarity bucket, when available.</param>
/// <param name="EarnedRate">Percentage of players who earned it, when available.</param>
/// <param name="ProgressTarget">For progress trophies (PS5 only): the value to reach.</param>
/// <param name="Progress">For progress trophies (PS5 only): the user's current value.</param>
/// <param name="ProgressRate">For progress trophies (PS5 only): progress percentage.</param>
public sealed record PsnTrophy(
    int Id,
    string GroupId,
    PsnTrophyType Type,
    string? Name,
    string? Detail,
    string? IconUrl,
    bool IsHidden,
    bool IsEarned,
    DateTimeOffset? EarnedAt,
    PsnTrophyRarity? Rarity,
    decimal? EarnedRate,
    int? ProgressTarget,
    int? Progress,
    int? ProgressRate);
