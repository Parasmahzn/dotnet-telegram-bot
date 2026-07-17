# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A .NET 10 Telegram bot that automates MeroShare (Nepal's share-registration portal) via **direct HTTP calls to MeroShare's backend REST API** (`https://webbackend.cdsc.com.np/api`): lists open IPOs, applies for them across multiple linked accounts per chat, tracks portfolios/watchlists, and runs a daily scheduled IPO check with opt-in notifications and confirm-tap autoapply. It talks to the plain JSON API MeroShare's Angular SPA itself calls (reverse-engineered via the reference client `projectashik/meroshare-manager`). Persistence is MySQL via EF Core (see Persistence below).

## Commands

```powershell
dotnet build
dotnet run
```

There is no test project. `TreatWarningsAsErrors` is on, so any compiler warning fails the build.

Adding/changing a `Shared/Data/Entities/*` shape requires a new migration: `dotnet ef migrations add <Name>` (needs the `dotnet-ef` tool — `dotnet tool install --global dotnet-ef`). Migrations are generated design-time via `Shared/Data/BotDbContextFactory.cs` (an `IDesignTimeDbContextFactory` with a throwaway connection string), so `dotnet ef` never needs a live database or a populated `ConnectionStrings:Default`. Applying migrations to a real database happens automatically at app startup (see Persistence below) — there is no separate "run migrations" command.

Docker: `docker build .` (no browser dependencies — the runtime image is a plain `aspnet:10.0` image; MySqlConnector is a pure managed driver, so no native client libraries are needed either).

## Environments

- **Development** (`dotnet run`): listens on http://localhost:4040, uses `appsettings.Development.json` — **gitignored, holds real Telegram bot token and MySQL connection string; never commit it**. MeroShare API traffic is logged at `Debug` level in dev via `MeroShareLoggingHandler` (request/response bodies).
- **Production**: no fixed port/URL baked into the image — `ASPNETCORE_URLS`/port binding comes from whatever the hosting environment sets, so the same image works on any host. Config overrides via env vars with `__` separators (e.g. `MeroShare__BaseUrl=...`, `ConnectionStrings__Default=...`).

All options (`TelegramOptions`, `MeroShareOptions`, `SecurityOptions`, `SchedulerOptions` in `Shared/Config/`) are bound and validated with `ValidateOnStart()` in `Program.cs` — invalid config crashes at startup, so new config keys should get DataAnnotations there. `SecurityOptions.DataEncryptionKey` is required and round-trip-tested at startup (AES-256-GCM) since it encrypts every linked account's credentials at rest. `ConnectionStrings:Default` is checked the same fail-fast way (empty/missing throws before the host builds) but isn't wrapped in an `IOptions<T>` — it's read directly via `builder.Configuration.GetConnectionString("Default")` since that's the idiomatic ASP.NET Core convention for connection strings.

## Persistence — MySQL via EF Core

`Shared/Data/BotDbContext.cs` is the single `DbContext`, with entities in `Shared/Data/Entities/` (`LinkedAccountEntity`, `ChatSettingsEntity`, `UserEntity`, `WatchlistEntity`) kept **deliberately separate** from the domain records (`LinkedAccount`, `UserRecord`, etc.) used throughout `Features/` — the entities carry persistence concerns (e.g. `ChatId` as a real FK-able column, JSON-serialized share-id sets) that the rest of the app never needs to see. `AccountStore`, `UserStore`, and `WatchlistStore` are still registered as **singletons** (unchanged from before the MySQL migration) but now hold an injected `IDbContextFactory<BotDbContext>` instead of a `ConcurrentDictionary` + JSON file — every public method creates a short-lived `DbContext` via `factory.CreateDbContext()`, does its query/update, and disposes, so the singleton itself is never touched concurrently by a live `DbContext` instance.

At startup, `Program.cs` calls `Database.Migrate()` inside a ~10-attempt retry loop (with a short delay between attempts) before `app.Run()` — a safety net for container/orchestrator races where the app starts slightly before MySQL accepts connections, not a substitute for waiting on the DB being reachable at all. `ConnectionStrings:Default` is required (fail-fast if empty); `Program.cs` uses a **fixed** `MySqlServerVersion` rather than `ServerVersion.AutoDetect(...)`, since auto-detect opens a connection during DI configuration — before the retry loop gets a chance to run.

`AccountStore.AddAccount`'s system-wide `(Username, Dp)` uniqueness (see below) is now a real unique index on `LinkedAccounts`, enforced by catching the resulting `DbUpdateException`/`MySqlException.ErrorCode == MySqlErrorCode.DuplicateKeyEntry` rather than an in-memory scan.

