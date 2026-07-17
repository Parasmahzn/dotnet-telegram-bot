# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A .NET 10 Telegram bot that automates MeroShare (Nepal's share-registration portal) via **direct HTTP calls to MeroShare's backend REST API** (`https://webbackend.cdsc.com.np/api`): lists open IPOs, applies for them across multiple linked accounts per chat, tracks portfolios/watchlists, and runs a daily scheduled IPO check with opt-in notifications and confirm-tap autoapply. It talks to the plain JSON API MeroShare's Angular SPA itself calls (reverse-engineered directly from the SPA's network traffic). Persistence is MySQL via EF Core (see Persistence below).

## Commands

```powershell
dotnet build
dotnet run
```

There is no test project. `TreatWarningsAsErrors` is on, so any compiler warning fails the build.

Adding/changing a `Shared/Data/Entities/*` shape requires a new migration: `dotnet ef migrations add <Name>` (needs the `dotnet-ef` tool — `dotnet tool install --global dotnet-ef`). Migrations are generated design-time via `Shared/Data/BotDbContextFactory.cs` (an `IDesignTimeDbContextFactory` with a throwaway connection string), so `dotnet ef` never needs a live database or a populated `ConnectionStrings:Default`. Applying migrations to a real database happens automatically at app startup (see Persistence below) — there is no separate "run migrations" command.

Docker: `docker build .` (no browser dependencies — the runtime image is a plain `aspnet:10.0` image; MySqlConnector is a pure managed driver, so no native client libraries are needed either).

## Environments

- **Development** (`dotnet run`): listens on http://localhost:4040, uses `appsettings.Development.json` — **gitignored, holds real Telegram bot token and MySQL connection string; never commit it**. MeroShare API traffic is logged at `Debug` level in dev via `MeroShareLoggingHandler` (request/response bodies) — routed *exclusively* to the `logs/log-{Date}.txt` file sink via a `Filter.ByIncludingOnly` sub-logger keyed on that handler's `SourceContext` (`Program.cs`), with a matching `MinimumLevel.Override` in `appsettings.json` (the global default is `Information`, which would otherwise drop these `Debug` events before they reach any sink). The Console sink is unfiltered and still gets everything — general app logs, EF Core commands, webhook logging — plus these same MeroShare lines, so the file is a clean MeroShare-only transcript while the console/server log remains the full picture.
- **Production**: no fixed port/URL baked into the image — `ASPNETCORE_URLS`/port binding comes from whatever the hosting environment sets, so the same image works on any host. Config overrides via env vars with `__` separators (e.g. `MeroShare__BaseUrl=...`, `ConnectionStrings__Default=...`).

All options (`TelegramOptions`, `MeroShareOptions`, `SecurityOptions`, `SchedulerOptions` in `Shared/Config/`) are bound and validated with `ValidateOnStart()` in `Program.cs` — invalid config crashes at startup, so new config keys should get DataAnnotations there. `SecurityOptions.DataEncryptionKey` is required and round-trip-tested at startup (AES-256-GCM) since it encrypts every linked account's credentials at rest. `ConnectionStrings:Default` is checked the same fail-fast way (empty/missing throws before the host builds) but isn't wrapped in an `IOptions<T>` — it's read directly via `builder.Configuration.GetConnectionString("Default")` since that's the idiomatic ASP.NET Core convention for connection strings.

## Persistence — MySQL via EF Core

`Shared/Data/BotDbContext.cs` is the single `DbContext`, with entities in `Shared/Data/Entities/` (`LinkedAccountEntity`, `ChatSettingsEntity`, `UserEntity`, `WatchlistEntity`) kept **deliberately separate** from the domain records (`LinkedAccount`, `UserRecord`, etc.) used throughout `Features/` — the entities carry persistence concerns (e.g. `ChatId` as a real FK-able column, JSON-serialized share-id sets) that the rest of the app never needs to see. `AccountStore`, `UserStore`, and `WatchlistStore` are still registered as **singletons** (unchanged from before the MySQL migration) but now hold an injected `IDbContextFactory<BotDbContext>` instead of a `ConcurrentDictionary` + JSON file — every public method creates a short-lived `DbContext` via `factory.CreateDbContext()`, does its query/update, and disposes, so the singleton itself is never touched concurrently by a live `DbContext` instance.

