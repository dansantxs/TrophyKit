using TrophyKit.PlayStation;
using TrophyKit.RetroAchievements;
using TrophyKit.Xbox;

namespace TrophyKit.IntegrationTests;

/// <summary>
/// Contract tests against the real APIs. Each test is skipped unless its credentials are set:
/// PSN_NPSSO; XBOX_CLIENT_ID + XBOX_REFRESH_TOKEN; RA_API_KEY + RA_USER.
/// They check that the responses still have the shape the clients expect.
/// </summary>
public class LiveApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Require(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(value), $"{name} is not set.");
        return value!;
    }

    [Fact]
    public async Task PlayStation_contract()
    {
        using var psn = new PsnClient(new PsnClientOptions { Npsso = Require("PSN_NPSSO") });

        var me = await psn.GetProfileAsync(PsnClient.Me, Ct);
        Assert.False(string.IsNullOrEmpty(me.OnlineId));

        var lookup = await psn.GetProfileByOnlineIdAsync(me.OnlineId, Ct);
        Assert.Matches("^[0-9]+$", lookup.AccountId);

        var summary = await psn.GetTrophySummaryAsync(cancellationToken: Ct);
        Assert.InRange(summary.TrophyLevel, 1, 999);

        var page = await psn.GetTrophyTitlesAsync(limit: 5, cancellationToken: Ct);
        Assert.SkipWhen(page.Titles.Count == 0, "The account has no trophy titles.");
        var title = page.Titles[0];
        Assert.Matches("^NPWR[0-9]+_[0-9]+$", title.NpCommunicationId);
        Assert.NotEmpty(title.Platforms);
        Assert.NotNull(title.LastUpdatedAt);

        var trophies = await psn.GetTitleTrophiesAsync(title, cancellationToken: Ct);
        Assert.Equal(title.DefinedTrophies.Total, trophies.Count);
        Assert.All(trophies, t => Assert.False(string.IsNullOrEmpty(t.IconUrl)));
        Assert.Equal(title.EarnedTrophies.Total, trophies.Count(t => t.IsEarned));
    }

    [Fact]
    public async Task Xbox_contract()
    {
        using var xbox = new XboxClient(new XboxClientOptions
        {
            ClientId = Require("XBOX_CLIENT_ID"),
            ClientSecret = Environment.GetEnvironmentVariable("XBOX_CLIENT_SECRET"),
            RefreshToken = Require("XBOX_REFRESH_TOKEN"),
        });

        var me = await xbox.GetCurrentUserAsync(Ct);
        Assert.False(string.IsNullOrEmpty(me.Gamertag));

        var byGamertag = await xbox.GetProfileByGamertagAsync(me.Gamertag, Ct);
        Assert.Equal(me.Xuid, byGamertag.Xuid);

        var titles = await xbox.GetTitleHistoryAsync(me.Xuid, Ct);
        var title = titles.FirstOrDefault(t => !t.IsXbox360 && t.CurrentAchievements > 0);
        Assert.SkipWhen(title is null, "The account has no Xbox One/Series titles with achievements.");

        var achievements = await xbox.GetAchievementsAsync(me.Xuid, title!, Ct);
        Assert.Equal(title!.TotalAchievements, achievements.Count);
        Assert.Equal(title.CurrentAchievements, achievements.Count(a => a.IsUnlocked));
        Assert.Equal(title.CurrentGamerscore, achievements.Where(a => a.IsUnlocked).Sum(a => a.Gamerscore));
    }

    [Fact]
    public async Task RetroAchievements_contract()
    {
        using var ra = new RetroAchievementsClient(new RetroAchievementsClientOptions { ApiKey = Require("RA_API_KEY") });
        var user = Require("RA_USER");

        var profile = await ra.GetUserProfileAsync(user, Ct);
        Assert.False(string.IsNullOrEmpty(profile.Ulid));

        var page = await ra.GetCompletionProgressAsync(user, count: 5, cancellationToken: Ct);
        Assert.SkipWhen(page.Games.Count == 0, "The user has no games.");
        var game = page.Games[0];

        var details = await ra.GetGameProgressAsync(user, game.GameId, Ct);
        Assert.Equal(game.MaxPossible, details.Achievements.Count);
        Assert.Equal(game.NumAwarded, details.Achievements.Count(a => a.IsEarned));

        var awards = await ra.GetUserAwardsAsync(user, Ct);
        Assert.True(awards.TotalAwardsCount >= awards.Awards.Count);
    }
}
