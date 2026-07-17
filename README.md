# MeroShareBot (.NET 10)

.NET 10 MeroShare Telegram bot — Vertical Slice Architecture, Telegram.Bot 22.x, Cronos scheduler, Serilog.

## Stack

| Layer | Choice |
|---|---|
| Runtime | .NET 10 / ASP.NET Core minimal APIs |
| Telegram | `Telegram.Bot` 22.10.1 (webhook, not long-polling) |
| Persistence | MySQL via `Pomelo.EntityFrameworkCore.MySql` 9.0.0 (EF Core) |
| Scheduling | `Cronos` 0.13.0 (cron parsing) + `TimeProvider` |
| Logging | `Serilog.AspNetCore` 10.0.0 (console + rolling file sinks) |
| Auth tokens | `System.IdentityModel.Tokens.Jwt` 8.2.1 (parses MeroShare's session JWT) |
| Credential encryption | AES-256-GCM, hand-rolled `CryptoService` |
| Container | Multi-stage `Dockerfile`, final image `mcr.microsoft.com/dotnet/aspnet:10.0` |

No test project. `TreatWarningsAsErrors` is on — any compiler warning fails the build.

## Development tools

- **dotnet-ef** — only needed to author a new migration after changing a `Shared/Data/Entities/*` shape: `dotnet tool install --global dotnet-ef`, then `dotnet ef migrations add <Name>`. Not needed to just run the app (migrations apply automatically at startup).
- **A tunnel** (ngrok, Cloudflare Tunnel, etc.) — Telegram delivers updates via webhook only; your local `dotnet run` (port 4040) isn't reachable from Telegram without one, e.g. `ngrok http 4040`. Note ngrok's own local inspector dashboard also defaults to `127.0.0.1:4040` — if both are running, point one of them elsewhere.
- **A MySQL instance** — local, containerized, or hosted; only `ConnectionStrings:Default` is required to point at it.
- **`GET /` / `GET /healthcheck`** — quick manual smoke test of a running instance without touching Telegram at all (see [Endpoints](#endpoints)).

## Run

```powershell
dotnet build
dotnet run
```

Development (`dotnet run`) uses `appsettings.Development.json` (gitignored — holds real credentials and the MySQL connection string), listens on http://localhost:4040, and logs MeroShare API request/response bodies at `Debug` level via `MeroShareLoggingHandler`. Production listens on :8080 and only logs at `Information`.

Logging is split across two sinks: the console (captured by whatever hosts the process — journalctl/docker/etc.) gets everything — app logs, EF Core commands, webhook requests, and the MeroShare traffic below — while `logs/log-{Date}.txt` is filtered to contain *only* the MeroShare API request/response lines, nothing else.

Persistence is MySQL via EF Core (`Pomelo.EntityFrameworkCore.MySql`) — pending migrations are applied automatically at startup (`Database.Migrate()`, with a retry loop in case the database isn't reachable the instant the app starts). No separate migration step is needed to run the app; you only need `dotnet-ef` installed if you're adding a new migration yourself (`dotnet tool install --global dotnet-ef`, then `dotnet ef migrations add <Name>`).

## Architecture

Vertical Slice Architecture — each `Features/<Slice>/` folder owns its Telegram-facing endpoint and its business-logic handler end to end.

```
Telegram ──POST──▶ /telegram/webhook (Program.cs)
                        │ returns 200 immediately, then fire-and-forget
                        ▼
                  BotUpdateHandler (fresh DI scope per update)
                        │
                        ▼
                  FeatureDispatcher ── gates on IsBlocked / IsApplyAllowed / IsAdmin
                        │ routes by command or callback_data prefix
                        ▼
              Features/<Slice>/*Endpoint  (sends messages/keyboards via TelegramSender)
                        │
                        ▼
              Features/<Slice>/*Handler   (business logic)
                        │
                        ▼
              MeroShareApiClient ──▶ https://webbackend.cdsc.com.np/api
                        │
                        ▼
              IMeroShareSessionCache: memory ──▶ DB (LinkedAccounts) ──▶ fresh login

Separately, on a cron schedule:
IpoCheckerService (BackgroundService, Cronos) ──▶ IpoCheckerJob
        ├─▶ DMs chats opted into /notify
        └─▶ AutoApplyScheduler ──▶ confirm-tap buttons (a human tap is always required to actually apply)

Persistence:
AccountStore / UserStore / WatchlistStore (singletons) ──▶ IDbContextFactory<BotDbContext> ──▶ MySQL
```

### Feature breakdown

| Slice (`Features/`) | Commands / triggers | Purpose |
|---|---|---|
| `Help` | `/start`, `/help` | Chat registration, command list |
| `Accounts` | `/login`, `/accounts`, `/switch`, `/removeaccount` | Link/list/switch/unlink MeroShare accounts per chat |
| `Settings` | `/settings` | Inline hub — notify toggle, default account, per-account autoapply |
| `Ipo` | `/ipo`, `/apply` | Browse open IPOs, two-step apply flow |
| `Profile` | `/profile` | Demat profile lookup |
| `Portfolio` | `/portfolio` | Summary card + paginated holdings, search, sort, CSV export |
| `AutoApply` | `/autoapply`, `autoapply_*` callbacks | Confirm-tap scheduled apply (see Scheduler) |
| `Notify` | `/notify` | Per-chat opt-in to the daily IPO-open alert |
| `Watchlist` | `/watch` | Per-chat symbol watchlist (no price alerts yet) |
| `Market` | `/market` | Stub — no NEPSE data source wired up yet |
| `Users` | `/users`, `/manageuser` | Admin: list all registered chats; toggle a chat's admin/apply/block flags |
| `Broadcast` | `/broadcast` | Admin: bulk message to selected chats |
| `Scheduler` | (background) | Daily cron IPO check → notify + autoapply confirm-taps |

### External API — MeroShare backend

All calls go through `Shared/MeroShare/MeroShareApiClient`, base URL `https://webbackend.cdsc.com.np/api/` (`MeroShare:BaseUrl`):

| Method | Path | Used for |
|---|---|---|
| `POST` | `meroShare/auth/` | Login (returns JWT in `Authorization` header) |
| `GET` | `meroShare/capital/` | DP (depository participant) list |
| `GET` | `meroShare/ownDetail/` | Profile (`/profile`) |
| `POST` | `meroShareView/myPortfolio/` | Portfolio holdings (`/portfolio`) |
| `POST` | `meroShare/companyShare/applicableIssue/` | Open IPOs (`/ipo`, `/apply`) |
| `GET` | `meroShare/active/{companyShareId}` | Single issue detail |
| `POST` | `meroShare/applicantForm/active/search/` | Existing applications search |
| `GET` | `meroShare/bank/` | Bank list |
| `GET` | `meroShare/bank/{bankId}` | Bank account list |
| `POST` | `meroShare/applicantForm/share/apply` | Submit share application (`/apply` confirm) |

This is MeroShare's own Angular SPA backend, reverse-engineered from its network traffic — not an official/documented API.

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
`Users` row:

- `IsAdmin` (default `false`) — gates `/users`, `/manageuser`, and `/broadcast`.
- `IsApplyAllowed` (default `false` — **deny by default**) — gates `/apply` and `/autoapply`.
- `IsBlocked` (default `false`) — when `true`, that chat is locked out of every command,
  callback, and free-text interaction with a "you're no longer allowed to use this bot" message.

Any existing admin can toggle all three, per chat, via `/manageuser` (pick a chat → tap a button).
Direct SQL (`UPDATE Users SET IsAdmin=1 WHERE ChatId=...`) is only still needed to bootstrap the
very first admin — there's no in-bot way to grant admin before one already exists.

Webhook registration is fully external/manual now — set it up directly with Telegram, the app no
longer registers one at startup.

Production overrides via environment variables use `__` as the separator, e.g. `MeroShare__BaseUrl=...`, `ConnectionStrings__Default=...`.

## Endpoints

- `POST /telegram/webhook` — Telegram updates (responds 200 immediately, processes asynchronously)
- `GET /` — plain text, confirms the server is up and shows the listening URL
- `GET /healthcheck` — HTML status page (environment, version, DB connectivity, next scheduled IPO check); returns 503 when the database is unreachable

## Docker

Multi-stage `Dockerfile`: `sdk:10.0` builds/publishes, final image is plain `aspnet:10.0` — no browser automation, and `MySqlConnector` is a pure managed driver, so no native client libraries are needed either.

```powershell
docker build -t meroshare-bot .
docker run -p 8080:8080 `
  -e ConnectionStrings__Default="Server=...;Database=...;Uid=...;Pwd=..." `
  -e Telegram__BotToken="..." `
  -e Security__DataEncryptionKey="..." `
  meroshare-bot
```

No fixed port is baked into the image — the entrypoint reads `$PORT` (falls back to `8080`) so the same image works on any host:
`ENTRYPOINT ["/bin/sh", "-c", "exec dotnet MeroShareBot.dll --urls http://+:${PORT:-8080}"]`. All other config overrides use `__`-separated env vars matching the `appsettings.json` keys. The webhook is not self-registered — point Telegram at the container's public URL yourself (see Configuration above).

## Bot commands

All commands are open to any chat by default (no whitelist), subject to the three permission flags
above: `IsBlocked` refuses every command/callback/free-text for that chat; `IsApplyAllowed` (off by
default) gates `/apply` and `/autoapply`; `IsAdmin` gates `/users`, `/manageuser`, and `/broadcast`.

### General

| Command | Notes |
|---|---|
| `/start` | Registers the chat |
| `/help` | Lists available commands |

### Accounts

| Command | Notes |
|---|---|
| `/login` | Wizard: Username → DP → Password → CRN (optional) → PIN (optional) → label; validates credentials against MeroShare before saving. Type "cancel" anytime to abort |
| `/accounts` | List linked accounts for this chat |
| `/switch <n>` | Set account `<n>` as this chat's default |
| `/removeaccount <n>` | Unlink account `<n>` |
| `/settings` | Inline hub — notify toggle, default account, per-account autoapply |

### IPO & portfolio

| Command | Notes |
|---|---|
| `/ipo [n]` | Open IPOs — account-agnostic, any linked account works; apply via inline button |
| `/profile [n]` | Demat profile; shows an account picker if 2+ accounts linked and none specified |
| `/portfolio [n]` | Summary card (totals, change, top 5) with buttons to drill into paginated holdings, search a script, toggle sort, or export CSV; same account resolution as `/profile` |
| `/apply [name]` | Two-step flow: pick account(s) → pick matching IPO → confirm. Optional `name` pre-filters eligible IPOs by name/symbol. Needs `IsApplyAllowed` |
| `/autoapply [n] on <kitta>` / `/autoapply [n] off` | Opts an account into scheduler confirm-tap prompts — never auto-submits, a human tap is always required. Bare `/autoapply [n]` shows current status. Needs `IsApplyAllowed` |
| `/notify [on\|off]` | This chat's daily IPO-open notification; bare `/notify` flips current state |
| `/watch` / `/watch add <symbol>` / `/watch remove <symbol>` | Per-chat symbol watchlist (no price alerts yet) |
| `/market` | Stub — no NEPSE data source wired up yet |

### Admin

| Command | Notes |
|---|---|
| `/users` | List all registered chats. Needs `IsAdmin` |
| `/manageuser` | Pick a registered chat → toggle `IsAdmin`/`IsApplyAllowed`/`IsBlocked` with buttons, tap feedback via toast. Needs `IsAdmin` |
| `/broadcast` | Select recipient chats → compose message → preview → send. Needs `IsAdmin` |