At startup, `Program.cs` calls `Database.Migrate()` inside a ~10-attempt retry loop (with a short delay between attempts) before `app.Run()` — a safety net for container/orchestrator races where the app starts slightly before MySQL accepts connections, not a substitute for waiting on the DB being reachable at all. `ConnectionStrings:Default` is required (fail-fast if empty); `Program.cs` uses a **fixed** `MySqlServerVersion` rather than `ServerVersion.AutoDetect(...)`, since auto-detect opens a connection during DI configuration — before the retry loop gets a chance to run.

`AccountStore.AddAccount`'s system-wide `(Username, Dp)` uniqueness (see below) is now a real unique index on `LinkedAccounts`, enforced by catching the resulting `DbUpdateException`/`MySqlException.ErrorCode == MySqlErrorCode.DuplicateKeyEntry` rather than an in-memory scan.

## Architecture

```
Telegram ──▶ POST /telegram/webhook ──▶ BotUpdateHandler (fire-and-forget, fresh DI scope)
                                              ▼
                                     FeatureDispatcher (IsBlocked/IsApplyAllowed/IsAdmin gates, command/callback routing)
                                              ▼
                                  Features/<Slice>/*Endpoint ──▶ *Handler ──▶ MeroShareApiClient ──▶ webbackend.cdsc.com.np/api
                                                                                    ▼
                                                                     IMeroShareSessionCache (memory ▶ DB ▶ fresh login)

IpoCheckerService (Cronos cron, BackgroundService) ──▶ IpoCheckerJob ──▶ /notify DMs + AutoApplyScheduler (confirm-tap only)
```

Vertical Slice Architecture. Each feature in `Features/<Slice>/` has:
- **Endpoint** — Telegram-facing: parses the command/callback, sends messages and inline keyboards via `TelegramSender`.
- **Handler** — does the real work, i.e. calls `MeroShareApiClient` (`GetOpenIposHandler`, `ApplyIpoHandler`, `GetProfileHandler`, `GetPortfolioHandler`).

`Shared/` holds cross-cutting infrastructure: `Telegram/` (dispatch, sending), `MeroShare/` (the API client, DTOs, DP catalog, session/error types), `Accounts/` (chat-owned linked-account storage), `Security/` (credential encryption), `Data/` (EF Core `DbContext` + entities), `Users/` (`UserStore`), `Config/`.

### Update flow (the path every Telegram message takes)

