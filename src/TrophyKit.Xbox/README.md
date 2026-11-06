# TrophyKit.Xbox

Unofficial Xbox Live achievements client for .NET.

```csharp
using var xbox = new XboxClient(new XboxClientOptions
{
    ClientId = "<azure app client id>",
    RedirectUri = "http://localhost:8080/callback",
}, new FileTokenStore(".tokens"));

// First run only: send the user to xbox.GetAuthorizationUrl(), then
// await xbox.SignInWithCodeAsync(codeFromRedirect);

var user = await xbox.GetProfileByGamertagAsync("Major Nelson");
foreach (var title in await xbox.GetTitleHistoryAsync(user.Xuid))
{
    Console.WriteLine($"{title.Name}: {title.CurrentGamerscore}/{title.TotalGamerscore} G");
}
```

Requires an Azure app registration for personal Microsoft accounts. Not affiliated with Microsoft.
See the [main README](https://github.com/dansantxs/TrophyKit#readme) for the sign-in setup and caveats.
