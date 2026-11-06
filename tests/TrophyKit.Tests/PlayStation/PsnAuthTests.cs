using System.Net;
using TrophyKit.PlayStation;
using TrophyKit.PlayStation.Auth;
using TrophyKit.Tests.Fakes;

namespace TrophyKit.Tests.PlayStation;

public class PsnAuthTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Exchanges_npsso_and_sends_bearer_token()
    {
        var fake = new FakeHttpHandler().WithLogin()
            .Route("trophySummary", HttpStatusCode.OK, Fixtures.Read("PlayStation/trophySummary.json"));
        using var psn = PsnFake.Client(fake);

        await psn.GetTrophySummaryAsync(cancellationToken: Ct);

        var authorize = fake.Requests.Single(r => r.Uri.ToString().Contains(PsnFake.AuthorizeUrl, StringComparison.Ordinal));
        Assert.Equal("npsso=npsso-value", authorize.Header("Cookie"));
        Assert.Contains("client_id=09515159-7237-4370-9b40-3806e67c0891", authorize.Uri.Query, StringComparison.Ordinal);

        var token = fake.Requests.Single(r => r.Uri.ToString().Contains(PsnFake.TokenUrl, StringComparison.Ordinal));
        Assert.Contains("code=v3.CODE", token.Body, StringComparison.Ordinal);
        Assert.Contains("grant_type=authorization_code", token.Body, StringComparison.Ordinal);
        Assert.StartsWith("Basic ", token.Header("Authorization"), StringComparison.Ordinal);

        Assert.Equal("Bearer access-1", fake.ApiRequests().Single().Header("Authorization"));
        Assert.Equal("refresh-1", psn.Tokens.Current!.RefreshToken);
    }

    [Fact]
    public async Task Reuses_token_across_calls()
    {
        var fake = new FakeHttpHandler().WithLogin()
            .Route("trophySummary", HttpStatusCode.OK, Fixtures.Read("PlayStation/trophySummary.json"));
        using var psn = PsnFake.Client(fake);

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => psn.GetTrophySummaryAsync(cancellationToken: Ct)));

        Assert.Single(fake.Requests, r => r.Uri.ToString().Contains(PsnFake.TokenUrl, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_npsso_throws_authentication_exception()
    {
        var fake = new FakeHttpHandler().Route(
            r => r.RequestUri!.ToString().Contains(PsnFake.AuthorizeUrl, StringComparison.Ordinal),
            _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri("com.scee.psxandroid.scecompcall://redirect/?error=login_required&error_code=4165");
                return response;
            });
        using var psn = PsnFake.Client(fake, npsso: "expired");

        var ex = await Assert.ThrowsAsync<TrophyKitAuthenticationException>(() => psn.GetTrophySummaryAsync(cancellationToken: Ct));

        Assert.Equal("login_required", ex.ErrorCode);
        Assert.Contains("NPSSO", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_credentials_throws_authentication_exception()
    {
        using var psn = PsnFake.Client(new FakeHttpHandler(), npsso: null);

        var ex = await Assert.ThrowsAsync<TrophyKitAuthenticationException>(() => psn.GetTrophySummaryAsync(cancellationToken: Ct));

        Assert.Contains("SignInAsync", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refreshes_expired_access_token_from_store()
    {
        var store = await PsnFake.StoreWith(new PsnTokens(
            "old", DateTimeOffset.UtcNow.AddMinutes(-1), "stored-refresh", DateTimeOffset.UtcNow.AddDays(30)));
        var fake = new FakeHttpHandler().WithLogin()
            .Route("trophySummary", HttpStatusCode.OK, Fixtures.Read("PlayStation/trophySummary.json"));
        using var psn = PsnFake.Client(fake, npsso: null, store);

        await psn.GetTrophySummaryAsync(cancellationToken: Ct);

        var token = fake.Requests.Single(r => r.Uri.ToString().Contains(PsnFake.TokenUrl, StringComparison.Ordinal));
        Assert.Contains("grant_type=refresh_token", token.Body, StringComparison.Ordinal);
        Assert.Contains("refresh_token=stored-refresh", token.Body, StringComparison.Ordinal);
        Assert.Contains("refresh-1", await store.GetAsync("trophykit:psn", Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Uses_valid_stored_token_without_login()
    {
        var store = await PsnFake.StoreWith(new PsnTokens(
            "stored", DateTimeOffset.UtcNow.AddHours(1), "r", DateTimeOffset.UtcNow.AddDays(30)));
        var fake = new FakeHttpHandler()
            .Route("trophySummary", HttpStatusCode.OK, Fixtures.Read("PlayStation/trophySummary.json"));
        using var psn = PsnFake.Client(fake, npsso: null, store);

        await psn.GetTrophySummaryAsync(cancellationToken: Ct);

        Assert.Equal("Bearer stored", Assert.Single(fake.Requests).Header("Authorization"));
    }

    [Fact]
    public async Task Falls_back_to_npsso_when_refresh_is_rejected()
    {
        var store = await PsnFake.StoreWith(new PsnTokens(
            "old", DateTimeOffset.UtcNow.AddMinutes(-1), "revoked", DateTimeOffset.UtcNow.AddDays(30)));
        var fake = new FakeHttpHandler()
            .Route(r => r.RequestUri!.ToString().Contains(PsnFake.TokenUrl, StringComparison.Ordinal)
                    && r.Content!.ReadAsStringAsync().Result.Contains("refresh_token=revoked", StringComparison.Ordinal),
                _ => FakeHttpHandler.Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Invalid refresh token"}"""))
            .WithLogin()
            .Route("trophySummary", HttpStatusCode.OK, Fixtures.Read("PlayStation/trophySummary.json"));
        using var psn = PsnFake.Client(fake, npsso: "npsso-value", store);

        await psn.GetTrophySummaryAsync(cancellationToken: Ct);

        Assert.Contains(fake.Requests, r => r.Uri.ToString().Contains(PsnFake.AuthorizeUrl, StringComparison.Ordinal));
        Assert.Equal("Bearer access-1", fake.ApiRequests().Single().Header("Authorization"));
    }

    [Fact]
    public async Task Renews_token_and_retries_once_after_401()
    {
        var store = await PsnFake.StoreWith(new PsnTokens(
            "revoked-access", DateTimeOffset.UtcNow.AddHours(1), "r", DateTimeOffset.UtcNow.AddDays(30)));
        var fake = new FakeHttpHandler()
            .Route(r => r.Headers.Authorization?.Parameter == "revoked-access",
                _ => FakeHttpHandler.Json(HttpStatusCode.Unauthorized, """{"error":{"code":2241801,"message":"Access token required"}}"""))
            .WithLogin()
            .Route("trophySummary", HttpStatusCode.OK, Fixtures.Read("PlayStation/trophySummary.json"));
        using var psn = PsnFake.Client(fake, npsso: null, store);

        var summary = await psn.GetTrophySummaryAsync(cancellationToken: Ct);

        Assert.Equal(431, summary.TrophyLevel);
        Assert.Equal(["Bearer revoked-access", "Bearer access-1"], fake.ApiRequests().Select(r => r.Header("Authorization")));
    }
}
