using System.Net;
using System.Text.Json;
using TrophyKit.Auth;
using TrophyKit.Tests.Fakes;
using TrophyKit.Tests.PlayStation;
using TrophyKit.Xbox;
using TrophyKit.Xbox.Auth;

namespace TrophyKit.Tests.Xbox;

internal static class XboxFake
{
    public const string MsTokenUrl = "login.live.com/oauth20_token.srf";
    public const string UserAuthUrl = "user.auth.xboxlive.com/user/authenticate";
    public const string XstsUrl = "xsts.auth.xboxlive.com/xsts/authorize";

    public static FakeHttpHandler WithXboxAuth(this FakeHttpHandler fake) => fake
        .Route(MsTokenUrl, HttpStatusCode.OK, Fixtures.Read("Xbox/ms-token.json"), HttpMethod.Post)
        .Route(UserAuthUrl, HttpStatusCode.OK, Fixtures.Read("Xbox/user-token.json"), HttpMethod.Post)
        .Route(XstsUrl, HttpStatusCode.OK, Fixtures.Read("Xbox/xsts.json"), HttpMethod.Post);

    public static XboxClientOptions Options(string? refreshToken = "seed-refresh") => new()
    {
        ClientId = "client-123",
        RedirectUri = "http://localhost:8080/callback",
        RefreshToken = refreshToken,
        Http = PsnFake.FastHttp(),
    };

    public static XboxClient Client(FakeHttpHandler fake, string? refreshToken = "seed-refresh", ITokenStore? store = null)
        => new(Options(refreshToken), store, fake);

    public static async Task<InMemoryTokenStore> StoreWith(XboxTokens tokens)
    {
        var store = new InMemoryTokenStore();
        await store.SetAsync("trophykit:xbox", JsonSerializer.Serialize(tokens));
        return store;
    }

    public static IEnumerable<RecordedRequest> ApiRequests(this FakeHttpHandler fake)
        => fake.Requests.Where(r => r.Uri.Host is not ("login.live.com" or "user.auth.xboxlive.com" or "xsts.auth.xboxlive.com"));
}
