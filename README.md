# MeroShareBot (.NET 10)

.NET 10 MeroShare Telegram bot — Vertical Slice Architecture, Telegram.Bot 22.x, Cronos scheduler, Serilog.

## Run

```powershell
dotnet build
dotnet run
```

Development (`dotnet run`) uses `appsettings.Development.json` (gitignored — holds real credentials and the MySQL connection string), listens on http://localhost:4040, and logs MeroShare API request/response bodies at `Debug` level via `MeroShareLoggingHandler`. Production listens on :8080 and only logs at `Information`.

Persistence is MySQL via EF Core (`Pomelo.EntityFrameworkCore.MySql`) — pending migrations are applied automatically at startup (`Database.Migrate()`, with a retry loop in case the database isn't reachable the instant the app starts). No separate migration step is needed to run the app; you only need `dotnet-ef` installed if you're adding a new migration yourself (`dotnet tool install --global dotnet-ef`, then `dotnet ef migrations add <Name>`).

## Configuration

| Key | Purpose |
|---|---|
| `ConnectionStrings:Default` | MySQL connection string (required — app fails fast at startup if empty) |
| `Telegram:BotToken` | Bot token from BotFather |
| `Telegram:ApiUrl` | Telegram Bot API base (default `https://api.telegram.org/bot`) — override for a self-hosted Bot API server/proxy |
| `MeroShare:BaseUrl` | MeroShare backend API base (default `https://webbackend.cdsc.com.np/api`) |
| `MeroShare:DefaultApplyKitta` | Units applied per IPO (default 10) |
| `Security:DataEncryptionKey` | AES-256-GCM key encrypting every linked account's credentials at rest |
| `Scheduler:IpoCron` | Daily IPO check, UTC (default `20 4 * * *` = 10:05 AM NPT) |

The bot is open to any chat — no whitelist. Every registered chat has three permission flags on its
`Users` row, all toggled by hand via direct SQL (no admin command for these, by design):

- `IsAdmin` (default `false`) — gates `/users` and `/broadcast`.
- `IsApplyAllowed` (default `false` — **deny by default**) — gates `/apply` and `/autoapply`; an
  admin opts a chat in with `UPDATE Users SET IsApplyAllowed=1 WHERE ChatId=...`.
- `IsBlocked` (default `false`) — when set to `1`, that chat is locked out of every command,
  callback, and free-text interaction with a "you're no longer allowed to use this bot" message.

Webhook registration is fully external/manual now — set it up directly with Telegram, the app no
longer registers one at startup.

Production overrides via environment variables use `__` as the separator, e.g. `MeroShare__BaseUrl=...`, `ConnectionStrings__Default=...`.

## Endpoints

- `POST /telegram/webhook` — Telegram updates (responds 200 immediately, processes asynchronously)
- `GET /` — health check

## Bot commands

All commands are open to any chat by default, subject to the permission flags above: `/users` and
`/broadcast` need `IsAdmin`; `/apply` and `/autoapply` need `IsApplyAllowed`; any command is refused
entirely for a chat with `IsBlocked` set.
