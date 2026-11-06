using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TrophyKit.PlayStation;
using TrophyKit.Tests.Fakes;

namespace TrophyKit.Tests.PlayStation;

public class PsnClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakeHttpHandler Fake() => new FakeHttpHandler().WithLogin();

    [Fact]
    public async Task Maps_trophy_titles()
    {
        var fake = Fake().Route("trophyTitles", HttpStatusCode.OK, Fixtures.Read("PlayStation/trophyTitles-page1.json"));
        using var psn = PsnFake.Client(fake);

        var page = await psn.GetTrophyTitlesAsync("123", limit: 2, cancellationToken: Ct);

        Assert.Equal(3, page.TotalItemCount);
        Assert.Equal(2, page.NextOffset);
        var astro = page.Titles[0];
        Assert.Equal("NPWR22810_00", astro.NpCommunicationId);
        Assert.Equal(PsnTrophyService.Trophy2, astro.Service);
        Assert.Equal(["PS5"], astro.Platforms);
        Assert.True(astro.HasPlatinum);
        Assert.Equal(46, astro.DefinedTrophies.Total);
        Assert.Null(astro.Detail);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 21, 14, 3, TimeSpan.Zero), astro.LastUpdatedAt);
        Assert.Equal(PsnTrophyService.Trophy, page.Titles[1].Service);
        Assert.False(page.Titles[1].HasPlatinum);

        var request = fake.ApiRequests().Single();
        Assert.Equal("/api/trophy/v1/users/123/trophyTitles", request.Uri.AbsolutePath);
        Assert.Equal("?limit=2&offset=0", request.Uri.Query);
        Assert.Equal("en-US", request.Header("Accept-Language"));
    }

    [Fact]
    public async Task Enumerates_all_pages()
    {
        var fake = Fake()
            .Route("offset=0", HttpStatusCode.OK, Fixtures.Read("PlayStation/trophyTitles-page1.json"))
            .Route("offset=2", HttpStatusCode.OK, Fixtures.Read("PlayStation/trophyTitles-page2.json"));
        using var psn = PsnFake.Client(fake);

        var titles = await psn.GetAllTrophyTitlesAsync(cancellationToken: Ct).CollectAsync(Ct);

        Assert.Equal(["Astro Bot", "Bloodborne™", "Old PS3 Game"], titles.Select(t => t.Name));
        Assert.Equal(["PS3", "PSVITA"], titles[2].Platforms);
        Assert.True(titles[2].IsHidden);
    }

    [Fact]
    public async Task Updated_since_stops_at_older_titles()
    {
        var fake = Fake().Route("trophyTitles", HttpStatusCode.OK, Fixtures.Read("PlayStation/trophyTitles-page1.json"));
        using var psn = PsnFake.Client(fake);

        var titles = await psn.GetTrophyTitlesUpdatedSinceAsync(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), cancellationToken: Ct)
            .CollectAsync(Ct);

        Assert.Equal("Astro Bot", Assert.Single(titles).Name);
        Assert.Single(fake.ApiRequests());
    }

    [Fact]
    public async Task Merges_trophy_definitions_with_user_status()
    {
        var fake = Fake()
            .Route("/users/me/npCommunicationIds/NPWR22810_00/trophyGroups/all/trophies?npServiceName=trophy2", HttpStatusCode.OK, Fixtures.Read("PlayStation/trophies-earned.json"))
            .Route("/npCommunicationIds/NPWR22810_00/trophyGroups/all/trophies?npServiceName=trophy2", HttpStatusCode.OK, Fixtures.Read("PlayStation/trophies-definitions.json"));
        using var psn = PsnFake.Client(fake);

        var trophies = await psn.GetTitleTrophiesAsync(PsnClient.Me, "NPWR22810_00", PsnTrophyService.Trophy2, cancellationToken: Ct);

        Assert.Equal([0, 1, 2], trophies.Select(t => t.Id));
        var platinum = trophies[0];
        Assert.Equal(PsnTrophyType.Platinum, platinum.Type);
        Assert.True(platinum.IsEarned);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 21, 14, 3, TimeSpan.Zero), platinum.EarnedAt);
        Assert.Equal(PsnTrophyRarity.UltraRare, platinum.Rarity);
        Assert.Equal(3.2m, platinum.EarnedRate);

        var progress = trophies[1];
        Assert.False(progress.IsEarned);
        Assert.Null(progress.EarnedAt);
        Assert.Equal(1000, progress.ProgressTarget);
        Assert.Equal(420, progress.Progress);
        Assert.Equal(42, progress.ProgressRate);
        Assert.Equal("Collector", progress.Name);

        Assert.True(trophies[2].IsHidden);
        Assert.Equal("001", trophies[2].GroupId);
    }

    [Fact]
    public async Task Gets_profile_by_online_id()
    {
        var fake = Fake().Route("us-prof.np.community.playstation.net/userProfile/v1/users/some_hunter/profile2", HttpStatusCode.OK, Fixtures.Read("PlayStation/profile-legacy.json"));
        using var psn = PsnFake.Client(fake);

        var profile = await psn.GetProfileByOnlineIdAsync("some_hunter", Ct);

        Assert.Equal("1234567890123456789", profile.AccountId);
        Assert.Equal("some_hunter", profile.OnlineId);
        Assert.True(profile.IsPlus);
        Assert.Equal("https://psn-rsc.prod.dl.playstation.net/avatar_l.png", profile.AvatarUrl);
    }

    [Fact]
    public async Task Gets_profile_by_account_id()
    {
        var fake = Fake().Route("/userProfile/v1/internal/users/123/profiles", HttpStatusCode.OK, Fixtures.Read("PlayStation/profile.json"));
        using var psn = PsnFake.Client(fake);

        var profile = await psn.GetProfileAsync("123", Ct);

        Assert.Equal("123", profile.AccountId);
        Assert.Null(profile.AboutMe);
        Assert.Equal("https://psn-rsc.prod.dl.playstation.net/xl.png", profile.AvatarUrl);
    }

    [Fact]
    public async Task Unknown_user_throws_not_found_with_code()
    {
        var fake = Fake().Route("profile2", HttpStatusCode.NotFound, Fixtures.Read("PlayStation/error-not-found.json"));
        using var psn = PsnFake.Client(fake);

        var ex = await Assert.ThrowsAsync<TrophyKitNotFoundException>(() => psn.GetProfileByOnlineIdAsync("nobody", Ct));

        Assert.Equal("2105356", ex.ErrorCode);
        Assert.Contains("User not found", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Private_trophies_throw_access_denied()
    {
        var fake = Fake().Route("trophyTitles", HttpStatusCode.Forbidden, Fixtures.Read("PlayStation/error-private.json"));
        using var psn = PsnFake.Client(fake);

        var ex = await Assert.ThrowsAsync<TrophyKitAccessDeniedException>(() => psn.GetTrophyTitlesAsync("123", cancellationToken: Ct));

        Assert.Equal("2240526", ex.ErrorCode);
        Assert.Equal("PlayStation", ex.Platform);
    }

    [Fact]
    public async Task Unexpected_payload_reports_format_change()
    {
        var fake = Fake().Route("trophySummary", HttpStatusCode.OK, "[1,2,3]");
        using var psn = PsnFake.Client(fake);

        var ex = await Assert.ThrowsAsync<TrophyKitException>(() => psn.GetTrophySummaryAsync(cancellationToken: Ct));

        Assert.Contains("unexpected format", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolves_from_dependency_injection()
    {
        var fake = Fake().Route("trophySummary", HttpStatusCode.OK, Fixtures.Read("PlayStation/trophySummary.json"));
        var services = new ServiceCollection();
        services.AddTrophyKitPlayStation(o =>
        {
            o.Npsso = "npsso-value";
            o.Http = PsnFake.FastHttp();
        });
        services.AddHttpClient(PsnServiceCollectionExtensions.ApiClientName).ConfigurePrimaryHttpMessageHandler(() => fake);
        services.AddHttpClient(PsnServiceCollectionExtensions.AuthClientName).ConfigurePrimaryHttpMessageHandler(() => fake);
        await using var provider = services.BuildServiceProvider();

        var psn = provider.GetRequiredService<PsnClient>();
        var summary = await psn.GetTrophySummaryAsync(cancellationToken: Ct);

        Assert.Equal(41, summary.EarnedTrophies.Platinum);
        Assert.Same(provider.GetRequiredService<PsnClient>().Tokens, psn.Tokens);
        Assert.Equal("Bearer access-1", fake.ApiRequests().Single().Header("Authorization"));
    }
}