1. `POST /telegram/webhook` (Program.cs) returns 200 immediately and hands the `Update` to `BotUpdateHandler`, which processes it **fire-and-forget** in a fresh DI scope (Telegram has a 5-second webhook timeout). If `FeatureDispatcher.DispatchAsync` throws, the handler logs the exception and still replies to the originating chat with a generic "⚠️ Something went wrong" message — no silent failures.
2. `FeatureDispatcher` (Shared/Telegram) is the single router — the bot is open to **any** chat, no whitelist. On every update it upserts the sender into `UserStore` (MySQL-backed `Users` table, a lightweight registration registry — chat ID, name, Telegram username, registered-at, plus the three permission flags below — **not** a credential store; MeroShare credentials stay solely in `AccountStore`/`LinkedAccounts`). Immediately after registering, the dispatcher checks two per-chat flags on `UserRecord` before routing anything else:
   - **`IsBlocked`** (default `false`) — if `true`, every command/callback/free-text for that chat is short-circuited with `"🚫 You're no longer allowed to use this bot."` before it reaches any handler. The chat's name/username still gets refreshed on each message (so it still shows current info via `/users`) — only command handling is blocked.
   - **`IsApplyAllowed`** (default **`false`** — deny by default, admin opts a chat in) — gates `/apply` and `/autoapply`, including their `apply_*`/`autoapply_*` callback continuations (since that's where actual submission happens), with `"🚫 Applying is currently disabled for your account..."` instead.
   - **`IsAdmin`** (default `false`) gates `/users`, `/manageuser`, and `/broadcast`.

   Any existing admin can toggle all three flags for any registered chat via `/manageuser` (`Features/Users/ManageUserEndpoint.cs` — pick a chat, then per-flag toggle buttons; stateless, re-renders from `UserStore` on every tap, same "fresh render, edited in place" shape as `SettingsEndpoint`). Direct SQL (`UPDATE Users SET IsAdmin=1 WHERE ChatId=...`) is still the only way to bootstrap the very first admin, since granting `IsAdmin` through the bot itself requires already having one.

   Callback queries are routed by `callback_data` prefix (`apply_*`, `profile_*`, `portfolio_*`, `autoapply_*`, `settings_*`, `broadcast_*`, `manageuser_*`). Free-text replies are checked against any in-progress wizard (`/login`, `/settings` kitta-amount prompt, `/broadcast`'s message-body prompt) before falling back to a random "I don't understand" message. `manageuser_*` callbacks are gated on `IsAdmin` a second time at the dispatcher level (like `apply_*`/`autoapply_*` on `IsApplyAllowed`) since the target chat id travels inside the callback data itself, not just the invoking chat.
3. **Adding a command/slice means touching three places**: the new `Features/<Slice>/` classes, DI registrations in `Program.cs` (endpoints/handlers are scoped), and a route in `FeatureDispatcher` (new callback prefixes also go in the dispatcher).

### Chat-owned MeroShare accounts (`/login`, `/accounts`, `/switch`, `/removeaccount`)

Accounts are **linked by the chat itself**, not configured by the operator. `/login` runs a two-phase conversational wizard (`Features/Accounts/Login/LoginWizardState`, singleton — same in-memory-session pattern as `PendingApplyStore`): first collects Username → DP → Password → CRN(optional) → PIN(optional) and validates the DP against `IMeroShareDpCatalog` plus does a real login attempt (via `IMeroShareSessionCache`, see below) before persisting, so bad credentials are caught immediately — then, **only after that validation succeeds**, prompts once more for an optional account label (`WizardSession.AwaitingLabel` distinguishes this second phase from the initial field queue; a failed validation never reaches it). Linked accounts live in `Features/Accounts/AccountStore` (singleton, MySQL-backed via `IDbContextFactory<BotDbContext>`, one `LinkedAccounts` row per account with a `ChatId` column plus a separate `ChatSettings` row per chat for its default-account pointer and notify flag) — `Password`/`Crn`/`Pin` are AES-256-GCM ciphertext via `Shared/Security/CryptoService`, `Username`/`Dp`/`Label` stay plaintext. `Label` defaults to `"Account {n}"` (next available number for that chat) when skipped or blank; `LinkedAccount.DisplayLabel` falls back to `Username` for pre-label rows (empty `Label`) — every place the bot lists/references an account for a human uses `DisplayLabel`, not `Username`, as the primary identifier. A chat can link multiple accounts and pick a default (`/switch <n>`); most commands accept an optional `[n]` index, resolved via `Shared/Accounts/AccountResolver`. With no index, `AccountResolver` only auto-resolves when the chat has exactly one linked account — with 2+ accounts it returns `null`, and each caller decides what that means: `/profile`/`/portfolio` show an account-picker inline keyboard, while `/ipo` (account-agnostic — any linked account can browse open issues) falls back to the first linked account instead. This is also how one chat manages accounts on behalf of people without a Telegram presence of their own (e.g. family members) — just link each one via a separate `/login` under that chat. `AccountStore.AddAccount` enforces a **system-wide** uniqueness constraint on `(Username, Dp)` — the same MeroShare account can never be linked under two different chats (a real unique DB index, see Persistence above).

### State across webhook requests

Each webhook update runs in its own DI scope, so multi-step conversations cannot live in scoped services. Follow the established singleton-`ConcurrentDictionary`-keyed-by-chat-ID pattern for any new multi-step flow: `PendingApplyStore` (`/apply`'s two-step account → IPO selection), `LoginWizardState` (`/login`'s field-by-field wizard), `SettingsKittaPromptState` (`/settings`' kitta-amount free-text prompt), `BroadcastState` (`/broadcast`'s select → type-message → confirm flow).

### MeroShare API client

All MeroShare interaction goes through `Shared/MeroShare/MeroShareApiClient` — a stateless, `HttpClient`-based typed client (base address `https://webbackend.cdsc.com.np/api/`) with **no mutable session field**: every authenticated method takes `MeroShareCredentials` directly (not a session) and resolves a session internally via `IMeroShareSessionCache`, so the single registered instance is safe under concurrent chats/accounts. **Sessions are cached and reused**, not re-logged-in every call — MeroShare's token is a real JWT (confirmed via jwt.io) with an `exp` claim, parsed via `JwtSecurityTokenHandler` in `Shared/MeroShare/MeroShareSessionCache`. Lookup order on every `GetSessionAsync(creds)` call: in-memory (`IMemoryCache`, keyed by `Username`+`Dp`) → DB (`LinkedAccountEntity.SessionToken`/`SessionTokenExpiresAt`, plaintext — a deliberate choice to allow direct Postman testing, unlike `Password`/`Crn`/`Pin`) → fresh login only if both miss or the stored token's expiry has passed. A per-account `SemaphoreSlim` (keyed by `(Username, Dp)`) ensures concurrent requests for the same account share one in-flight login rather than each triggering their own. `MeroShareApiClient`'s `SendAsync<T>`/`ApplyAsync` both retry once on a 401 (invalidate the cached/persisted token via `IMeroShareSessionCache.InvalidateAsync`, log in fresh, retry) — accepted as safe even for the non-idempotent `ApplyAsync` since a 401 almost always means the auth check rejected the request before any business logic ran. `MeroShareSessionCache` is registered as a genuine singleton (named `HttpClient` + `IHttpClientFactory`, **not** `AddHttpClient<T>` which defaults to transient) since its lock dictionary must survive across requests — the same transient-typed-client trap that silently broke `MeroShareDpCatalog`'s cache before that was fixed (see below). `Shared/MeroShare/IMeroShareSessionStore` is the DB-persistence port `AccountStore` implements, keeping `Shared/MeroShare` from depending on account storage directly (same DIP shape as `IMeroShareDpCatalog`).

`Shared/MeroShare/MeroShareDpCatalog` resolves a DP code/name to the numeric `clientId` login needs, cached via `IMemoryCache` with a 30-day TTL (the DP list is near-static reference data, and `IMemoryCache` — unlike hand-rolled instance-field caching — actually survives across requests regardless of the catalog's own DI lifetime).

Errors surface as one of three exception types, each with a different user-facing safety contract:
- `MeroShareApiException` — any non-2xx response; `ApiMessage` carries the parsed `message` field when present (backend-authored, safe to show).
- `MeroShareAccountStatusException` — 200 OK with a token, but the response body flags `dematExpired`/`accountExpired`/`passwordExpired` (MeroShare issued a session for an account that can't actually use it); caught at login time in `MeroShareSessionCache` so callers get the real reason instead of a confusing downstream 401 on the first authenticated call. `Message` is MeroShare's own account-status reason (or a safe fallback) — safe to show directly to end users.
- `MeroShareLoginException` — 200 OK but no `Authorization` header, or an empty one: a contract violation against the expected login shape, not a credentials or account issue. `Message` is internal diagnostic text and **must never** be shown to end users.

### Login error-handling policy

Every call site that logs in (`LoginEndpoint.FinishAsync`, `GetProfileHandler`, `GetPortfolioHandler`, `GetOpenIposHandler`, `ApplyIpoHandler`) follows the same pattern: a single `catch (Exception ex)` that calls `ex.Resolve(logger, operation, genericFallback)` (`Shared/MeroShare/MeroShareErrorMessages.cs`) to get a user-safe string, rather than branching on exception type per call site. `Resolve` is the one place that knows which exceptions are user-safe (`MeroShareApiException.ApiMessage`, `MeroShareAccountStatusException.Message`) versus which must be logged and replaced with the generic fallback (`MeroShareLoginException`, and any other unexpected exception). New code that triggers a MeroShare login (directly via `IMeroShareSessionCache.GetSessionAsync`, or indirectly via any `MeroShareApiClient` method) should follow this same single-catch-plus-`Resolve` shape rather than adding new per-type catch blocks — and any new MeroShare failure mode that isn't safe to show verbatim to a user should get its own exception type (following `MeroShareAccountStatusException`/`MeroShareLoginException`'s XML-doc convention of stating explicitly whether `Message` is user-safe) rather than being bolted onto an existing type with a different safety contract.

DTOs live in `Shared/MeroShare/MeroShareDtos.cs` with exact `JsonPropertyName` casing from the API — note `ApplyRequest.CompanyShareId`/`BankId` serialize as **strings** despite being numeric IDs everywhere else, and the PIN field is `transactionPIN` (capital PIN).

Endpoints called (all relative to `MeroShare:BaseUrl`, `MeroShareApiClient.cs`): `POST meroShare/auth/` (login), `GET meroShare/capital/` (DP list, `MeroShareDpCatalog`), `GET meroShare/ownDetail/` (profile), `POST meroShareView/myPortfolio/` (portfolio), `POST meroShare/companyShare/applicableIssue/` (open IPOs), `GET meroShare/active/{companyShareId}` (issue detail), `POST meroShare/applicantForm/active/search/` (applied-issue search), `GET meroShare/bank/` / `GET meroShare/bank/{bankId}` (bank lookups), `POST meroShare/applicantForm/share/apply` (submit application).

`IsEligibleIpo.Check` filters which open issues qualify for `/apply` (ordinary shares only) — reads `ApplicableIssue.ShareTypeName`/`ShareGroupName` from the live API, mapped onto `IpoData.Type`/`ShareType`.

### Autoapply — never submits unattended

`/autoapply <n> on <kitta>` opts an account in — but only takes effect if the chat's `IsApplyAllowed` flag is on (see Update flow above; off by default). The scheduler (`Features/AutoApply/AutoApplyScheduler`, called from `IpoCheckerJob`) only ever **sends a confirm-tap message** (`autoapply_go_*`/`autoapply_skip_*` callback buttons) for each new eligible IPO an autoapply-enabled account hasn't been prompted for or applied to yet — it never calls the real apply API itself. Only `Features/AutoApply/AutoApplyCallbackEndpoint`, reachable exclusively from a genuine Telegram `callback_query` (a human tap), reaches `ApplyIpoHandler`/`MeroShareApiClient.ApplyAsync`, and `FeatureDispatcher` gates that callback on `IsApplyAllowed` too — so even a stale confirm-tap button from before the flag was turned off can't submit. There is no timer/retry/background path that submits an application without an explicit tap.

### Scheduler

`IpoCheckerService` is a `BackgroundService` that parses `Scheduler:IpoCron` with Cronos (UTC), sleeps until the next occurrence via the injected `TimeProvider`, then runs `IpoCheckerJob` in a fresh scope. The job fetches the (account-agnostic) open-issue list using any one linked account anywhere, DMs chats that opted in via `/notify` (`AccountStore.GetNotifyEnabledChatIds`), then hands the issue list to `AutoApplyScheduler`. Notifications are opt-in per chat, not a static admin broadcast — for one-off ad-hoc messaging to some or all chats, use `/broadcast` instead (below).

### `/broadcast` — admin-only bulk messaging

`Features/Broadcast/` — admin-gated the same way as `/users`. Presents a checkbox-style multi-select inline keyboard over every registered chat (`BroadcastState`, singleton, same `PendingXStore` shape as `PendingApplyStore`), toggled in place (`TelegramSender.EditKeyboardAsync`) until the admin taps Done, then prompts for a free-text message body, then a preview + recipient count with Send/Cancel, then sends best-effort to every selected chat (one failure never blocks the rest) and reports a `Sent to X/Y` summary. Cancel works from any step. This is the only place in the codebase that sends an arbitrary admin-authored message to multiple chats at once — deliberately separate from the opt-in `/notify` IPO alerts.

### `/market` and `/watch`

`/market` is currently a stub (`Features/Market/MarketEndpoint`) — no NEPSE index/quote data source has been picked yet. `/watch` (`Features/Watchlist/WatchlistStore`, MySQL-backed `WatchlistItems` table, composite `(ChatId, Symbol)` key) is pure per-chat symbol-list CRUD with no price-based alerting until a market-data source exists.
