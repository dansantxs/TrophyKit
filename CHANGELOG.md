# Changelog

All notable changes are listed per package. Versions follow [Semantic Versioning](https://semver.org).

## Unreleased

### TrophyKit.Core 0.1.0
- HTTP pipeline with retries (backoff, jitter, `Retry-After`), sliding-window rate limiting and per-attempt timeout.
- `ITokenStore` with `InMemoryTokenStore` and `FileTokenStore`.
- Typed exceptions.

### TrophyKit.PlayStation 0.1.0
- NPSSO sign-in, automatic token refresh, NPSSO fallback.
- Profiles by online id and account id, trophy summary, trophy titles (paged, all, updated since), trophies.

### TrophyKit.Xbox 0.1.0
- Microsoft OAuth (authorization code or refresh token), Xbox user token and XSTS chain with automatic renewal.
- Profiles by xuid and gamertag, title history, achievements (Xbox One, Series X|S, PC).

### TrophyKit.RetroAchievements 0.1.0
- User profile, completion progress, game progress with achievements, user awards.
