// TrophyKit console demo.
//
//   dotnet run --project samples/ConsoleDemo -- psn <onlineId>
//   dotnet run --project samples/ConsoleDemo -- xbox-login
//   dotnet run --project samples/ConsoleDemo -- xbox <gamertag>
//   dotnet run --project samples/ConsoleDemo -- ra <user>
//
// Credentials come from environment variables (see README): PSN_NPSSO, XBOX_CLIENT_ID, XBOX_REDIRECT_URI,
// RA_API_KEY. Tokens are saved in ~/.trophykit so you only sign in once.

using TrophyKit;
using TrophyKit.Auth;
using TrophyKit.PlayStation;
using TrophyKit.RetroAchievements;
using TrophyKit.Xbox;

var tokenDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".trophykit");
using var store = new FileTokenStore(tokenDir);

if (args.Length == 0)
{
    Console.WriteLine("usage: psn <onlineId> | xbox-login | xbox <gamertag> | ra <user>");
    return 1;
}

try
{
    switch (args[0])
    {
        case "psn":
            await PlayStationAsync(Arg(1, "onlineId"));
            break;
        case "xbox-login":
            await XboxLoginAsync();
            break;
        case "xbox":
            await XboxAsync(Arg(1, "gamertag"));
            break;
        case "ra":
            await RetroAchievementsAsync(Arg(1, "user"));
            break;
        default:
            Console.WriteLine($"unknown command '{args[0]}'");
            return 1;
    }

    return 0;
}
catch (TrophyKitException ex)
{
    Console.Error.WriteLine($"[{ex.Platform}] {ex.GetType().Name}: {ex.Message}");
    return 2;
}
catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or HttpRequestException or TimeoutException)
{
    Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
    return 2;
}

async Task PlayStationAsync(string onlineId)
{
    using var psn = new PsnClient(new PsnClientOptions { Npsso = Environment.GetEnvironmentVariable("PSN_NPSSO") }, store);

    var user = await psn.GetProfileByOnlineIdAsync(onlineId);
    var summary = await psn.GetTrophySummaryAsync(user.AccountId);
    Console.WriteLine($"{user.OnlineId} — level {summary.TrophyLevel}, {summary.EarnedTrophies.Platinum} platinum(s)");

    var count = 0;
    await foreach (var title in psn.GetAllTrophyTitlesAsync(user.AccountId))
    {
        Console.WriteLine($"  {(title.HasPlatinum ? "[P]" : "   ")} {title.Progress,3}%  {title.Name} ({string.Join("/", title.Platforms)})");
        if (++count == 10)
        {
            break;
        }
    }
}

async Task XboxLoginAsync()
{
    using var xbox = NewXbox();
    Console.WriteLine("Open this URL, sign in, then paste the full URL you were redirected to:");
    Console.WriteLine(xbox.GetAuthorizationUrl());
    Console.Write("> ");
    var input = Console.ReadLine()?.Trim() ?? string.Empty;
    var code = input.Contains("code=", StringComparison.Ordinal)
        ? Uri.UnescapeDataString(input[(input.IndexOf("code=", StringComparison.Ordinal) + 5)..].Split('&')[0])
        : input;

    var tokens = await xbox.SignInWithCodeAsync(code);
    Console.WriteLine($"Signed in as {tokens.Xsts?.Gamertag}. Tokens saved in {tokenDir}.");
}

async Task XboxAsync(string gamertag)
{
    using var xbox = NewXbox();
    var user = await xbox.GetProfileByGamertagAsync(gamertag);
    Console.WriteLine($"{user.Gamertag} — {user.Gamerscore} G");

    var titles = await xbox.GetTitleHistoryAsync(user.Xuid);
    foreach (var title in titles.Take(10))
    {
        Console.WriteLine($"  {(title.IsCompleted ? "[100]" : "     ")} {title.CurrentGamerscore,5}/{title.TotalGamerscore,-5} G  {title.Name}");
    }

    var first = titles.FirstOrDefault(t => !t.IsXbox360 && t.TotalAchievements > 0);
    if (first is not null)
    {
        var achievements = await xbox.GetAchievementsAsync(user.Xuid, first);
        Console.WriteLine($"  {first.Name}: {achievements.Count(a => a.IsUnlocked)}/{achievements.Count} achievements unlocked");
    }
}

async Task RetroAchievementsAsync(string user)
{
    using var ra = new RetroAchievementsClient(new RetroAchievementsClientOptions { ApiKey = Env("RA_API_KEY") });
    var profile = await ra.GetUserProfileAsync(user);
    Console.WriteLine($"{profile.User} — {profile.TotalPoints} points ({profile.TotalTruePoints} RetroPoints)");

    var count = 0;
    await foreach (var game in ra.GetAllCompletionProgressAsync(user))
    {
        Console.WriteLine($"  {(game.IsMastered ? "[M]" : "   ")} {game.NumAwardedHardcore,3}/{game.MaxPossible,-3} {game.Title} ({game.ConsoleName})");
        if (++count == 10)
        {
            break;
        }
    }
}

XboxClient NewXbox() => new(new XboxClientOptions
{
    ClientId = Env("XBOX_CLIENT_ID"),
    RedirectUri = Environment.GetEnvironmentVariable("XBOX_REDIRECT_URI") ?? "http://localhost:8080/callback",
    RefreshToken = Environment.GetEnvironmentVariable("XBOX_REFRESH_TOKEN"),
}, store);

string Arg(int index, string name)
    => args.Length > index ? args[index] : throw new ArgumentException($"missing <{name}>");

static string Env(string name)
    => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"environment variable {name} is not set");