## Architecture

Vertical Slice Architecture. Each feature in `Features/<Slice>/` has:
- **Endpoint** — Telegram-facing: parses the command/callback, sends messages and inline keyboards via `TelegramSender`.
- **Handler** — does the real work, i.e. calls `MeroShareApiClient` (`GetOpenIposHandler`, `ApplyIpoHandler`, `GetProfileHandler`, `GetPortfolioHandler`).

`Shared/` holds cross-cutting infrastructure: `Telegram/` (dispatch, sending), `MeroShare/` (the API client, DTOs, DP catalog, session/error types), `Accounts/` (chat-owned linked-account storage), `Security/` (credential encryption), `Data/` (EF Core `DbContext` + entities), `Users/` (`UserStore`), `Config/`.

### Update flow (the path every Telegram message takes)

1. `POST /telegram/webhook` (Program.cs) returns 200 immediately and hands the `Update` to `BotUpdateHandler`, which processes it **fire-and-forget** in a fresh DI scope (Telegram has a 5-second webhook timeout). If `FeatureDispatcher.DispatchAsync` throws, the handler logs the exception and still replies to the originating chat with a generic "⚠️ Something went wrong" message — no silent failures.
2. `FeatureDispatcher` (Shared/Telegram) is the single router — the bot is open to **any** chat, no whitelist. On every update it upserts the sender into `UserStore` (MySQL-backed `Users` table, a lightweight registration registry — chat ID, name, Telegram username, registered-at, plus the three permission flags below — **not** a credential store; MeroShare credentials stay solely in `AccountStore`/`LinkedAccounts`). Immediately after registering, the dispatcher checks two per-chat flags on `UserRecord` before routing anything else:
   - **`IsBlocked`** (default `false`) — if `true`, every command/callback/free-text for that chat is short-circuited with `"🚫 You're no longer allowed to use this bot."` before it reaches any handler. The chat's name/username still gets refreshed on each message (so it still shows current info via `/users`) — only command handling is blocked.
   - **`IsApplyAllowed`** (default **`false`** — deny by default, admin opts a chat in) — gates `/apply` and `/autoapply`, including their `apply_*`/`autoapply_*` callback continuations (since that's where actual submission happens), with `"🚫 Applying is currently disabled for your account..."` instead.
   - **`IsAdmin`** (default `false`) gates only `/users` and `/broadcast`.

   All three flags are toggled the same way: **direct SQL** against the `Users` table (`UPDATE Users SET IsAdmin=1 WHERE ChatId=...`, etc.) — there is no admin command or UI for flipping them, by design, matching how `IsAdmin` has always been bootstrapped.

   Callback queries are routed by `callback_data` prefix (`apply_*`, `profile_*`, `portfolio_*`, `autoapply_*`, `settings_*`, `broadcast_*`). Free-text replies are checked against any in-progress wizard (`/login`, `/settings` kitta-amount prompt, `/broadcast`'s message-body prompt) before falling back to a random "I don't understand" message.
3. **Adding a command/slice means touching three places**: the new `Features/<Slice>/` classes, DI registrations in `Program.cs` (endpoints/handlers are scoped), and a route in `FeatureDispatcher` (new callback prefixes also go in the dispatcher).

### Chat-owned MeroShare accounts (`/login`, `/accounts`, `/switch`, `/removeaccount`)

Accounts are **linked by the chat itself**, not configured by the operator. `/login` runs a one-field-at-a-time conversational wizard (`Features/Accounts/Login/LoginWizardState`, singleton — same in-memory-session pattern as `PendingApplyStore`) collecting Username → DP → Password → CRN(optional) → PIN(optional), validates the DP against `IMeroShareDpCatalog` and does a real login attempt before persisting, so bad credentials are caught immediately. Linked accounts live in `Features/Accounts/AccountStore` (singleton, MySQL-backed via `IDbContextFactory<BotDbContext>`, one `LinkedAccounts` row per account with a `ChatId` column plus a separate `ChatSettings` row per chat for its default-account pointer and notify flag) — `Password`/`Crn`/`Pin` are AES-256-GCM ciphertext via `Shared/Security/CryptoService`, `Username`/`Dp` stay plaintext. A chat can link multiple accounts and pick a default (`/switch <n>`); most commands accept an optional `[n]` index and fall back to the default account via `Shared/Accounts/AccountResolver`. This is also how one chat manages accounts on behalf of people without a Telegram presence of their own (e.g. family members) — just link each one via a separate `/login` under that chat. `AccountStore.AddAccount` enforces a **system-wide** uniqueness constraint on `(Username, Dp)` — the same MeroShare account can never be linked under two different chats (a real unique DB index, see Persistence above).

### State across webhook requests

Each webhook update runs in its own DI scope, so multi-step conversations cannot live in scoped services. Follow the established singleton-`ConcurrentDictionary`-keyed-by-chat-ID pattern for any new multi-step flow: `PendingApplyStore` (`/apply`'s two-step account → IPO selection), `LoginWizardState` (`/login`'s field-by-field wizard), `SettingsKittaPromptState` (`/settings`' kitta-amount free-text prompt), `BroadcastState` (`/broadcast`'s select → type-message → confirm flow).

### MeroShare API client

All MeroShare interaction goes through `Shared/MeroShare/MeroShareApiClient` — a stateless, `HttpClient`-based typed client (base address `https://webbackend.cdsc.com.np/api/`) with **no mutable session field**: every authenticated method takes an explicit `MeroShareSession` (the raw `Authorization` response-header token from login, no `Bearer` prefix) so the single registered instance is safe under concurrent chats/accounts. Every operation does a **fresh login** (no session caching across calls) — sidesteps MeroShare's undocumented token-expiry behavior. `Shared/MeroShare/MeroShareDpCatalog` resolves a DP code/name to the numeric `clientId` login needs, cached with a 12h TTL. Errors surface as `MeroShareApiException` (any non-2xx, carries the parsed `message` field when present) or `MeroShareLoginException` (200 OK but no `Authorization` header — a contract violation distinct from bad credentials). DTOs live in `Shared/MeroShare/MeroShareDtos.cs` with exact `JsonPropertyName` casing from the API — note `ApplyRequest.CompanyShareId`/`BankId` serialize as **strings** despite being numeric IDs everywhere else, and the PIN field is `transactionPIN` (capital PIN).

`IsEligibleIpo.Check` filters which open issues qualify for `/apply` (ordinary shares only) — reads `ApplicableIssue.ShareTypeName`/`ShareGroupName` from the live API, mapped onto `IpoData.Type`/`ShareType`.

### Autoapply — never submits unattended

`/autoapply <n> on <kitta>` opts an account in — but only takes effect if the chat's `IsApplyAllowed` flag is on (see Update flow above; off by default). The scheduler (`Features/AutoApply/AutoApplyScheduler`, called from `IpoCheckerJob`) only ever **sends a confirm-tap message** (`autoapply_go_*`/`autoapply_skip_*` callback buttons) for each new eligible IPO an autoapply-enabled account hasn't been prompted for or applied to yet — it never calls the real apply API itself. Only `Features/AutoApply/AutoApplyCallbackEndpoint`, reachable exclusively from a genuine Telegram `callback_query` (a human tap), reaches `ApplyIpoHandler`/`MeroShareApiClient.ApplyAsync`, and `FeatureDispatcher` gates that callback on `IsApplyAllowed` too — so even a stale confirm-tap button from before the flag was turned off can't submit. There is no timer/retry/background path that submits an application without an explicit tap.

### Scheduler

`IpoCheckerService` is a `BackgroundService` that parses `Scheduler:IpoCron` with Cronos (UTC), sleeps until the next occurrence via the injected `TimeProvider`, then runs `IpoCheckerJob` in a fresh scope. The job fetches the (account-agnostic) open-issue list using any one linked account anywhere, DMs chats that opted in via `/notify` (`AccountStore.GetNotifyEnabledChatIds`), then hands the issue list to `AutoApplyScheduler`. Notifications are opt-in per chat, not a static admin broadcast — for one-off ad-hoc messaging to some or all chats, use `/broadcast` instead (below).

### `/broadcast` — admin-only bulk messaging

`Features/Broadcast/` — admin-gated the same way as `/users`. Presents a checkbox-style multi-select inline keyboard over every registered chat (`BroadcastState`, singleton, same `PendingXStore` shape as `PendingApplyStore`), toggled in place (`TelegramSender.EditKeyboardAsync`) until the admin taps Done, then prompts for a free-text message body, then a preview + recipient count with Send/Cancel, then sends best-effort to every selected chat (one failure never blocks the rest) and reports a `Sent to X/Y` summary. Cancel works from any step. This is the only place in the codebase that sends an arbitrary admin-authored message to multiple chats at once — deliberately separate from the opt-in `/notify` IPO alerts.

### `/market` and `/watch`

`/market` is currently a stub (`Features/Market/MarketEndpoint`) — no NEPSE index/quote data source has been picked yet. `/watch` (`Features/Watchlist/WatchlistStore`, MySQL-backed `WatchlistItems` table, composite `(ChatId, Symbol)` key) is pure per-chat symbol-list CRUD with no price-based alerting until a market-data source exists.
