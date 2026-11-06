# TrophyKit.RetroAchievements

RetroAchievements Web API client for .NET.

```csharp
using var ra = new RetroAchievementsClient(new RetroAchievementsClientOptions { ApiKey = "<web api key>" });

await foreach (var game in ra.GetAllCompletionProgressAsync("SomeUser"))
{
    Console.WriteLine($"{game.Title} ({game.ConsoleName}): {game.NumAwardedHardcore}/{game.MaxPossible} {(game.IsMastered ? "★" : "")}");
}
```

Get your Web API key at retroachievements.org → Settings → Keys. Community project, not affiliated with
RetroAchievements.org. See the [main README](https://github.com/dansantxs/TrophyKit#readme).
