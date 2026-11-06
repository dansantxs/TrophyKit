using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TrophyKit.Tests.Fakes;
using TrophyKit.Tests.PlayStation;
using TrophyKit.Xbox;

namespace TrophyKit.Tests.Xbox;

public class XboxClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Gets_profile_by_gamertag()
    {
        var fake = new FakeHttpHandler().WithXboxAuth()
            .Route("profile.xboxlive.com/users/gt(Some Hunter)/profile/settings", HttpStatusCode.OK, Fixtures.Read("Xbox/profile.json"));
        using var xbox = XboxFake.Client(fake);

        var profile = await xbox.GetProfileByGamertagAsync("Some Hunter", Ct);

        Assert.Equal("2533274800000001", profile.Xuid);
        Assert.Equal("Some Hunter", profile.Gamertag);
        Assert.Equal(123456, profile.Gamerscore);
        Assert.Equal("Gold", profile.AccountTier);
        Assert.Equal("GoodPlayer", profile.Reputation);
        Assert.StartsWith("https://images-eds-ssl.xboxlive.com", profile.GamerPicUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Current_user_uses_xuid_from_xsts()
    {
        var fake = new FakeHttpHandler().WithXboxAuth()
            .Route("profile.xboxlive.com/users/xuid(2533274800000001)/", HttpStatusCode.OK, Fixtures.Read("Xbox/profile.json"));
        using var xbox = XboxFake.Client(fake);

        var me = await xbox.GetCurrentUserAsync(Ct);

        Assert.Equal("Some Hunter", me.Gamertag);
    }

    [Fact]
    public async Task Maps_title_history_most_recent_first()
    {
        var fake = new FakeHttpHandler().WithXboxAuth()
            .Route("titlehub.xboxlive.com/users/xuid(1)/titles/titlehistory/decoration/achievement,image", HttpStatusCode.OK, Fixtures.Read("Xbox/titlehistory.json"));
        using var xbox = XboxFake.Client(fake);

        var titles = await xbox.GetTitleHistoryAsync("1", Ct);

        Assert.Equal(["Starfield", "Halo Infinite", "Halo 3"], titles.Select(t => t.Name));
        var halo = titles[1];
        Assert.True(halo.IsCompleted);
        Assert.False(halo.IsXbox360);
        Assert.Equal(["XboxSeries", "XboxOne", "PC"], halo.Devices);
        Assert.Equal(1600, halo.TotalGamerscore);
        Assert.True(titles[2].IsXbox360);
        Assert.Equal(24, titles[2].ProgressPercentage);
        Assert.Null(titles[0].ImageUrl);
        Assert.Equal("2", fake.ApiRequests().Single().Header("x-xbl-contract-version"));
    }

    [Fact]
    public async Task Follows_achievement_pages()
    {
        var fake = new FakeHttpHandler().WithXboxAuth()
            .Route("continuationToken=1", HttpStatusCode.OK, Fixtures.Read("Xbox/achievements-page2.json"))
            .Route("achievements.xboxlive.com/users/xuid(1)/achievements?titleId=1915865634&maxItems=1000", HttpStatusCode.OK, Fixtures.Read("Xbox/achievements-page1.json"));
        using var xbox = XboxFake.Client(fake);

        var achievements = await xbox.GetAchievementsAsync("1", "1915865634", Ct);

        Assert.Equal(2, achievements.Count);
        Assert.Equal(2, fake.ApiRequests().Count());
        var welcome = achievements[0];
        Assert.True(welcome.IsUnlocked);
        Assert.Equal(15, welcome.Gamerscore);
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 18, 42, 10, 537, TimeSpan.Zero), welcome.UnlockedAt);
        Assert.Equal(78.4m, welcome.RarityPercentage);
        Assert.Equal("https://images-eds.xboxlive.com/1.png", welcome.IconUrl);

        var secret = achievements[1];
        Assert.False(secret.IsUnlocked);
        Assert.Null(secret.UnlockedAt);
        Assert.True(secret.IsSecret);
        Assert.Null(secret.Description);
        Assert.Equal("Secret achievement", secret.LockedDescription);
        Assert.Equal("Rare", secret.RarityCategory);
    }

    [Fact]
    public async Task Xbox360_titles_are_rejected_explicitly()
    {
        var fake = new FakeHttpHandler().WithXboxAuth()
            .Route("titlehub", HttpStatusCode.OK, Fixtures.Read("Xbox/titlehistory.json"));
        using var xbox = XboxFake.Client(fake);
        var halo3 = (await xbox.GetTitleHistoryAsync("1", Ct)).Single(t => t.IsXbox360);

        await Assert.ThrowsAsync<NotSupportedException>(() => xbox.GetAchievementsAsync("1", halo3, Ct));
    }

    [Fact]
    public async Task Private_history_throws_access_denied()
    {
        var fake = new FakeHttpHandler().WithXboxAuth()
            .Route("titlehub", HttpStatusCode.Forbidden, """{"code":15,"source":"TitleHub","description":"Privacy settings"}""");
        using var xbox = XboxFake.Client(fake);

        var ex = await Assert.ThrowsAsync<TrophyKitAccessDeniedException>(() => xbox.GetTitleHistoryAsync("1", Ct));

        Assert.Equal("15", ex.ErrorCode);
        Assert.Contains("Privacy settings", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolves_from_dependency_injection()
    {
        var fake = new FakeHttpHandler().WithXboxAuth()
            .Route("profile.xboxlive.com", HttpStatusCode.OK, Fixtures.Read("Xbox/profile.json"));
        var services = new ServiceCollection();
        services.AddTrophyKitXbox(o =>
        {
            o.ClientId = "client-123";
            o.RefreshToken = "seed-refresh";
            o.Http = PsnFake.FastHttp();
        });
        services.AddHttpClient(XboxServiceCollectionExtensions.ApiClientName).ConfigurePrimaryHttpMessageHandler(() => fake);
        services.AddHttpClient(XboxServiceCollectionExtensions.AuthClientName).ConfigurePrimaryHttpMessageHandler(() => fake);
        await using var provider = services.BuildServiceProvider();

        var xbox = provider.GetRequiredService<XboxClient>();
        var profile = await xbox.GetProfileAsync("2533274800000001", Ct);

        Assert.Equal("Some Hunter", profile.Gamertag);
        Assert.Equal("XBL3.0 x=1111111111111111111;xsts-token-1", fake.ApiRequests().Single().Header("Authorization"));
    }
}
