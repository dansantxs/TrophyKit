using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TrophyKit.RetroAchievements;
using TrophyKit.Tests.Fakes;
using TrophyKit.Tests.PlayStation;

namespace TrophyKit.Tests.RetroAchievements;

public class RetroAchievementsClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RetroAchievementsClient Client(FakeHttpHandler fake, string? username = null) => new(
        new RetroAchievementsClientOptions { ApiKey = "secret-key", Username = username, Http = PsnFake.FastHttp() }, fake);

    [Fact]
    public async Task Sends_api_key_and_maps_profile()
    {
        var fake = new FakeHttpHandler().Route("API_GetUserProfile.php", HttpStatusCode.OK, Fixtures.Read("RetroAchievements/profile.json"));
        using var ra = Client(fake, username: "me");

        var profile = await ra.GetUserProfileAsync("SomeUser", Ct);

        var request = Assert.Single(fake.Requests);
        Assert.Equal("https://retroachievements.org/API/API_GetUserProfile.php?y=secret-key&z=me&u=SomeUser", request.Uri.AbsoluteUri);
        Assert.Equal("00003EMFWR7XB8SDPEHB3K56ZQ", profile.Ulid);
        Assert.Equal("https://media.retroachievements.org/UserPic/SomeUser.png", profile.AvatarUrl);
        Assert.Equal(new DateTimeOffset(2013, 4, 7, 19, 5, 50, TimeSpan.Zero), profile.MemberSince);
        Assert.Null(profile.Motto);
        Assert.Equal(9876, profile.TotalTruePoints);
    }

    [Fact]
    public async Task Maps_completion_progress()
    {
        var fake = new FakeHttpHandler().Route("API_GetUserCompletionProgress.php", HttpStatusCode.OK, Fixtures.Read("RetroAchievements/completion.json"));
        using var ra = Client(fake);

        var games = await ra.GetAllCompletionProgressAsync("SomeUser", Ct).CollectAsync(Ct);

        Assert.Equal(2, games.Count);
        Assert.Single(fake.Requests);
        Assert.Contains("&c=500&o=0", fake.Requests[0].Uri.Query, StringComparison.Ordinal);
        Assert.True(games[0].IsMastered);
        Assert.Equal("https://media.retroachievements.org/Images/085573.png", games[0].IconUrl);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 0, 39, 39, TimeSpan.Zero), games[0].HighestAwardAt);
        Assert.Null(games[1].HighestAward);
    }

    [Fact]
    public async Task Maps_game_progress_in_display_order()
    {
        var fake = new FakeHttpHandler().Route("API_GetGameInfoAndUserProgress.php", HttpStatusCode.OK, Fixtures.Read("RetroAchievements/game-progress.json"));
        using var ra = Client(fake);

        var game = await ra.GetGameProgressAsync("SomeUser", 1, Ct);

        Assert.Contains("g=1&u=SomeUser&a=1", fake.Requests[0].Uri.Query, StringComparison.Ordinal);
        Assert.Equal(27080, game.NumDistinctPlayers);
        Assert.Equal([4, 9], game.Achievements.Select(a => a.Id));
        var rings = game.Achievements[0];
        Assert.True(rings.IsEarnedHardcore);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero), rings.EarnedAt);
        Assert.Equal("https://media.retroachievements.org/Badge/250333.png", rings.BadgeUrl);
        Assert.Equal("https://media.retroachievements.org/Badge/250333_lock.png", rings.BadgeLockedUrl);
        Assert.Null(rings.Type);
        var speed = game.Achievements[1];
        Assert.False(speed.IsEarned);
        Assert.Equal("progression", speed.Type);
        Assert.Equal(31, speed.TrueRatio);
    }

    [Fact]
    public async Task Game_without_achievements_has_empty_list()
    {
        var fake = new FakeHttpHandler().Route("API_GetGameInfoAndUserProgress.php", HttpStatusCode.OK, Fixtures.Read("RetroAchievements/game-no-achievements.json"));
        using var ra = Client(fake);

        var game = await ra.GetGameProgressAsync("SomeUser", 99, Ct);

        Assert.Empty(game.Achievements);
    }

    [Fact]
    public async Task Maps_awards()
    {
        var fake = new FakeHttpHandler().Route("API_GetUserAwards.php", HttpStatusCode.OK, Fixtures.Read("RetroAchievements/awards.json"));
        using var ra = Client(fake);

        var awards = await ra.GetUserAwardsAsync("SomeUser", Ct);

        Assert.Equal(1, awards.MasteryAwardsCount);
        Assert.Equal(3, awards.Awards.Count);
        Assert.True(awards.Awards[0].IsHardcore);
        Assert.Equal(1, awards.Awards[0].GameId);
        Assert.False(awards.Awards[2].IsHardcore);
        Assert.Equal("https://media.retroachievements.org/Images/event.png", awards.Awards[2].ImageUrl);
    }

    [Fact]
    public async Task Unknown_user_throws_not_found_without_leaking_key()
    {
        var fake = new FakeHttpHandler().Route("API_GetUserProfile.php", HttpStatusCode.UnprocessableEntity,
            """{"message":"The selected u is invalid.","errors":{"u":["The selected u is invalid."]}}""");
        using var ra = Client(fake);

        var ex = await Assert.ThrowsAsync<TrophyKitNotFoundException>(() => ra.GetUserProfileAsync("nobody", Ct));

        Assert.Equal("u", ex.ErrorCode);
        Assert.Contains("The selected u is invalid.", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_key_throws_authentication_exception()
    {
        var fake = new FakeHttpHandler().Route("API_GetUserProfile.php", HttpStatusCode.Unauthorized, """{"message":"Unauthenticated."}""");
        using var ra = Client(fake);

        var ex = await Assert.ThrowsAsync<TrophyKitAuthenticationException>(() => ra.GetUserProfileAsync("x", Ct));

        Assert.DoesNotContain("secret-key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_key_fails_before_calling_the_api()
    {
        var fake = new FakeHttpHandler();
        using var ra = new RetroAchievementsClient(new RetroAchievementsClientOptions(), fake);

        await Assert.ThrowsAsync<TrophyKitAuthenticationException>(() => ra.GetUserProfileAsync("x", Ct));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Resolves_from_dependency_injection()
    {
        var fake = new FakeHttpHandler().Route("API_GetUserProfile.php", HttpStatusCode.OK, Fixtures.Read("RetroAchievements/profile.json"));
        var services = new ServiceCollection();
        services.AddTrophyKitRetroAchievements(o =>
        {
            o.ApiKey = "secret-key";
            o.Http = PsnFake.FastHttp();
        });
        services.AddHttpClient(RetroAchievementsServiceCollectionExtensions.ClientName).ConfigurePrimaryHttpMessageHandler(() => fake);
        await using var provider = services.BuildServiceProvider();

        var profile = await provider.GetRequiredService<RetroAchievementsClient>().GetUserProfileAsync("SomeUser", Ct);

        Assert.Equal("SomeUser", profile.User);
    }
}
