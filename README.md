# MeroShareBot (.NET 10)

.NET 10 MeroShare Telegram bot — Vertical Slice Architecture, Telegram.Bot 22.x, Cronos scheduler, Serilog.

## Run

```powershell
dotnet build
dotnet run
```

Development (`dotnet run`) uses `appsettings.Development.json` (gitignored — holds real credentials), listens on http://localhost:4040, and logs MeroShare API request/response bodies at `Debug` level via `MeroShareLoggingHandler`. Production listens on :8080 and only logs at `Information`.

## Configuration

| Key | Purpose |
|---|---|
| `Telegram:BotToken` | Bot token from BotFather |
| `Telegram:ApiUrl` | Telegram Bot API base (default `https://api.telegram.org/bot`) — override for a self-hosted Bot API server/proxy |
| `MeroShare:BaseUrl` | MeroShare backend API base (default `https://webbackend.cdsc.com.np/api`) |
| `MeroShare:DefaultApplyKitta` | Units applied per IPO (default 10) |
| `Security:DataEncryptionKey` | AES-256-GCM key encrypting every linked account's credentials at rest |
| `Scheduler:IpoCron` | Daily IPO check, UTC (default `20 4 * * *` = 10:05 AM NPT) |

The bot is open to any chat — no whitelist. Admin status lives on `UserRecord.IsAdmin` in
`data/users.json` (gates only `/users`), bootstrapped by hand-editing that file once after the
admin's chat has messaged the bot (see `CLAUDE.md`). Webhook registration is fully external/manual
now — set it up directly with Telegram, the app no longer registers one at startup.

Production overrides via environment variables use `__` as the separator, e.g. `MeroShare__BaseUrl=...`.

## Endpoints

- `POST /telegram/webhook` — Telegram updates (responds 200 immediately, processes asynchronously)
- `GET /` — health check

## Bot commands

All commands are open to any chat. `/users` and `/broadcast` are admin-only (see Configuration above).
