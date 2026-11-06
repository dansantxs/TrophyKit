# TrophyKit.PlayStation

Unofficial PlayStation Network trophy client for .NET.

```csharp
using var psn = new PsnClient(new PsnClientOptions { Npsso = "<your NPSSO>" });

var user = await psn.GetProfileByOnlineIdAsync("some_psn_id");
await foreach (var title in psn.GetAllTrophyTitlesAsync(user.AccountId))
{
    Console.WriteLine($"{title.Name}: {title.Progress}% {(title.HasPlatinum ? "🏆" : "")}");
}
```

Not affiliated with Sony Interactive Entertainment. Uses undocumented endpoints that may change at any time.
See the [main README](https://github.com/dansantxs/TrophyKit#readme) for authentication, rate limits and caveats.
