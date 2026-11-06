using System.Net;
using System.Text.Json;
using TrophyKit.Auth;
using TrophyKit.Http;
using TrophyKit.PlayStation;
using TrophyKit.PlayStation.Auth;
using TrophyKit.Tests.Fakes;

namespace TrophyKit.Tests.PlayStation;

internal static class PsnFake
{
    public const string AuthorizeUrl = "ca.account.sony.com/api/authz/v3/oauth/authorize";
    public const string TokenUrl = "ca.account.sony.com/api/authz/v3/oauth/token";

    public static FakeHttpHandler WithLogin(this FakeHttpHandler fake, string code = "v3.CODE")
    {
        fake.Route(r => r.RequestUri!.ToString().Contains(AuthorizeUrl, StringComparison.Ordinal), _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri($"com.scee.psxandroid.scecompcall://redirect/?code={code}&cid=abc");
            return response;
        });
        fake.Route(TokenUrl, HttpStatusCode.OK, Fixtures.Read("PlayStation/token.json"), HttpMethod.Post);
        return fake;
    }

    public static PsnClient Client(FakeHttpHandler fake, string? npsso = "npsso-value", ITokenStore? store = null)
        => new(new PsnClientOptions { Npsso = npsso, Http = FastHttp() }, store, fake);

    public static TrophyKitHttpOptions FastHttp() => new()
    {
        MaxRetries = 1,
        RetryBaseDelay = TimeSpan.FromMilliseconds(1),
        RetryMaxDelay = TimeSpan.FromMilliseconds(5),
    };

    public static async Task<InMemoryTokenStore> StoreWith(PsnTokens tokens)
    {
        var store = new InMemoryTokenStore();
        await store.SetAsync("trophykit:psn", JsonSerializer.Serialize(tokens));
        return store;
    }

    public static IEnumerable<RecordedRequest> ApiRequests(this FakeHttpHandler fake)
        => fake.Requests.Where(r => !r.Uri.Host.StartsWith("ca.account", StringComparison.Ordinal));
}
