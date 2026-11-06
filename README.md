# TrophyKit

[![build](https://github.com/dansantxs/TrophyKit/actions/workflows/build.yml/badge.svg)](https://github.com/dansantxs/TrophyKit/actions/workflows/build.yml)

.NET clients for game trophies and achievements: **PlayStation Network**, **Xbox Live** and **RetroAchievements**.
Each platform is a separate NuGet package built on a shared core that handles authentication, token refresh,
rate limiting, retries and typed errors, so your app only deals with the data.

| Package | What it does |
| --- | --- |
| `TrophyKit.PlayStation` | NPSSO sign-in, profiles, trophy level, trophy lists and trophies (PS3, PS4, PS5, Vita) |
| `TrophyKit.Xbox` | Microsoft account sign-in, profiles, title history and achievements (Xbox One, Series X\|S, PC) |
| `TrophyKit.RetroAchievements` | Profiles, completion progress, game progress with achievements, awards |
| `TrophyKit.Core` | Shared HTTP pipeline, token stores and exceptions (installed automatically) |

Targets .NET 8 and .NET 10.

> [!IMPORTANT]
> **Not affiliated with Sony, Microsoft or RetroAchievements.org.** The PlayStation and Xbox packages use
> undocumented endpoints, the same ones the official apps use. They can change or stop working without
> notice, and using them may be against those platforms' terms of service. Use a secondary account for the
> credentials, and don't build a business on them without understanding that risk. RetroAchievements has an
> official public API.

## Install

```bash
dotnet add package TrophyKit.PlayStation
dotnet add package TrophyKit.Xbox
dotnet add package TrophyKit.RetroAchievements
```

## Quick start

### PlayStation

```csharp
using TrophyKit.PlayStation;

using var psn = new PsnClient(new PsnClientOptions { Npsso = "<64-character NPSSO>" });

var user = await psn.GetProfileByOnlineIdAsync("some_psn_id");      // online id → account id
var summary = await psn.GetTrophySummaryAsync(user.AccountId);
Console.WriteLine($"Level {summary.TrophyLevel}, {summary.EarnedTrophies.Platinum} platinums");

await foreach (var title in psn.GetAllTrophyTitlesAsync(user.AccountId))
{
    Console.WriteLine($"{title.Name} [{string.Join("/", title.Platforms)}] {title.Progress}%");

    var trophies = await psn.GetTitleTrophiesAsync(title, user.AccountId);
    // name, icon, grade, earned date, rarity, PS5 progress...
}
```

`PsnClient.Me` (`"me"`) refers to the signed-in account. Other accounts can be read only when their trophy
list is visible to the signed-in account, which normally means public. **You don't need each user's NPSSO**:
one service account reads any public profile from the online id alone.

### Xbox

```csharp
using TrophyKit.Auth;
using TrophyKit.Xbox;

using var xbox = new XboxClient(new XboxClientOptions
{
    ClientId = "<azure app client id>",
    RedirectUri = "http://localhost:8080/callback",
}, new FileTokenStore(".tokens"));

// First run: send the user to this URL; Microsoft redirects back with ?code=...
Console.WriteLine(xbox.GetAuthorizationUrl());
await xbox.SignInWithCodeAsync(code);

var user = await xbox.GetProfileByGamertagAsync("Some Gamertag");
foreach (var title in await xbox.GetTitleHistoryAsync(user.Xuid))
{
    Console.WriteLine($"{title.Name}: {title.CurrentGamerscore}/{title.TotalGamerscore} G");
    if (!title.IsXbox360)
    {
        var achievements = await xbox.GetAchievementsAsync(user.Xuid, title);
    }
}
```

### RetroAchievements

```csharp
using TrophyKit.RetroAchievements;

using var ra = new RetroAchievementsClient(new RetroAchievementsClientOptions { ApiKey = "<web api key>" });

await foreach (var game in ra.GetAllCompletionProgressAsync("SomeUser"))
{
    Console.WriteLine($"{game.Title}: {game.NumAwardedHardcore}/{game.MaxPossible} {(game.IsMastered ? "mastered" : "")}");
}

var details = await ra.GetGameProgressAsync("SomeUser", gameId: 1);
```

A runnable demo is in [`samples/ConsoleDemo`](samples/ConsoleDemo/Program.cs).

## Authentication

### PlayStation: NPSSO

1. Sign in at [playstation.com](https://www.playstation.com) with the account the app will use.
2. In the same browser, open `https://ca.account.sony.com/api/v1/ssocookie`.
3. Copy the `npsso` value (64 characters).

The client exchanges it for an access token (about 1 hour) and a refresh token (about 2 months) and renews them
by itself. When the refresh token expires it uses the NPSSO again, if it is still set and still valid. If not,
calls throw `TrophyKitAuthenticationException` and someone has to provide a new NPSSO through
`psn.SignInAsync(npsso)`.

### Xbox: Azure app registration

Xbox Live requires a Microsoft account sign-in through your own app registration:

1. Go to [portal.azure.com](https://portal.azure.com) → **App registrations** → **New registration**.
2. Under **Supported account types**, choose one that includes **personal Microsoft accounts**.
3. Add a redirect URI:
   - Desktop or console app: platform **Mobile and desktop applications**, e.g. `http://localhost:8080/callback`.
     No secret is needed.
   - Web app: platform **Web**, using your callback URL. Create a client secret and set `ClientSecret`.
4. Copy the **Application (client) id** into `XboxClientOptions.ClientId`.

The client asks for the `Xboxlive.signin Xboxlive.offline_access` scopes and handles the whole chain:
Microsoft token → Xbox user token → XSTS token. The XSTS token is renewed every ~16 hours, and Microsoft's rotated
refresh token is saved after each refresh. For servers and CI, sign in once interactively, take the refresh
token from the token store, and pass it in `XboxClientOptions.RefreshToken`.

XSTS failures such as "no Xbox profile", "child account" or "country not supported" are reported with a readable
message and the `XErr` code in `ErrorCode`.

### RetroAchievements: Web API key

Get it at retroachievements.org → **Settings** → **Keys**, and set `ApiKey` (and optionally `Username`).
The key is sent as a query parameter but never included in exception messages.

## Token storage

Tokens are kept in an `ITokenStore`. The default, `InMemoryTokenStore`, loses them when the process exits.
`FileTokenStore` writes plain-text files, which is fine for local tools. For production, implement `ITokenStore`
on top of your database, and encrypt the values, for example with ASP.NET Core Data Protection:

```csharp
public sealed class ProtectedDbTokenStore(AppDb db, IDataProtectionProvider dp) : ITokenStore
{
    private readonly IDataProtector _protector = dp.CreateProtector("TrophyKit.Tokens");

    public async ValueTask<string?> GetAsync(string key, CancellationToken ct = default)
        => (await db.Tokens.FindAsync([key], ct)) is { } row ? _protector.Unprotect(row.Value) : null;

    public async ValueTask SetAsync(string key, string value, CancellationToken ct = default) { /* upsert _protector.Protect(value) */ }

    public async ValueTask RemoveAsync(string key, CancellationToken ct = default) { /* delete */ }
}
```

To run several accounts against the same store, give each client its own `TokenStoreKey`.

## Dependency injection

```csharp
builder.Services.AddSingleton<ITokenStore, ProtectedDbTokenStore>();   // optional, before or after

builder.Services.AddTrophyKitPlayStation(o => o.Npsso = builder.Configuration["Psn:Npsso"]);
builder.Services.AddTrophyKitXbox(o =>
{
    o.ClientId = builder.Configuration["Xbox:ClientId"]!;
    o.RefreshToken = builder.Configuration["Xbox:RefreshToken"];
});
builder.Services.AddTrophyKitRetroAchievements(o => o.ApiKey = builder.Configuration["RetroAchievements:ApiKey"]!);

// then inject PsnClient, XboxClient or RetroAchievementsClient
```

The clients use `IHttpClientFactory`. Every resolved client shares a single token manager and a single rate
limiter per platform.

## Errors

Every API error is a `TrophyKitException` carrying `Platform`, `StatusCode`, `ErrorCode` (the platform's own
code) and a truncated `ResponseBody`:

| Exception | When |
| --- | --- |
| `TrophyKitAuthenticationException` | Missing, invalid or expired credentials; the user must sign in again |
| `TrophyKitAccessDeniedException` | Private profile or trophy list |
| `TrophyKitNotFoundException` | Unknown user, title or game |
| `TrophyKitRateLimitException` | Platform limit hit and retries exhausted, or the local queue is full (`RetryAfter` when known) |
| `TrophyKitException` | Anything else, including responses in an unexpected format (the API probably changed) |

If the network keeps failing after the retries, you get `HttpRequestException` or `TimeoutException` as usual.

## Rate limits and resilience

Each client runs requests through **retry → rate limiter → per-attempt timeout**:

- **Retries:** 408, 429, 5xx, network errors and timeouts are retried with exponential backoff and jitter
  (3 retries by default), honouring `Retry-After`. If the platform asks to wait longer than `MaxRetryAfter`,
  the client throws `TrophyKitRateLimitException` instead of blocking.
- **Rate limiting:** a local sliding window keeps you under the platform limits instead of hitting 429s. The
  defaults are conservative:

  | Platform | Default |
  | --- | --- |
  | PlayStation | 300 requests / 15 min (community-reported; Sony doesn't document it) |
  | Xbox | 10 requests / 15 s |
  | RetroAchievements | 60 requests / min |

  The limits apply per account. To sync many users, spread the load across several service accounts, each
  with its own client and `TokenStoreKey`.

Everything is configurable through `options.Http` (`TrophyKitHttpOptions`).

## Incremental sync

PlayStation's trophy list is ordered by last update. `GetTrophyTitlesUpdatedSinceAsync(lastSync)` stops at the
first title that hasn't changed, so a periodic sync costs one request when nothing happened:

```csharp
await foreach (var title in psn.GetTrophyTitlesUpdatedSinceAsync(lastSync, accountId))
{
    var trophies = await psn.GetTitleTrophiesAsync(title, accountId);
    // upsert...
}
```

## Limitations

- **Xbox 360** achievements aren't supported yet. `XboxTitle.IsXbox360` marks those titles, and
  `GetAchievementsAsync(xuid, title)` throws `NotSupportedException` for them.
- **PlayStation:** the PS3-era playtime and trophy groups (DLC names) aren't exposed yet. Pass `trophyGroupId`
  to filter by group.
- **Nintendo** has no achievements. **Apple Game Center** has no third-party access.

## Development

```bash
dotnet build
dotnet test tests/TrophyKit.Tests          # unit tests, no network
```

The integration tests in `tests/TrophyKit.IntegrationTests` hit the real APIs. Each test is skipped unless its
environment variables are set: `PSN_NPSSO`; `XBOX_CLIENT_ID` + `XBOX_REFRESH_TOKEN` (+ `XBOX_CLIENT_SECRET`);
`RA_API_KEY` + `RA_USER`. The `contract` workflow runs them every day with the repository secrets, to catch API
changes early.

**Releasing:** each package has its own `<Version>` in its `.csproj`. Bump the packages that changed, update
`CHANGELOG.md`, and push a `v*` tag. The `release` workflow packs everything and pushes to NuGet with
`--skip-duplicate`, so only the packages whose version changed are published. It needs a `NUGET_API_KEY`
repository secret.

## License

[MIT](LICENSE)
