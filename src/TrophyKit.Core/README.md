# TrophyKit.Core

Shared infrastructure for the TrophyKit platform clients. You normally don't reference it directly: install
`TrophyKit.PlayStation`, `TrophyKit.Xbox` or `TrophyKit.RetroAchievements`.

It contains:

- The HTTP pipeline (retry with backoff and `Retry-After`, sliding-window rate limiting, per-attempt timeout).
- `ITokenStore` with `InMemoryTokenStore` and `FileTokenStore`, used to persist tokens between runs.
- Typed exceptions: `TrophyKitAuthenticationException`, `TrophyKitAccessDeniedException`,
  `TrophyKitNotFoundException`, `TrophyKitRateLimitException`.

See the [main README](https://github.com/dansantxs/TrophyKit#readme).
