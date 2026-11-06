using System.Net;
using System.Text.Json;
using TrophyKit.Tests.Fakes;
using TrophyKit.Xbox;
using TrophyKit.Xbox.Auth;

namespace TrophyKit.Tests.Xbox;

public class XboxAuthTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Builds_authorization_url()
    {
        using var xbox = XboxFake.Client(new FakeHttpHandler());

        var url = xbox.GetAuthorizationUrl("state-1").AbsoluteUri;

        Assert.StartsWith("https://login.live.com/oauth20_authorize.srf?", url, StringComparison.Ordinal);
        Assert.Contains("client_id=client-123", url, StringComparison.Ordinal);
        Assert.Contains("scope=Xboxlive.signin%20Xboxlive.offline_access", url, StringComparison.Ordinal);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A8080%2Fcallback", url, StringComparison.Ordinal);
        Assert.Contains("state=state-1", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Code_sign_in_runs_full_chain_and_stores_tokens()
    {
        var fake = new FakeHttpHandler().WithXboxAuth();
        var store = new TrophyKit.Auth.InMemoryTokenStore();
        using var xbox = XboxFake.Client(fake, refreshToken: null, store);

        var tokens = await xbox.SignInWithCodeAsync("M.code", Ct);

        var ms = fake.Requests.Single(r => r.Uri.ToString().Contains(XboxFake.MsTokenUrl, StringComparison.Ordinal));
        Assert.Contains("grant_type=authorization_code", ms.Body, StringComparison.Ordinal);
        Assert.Contains("code=M.code", ms.Body, StringComparison.Ordinal);
        Assert.Contains("client_id=client-123", ms.Body, StringComparison.Ordinal);

        var user = fake.Requests.Single(r => r.Uri.ToString().Contains(XboxFake.UserAuthUrl, StringComparison.Ordinal));
        Assert.Contains("\"RpsTicket\":\"d=ms-access-1\"", user.Body, StringComparison.Ordinal);
        Assert.Equal("1", user.Header("x-xbl-contract-version"));

        var xsts = fake.Requests.Single(r => r.Uri.ToString().Contains(XboxFake.XstsUrl, StringComparison.Ordinal));
        Assert.Contains("\"UserTokens\":[\"user-token-1\"]", xsts.Body, StringComparison.Ordinal);
        Assert.Contains("\"RelyingParty\":\"http://xboxlive.com\"", xsts.Body, StringComparison.Ordinal);

        Assert.Equal("2533274800000001", tokens.Xsts!.Xuid);
        Assert.Equal("Some Hunter", tokens.Xsts.Gamertag);
        Assert.Equal("XBL3.0 x=1111111111111111111;xsts-token-1", tokens.Xsts.AuthorizationHeader);
        Assert.Contains("ms-refresh-2", await store.GetAsync("trophykit:xbox", Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Seed_refresh_token_signs_in_on_first_call()
    {
        var fake = new FakeHttpHandler().WithXboxAuth()
            .Route("profile.xboxlive.com", HttpStatusCode.OK, Fixtures.Read("Xbox/profile.json"));
        using var xbox = XboxFake.Client(fake);

        await xbox.GetProfileAsync("2533274800000001", Ct);

        var ms = fake.Requests.Single(r => r.Uri.ToString().Contains(XboxFake.MsTokenUrl, StringComparison.Ordinal));
        Assert.Contains("grant_type=refresh_token", ms.Body, StringComparison.Ordinal);
        Assert.Contains("refresh_token=seed-refresh", ms.Body, StringComparison.Ordinal);
        var api = fake.ApiRequests().Single();
        Assert.Equal("XBL3.0 x=1111111111111111111;xsts-token-1", api.Header("Authorization"));
        Assert.Equal("3", api.Header("x-xbl-contract-version"));
    }

    [Fact]
    public async Task Valid_stored_xsts_is_reused_without_auth_calls()
    {
        var store = await XboxFake.StoreWith(new XboxTokens(
            new MicrosoftTokens("ms", DateTimeOffset.UtcNow.AddHours(1), "r"),
            new XstsToken("stored-xsts", DateTimeOffset.UtcNow.AddHours(10), "uhs", "1", "gt")));
        var fake = new FakeHttpHandler().Route("profile.xboxlive.com", HttpStatusCode.OK, Fixtures.Read("Xbox/profile.json"));
        using var xbox = XboxFake.Client(fake, refreshToken: null, store);

        await xbox.GetProfileAsync("1", Ct);

        Assert.Equal("XBL3.0 x=uhs;stored-xsts", Assert.Single(fake.Requests).Header("Authorization"));
    }

    [Fact]
    public async Task Expired_tokens_are_refreshed_and_rotated_refresh_token_is_saved()
    {
        var store = await XboxFake.StoreWith(new XboxTokens(
            new MicrosoftTokens("ms-old", DateTimeOffset.UtcNow.AddMinutes(-1), "ms-refresh-1"),
            new XstsToken("xsts-old", DateTimeOffset.UtcNow.AddMinutes(-1), "uhs", "1", "gt")));
        var fake = new FakeHttpHandler().WithXboxAuth()
            .Route("profile.xboxlive.com", HttpStatusCode.OK, Fixtures.Read("Xbox/profile.json"));
        using var xbox = XboxFake.Client(fake, refreshToken: null, store);

        await xbox.GetProfileAsync("1", Ct);

        var ms = fake.Requests.Single(r => r.Uri.ToString().Contains(XboxFake.MsTokenUrl, StringComparison.Ordinal));
        Assert.Contains("refresh_token=ms-refresh-1", ms.Body, StringComparison.Ordinal);
        var saved = JsonSerializer.Deserialize<XboxTokens>((await store.GetAsync("trophykit:xbox", Ct))!)!;
        Assert.Equal("ms-refresh-2", saved.Microsoft.RefreshToken);
        Assert.Equal("xsts-token-1", saved.Xsts!.Token);
    }

    [Fact]
    public async Task Xsts_error_is_explained()
    {
        var fake = new FakeHttpHandler()
            .Route(XboxFake.MsTokenUrl, HttpStatusCode.OK, Fixtures.Read("Xbox/ms-token.json"), HttpMethod.Post)
            .Route(XboxFake.UserAuthUrl, HttpStatusCode.OK, Fixtures.Read("Xbox/user-token.json"), HttpMethod.Post)
            .Route(XboxFake.XstsUrl, HttpStatusCode.Unauthorized, Fixtures.Read("Xbox/xsts-no-account.json"), HttpMethod.Post);
        using var xbox = XboxFake.Client(fake);

        var ex = await Assert.ThrowsAsync<TrophyKitAuthenticationException>(() => xbox.GetProfileAsync("1", Ct));

        Assert.Equal("2148916233", ex.ErrorCode);
        Assert.Contains("no Xbox profile", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoked_refresh_token_requires_new_sign_in()
    {
        var fake = new FakeHttpHandler().Route(XboxFake.MsTokenUrl, HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"The user could not be authenticated."}""", HttpMethod.Post);
        using var xbox = XboxFake.Client(fake);

        var ex = await Assert.ThrowsAsync<TrophyKitAuthenticationException>(() => xbox.GetProfileAsync("1", Ct));

        Assert.Equal("invalid_grant", ex.ErrorCode);
    }

    [Fact]
    public async Task Without_credentials_throws()
    {
        using var xbox = XboxFake.Client(new FakeHttpHandler(), refreshToken: null);

        var ex = await Assert.ThrowsAsync<TrophyKitAuthenticationException>(() => xbox.GetProfileAsync("1", Ct));

        Assert.Contains("GetAuthorizationUrl", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Renews_xsts_after_401()
    {
        var store = await XboxFake.StoreWith(new XboxTokens(
            new MicrosoftTokens("ms", DateTimeOffset.UtcNow.AddHours(1), "r"),
            new XstsToken("revoked-xsts", DateTimeOffset.UtcNow.AddHours(10), "uhs", "1", "gt")));
        var fake = new FakeHttpHandler()
            .Route(r => r.Headers.TryGetValues("Authorization", out var v) && v.Single().EndsWith(";revoked-xsts", StringComparison.Ordinal),
                _ => FakeHttpHandler.Json(HttpStatusCode.Unauthorized, string.Empty))
            .WithXboxAuth()
            .Route("profile.xboxlive.com", HttpStatusCode.OK, Fixtures.Read("Xbox/profile.json"));
        using var xbox = XboxFake.Client(fake, refreshToken: null, store);

        await xbox.GetProfileAsync("1", Ct);

        Assert.Equal(
            ["XBL3.0 x=uhs;revoked-xsts", "XBL3.0 x=1111111111111111111;xsts-token-1"],
            fake.ApiRequests().Select(r => r.Header("Authorization")));
        Assert.DoesNotContain(fake.Requests, r => r.Uri.ToString().Contains(XboxFake.MsTokenUrl, StringComparison.Ordinal));
    }
}
