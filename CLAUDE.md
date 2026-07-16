# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A .NET 10 Telegram bot that automates MeroShare (Nepal's share-registration portal) via **direct HTTP calls to MeroShare's backend REST API** (`https://webbackend.cdsc.com.np/api`): lists open IPOs, applies for them across multiple linked accounts per chat, tracks portfolios/watchlists, and runs a daily scheduled IPO check with opt-in notifications and confirm-tap autoapply. It talks to the plain JSON API MeroShare's Angular SPA itself calls (reverse-engineered via the reference client `projectashik/meroshare-manager`).

## Commands

```powershell
dotnet build
dotnet run
```

There is no test project. `TreatWarningsAsErrors` is on, so any compiler warning fails the build.

Docker: `docker build .` (no browser dependencies — the runtime image is a plain `aspnet:10.0` image).

## Environments

- **Development** (`dotnet run`): listens on http://localhost:4040, uses `appsettings.Development.json` — **gitignored, holds real Telegram bot token; never commit it**. MeroShare API traffic is logged at `Debug` level in dev via `MeroShareLoggingHandler` (request/response bodies).
- **Production**: listens on :8080. Config overrides via env vars with `__` separators (e.g. `MeroShare__BaseUrl=...`). Webhook registration is fully external/manual — the app no longer registers one at startup (`WebhookRegistrationService` was removed).

All options (`TelegramOptions`, `MeroShareOptions`, `SecurityOptions`, `SchedulerOptions` in `Shared/Config/`) are bound and validated with `ValidateOnStart()` in `Program.cs` — invalid config crashes at startup, so new config keys should get DataAnnotations there. `SecurityOptions.DataEncryptionKey` is required and round-trip-tested at startup (AES-256-GCM) since it encrypts every linked account's credentials at rest.

## Architecture

Vertical Slice Architecture. Each feature in `Features/<Slice>/` has:
- **Endpoint** — Telegram-facing: parses the command/callback, sends messages and inline keyboards via `TelegramSender`.
- **Handler** — does the real work, i.e. calls `MeroShareApiClient` (`GetOpenIposHandler`, `ApplyIpoHandler`, `GetProfileHandler`, `GetPortfolioHandler`).

`Shared/` holds cross-cutting infrastructure: `Telegram/` (dispatch, sending), `MeroShare/` (the API client, DTOs, DP catalog, session/error types), `Accounts/` (chat-owned linked-account storage), `Security/` (credential encryption), `Watchlist/`, `Config/`.

### Update flow (the path every Telegram message takes)

1. `POST /telegram/webhook` (Program.cs) returns 200 immediately and hands the `Update` to `BotUpdateHandler`, which processes it **fire-and-forget** in a fresh DI scope (Telegram has a 5-second webhook timeout). If `FeatureDispatcher.DispatchAsync` throws, the handler logs the exception and still replies to the originating chat with a generic "⚠️ Something went wrong" message — no silent failures.
2. `FeatureDispatcher` (Shared/Telegram) is the single router — the bot is open to **any** chat, no whitelist. On every update it upserts the sender into `UserStore` (`data/users.json`, a lightweight registration registry — chat ID, name, Telegram username, registered-at — **not** a credential store; MeroShare credentials stay solely in `AccountStore`/`data/accounts.json`). Admin status lives on `UserRecord.IsAdmin` (default `false`), bootstrapped by hand-editing `data/users.json` once after the admin's chat has messaged the bot — it gates only the `/users` command (lists every registered chat). Callback queries are routed by `callback_data` prefix (`apply_*`, `profile_*`, `portfolio_*`, `autoapply_*`, `settings_*`). Free-text replies are checked against any in-progress wizard (`/login`, `/settings` kitta-amount prompt) before falling back to a random "I don't understand" message.
3. **Adding a command/slice means touching three places**: the new `Features/<Slice>/` classes, DI registrations in `Program.cs` (endpoints/handlers are scoped), and a route in `FeatureDispatcher` (new callback prefixes also go in the dispatcher).

### Chat-owned MeroShare accounts (`/login`, `/accounts`, `/switch`, `/removeaccount`)

Accounts are **linked by the chat itself**, not configured by the operator. `/login` runs a one-field-at-a-time conversational wizard (`Features/Accounts/Login/LoginWizardState`, singleton — same in-memory-session pattern as `PendingApplyStore`) collecting Username → DP → Password → CRN(optional) → PIN(optional), validates the DP against `IMeroShareDpCatalog` and does a real login attempt before persisting, so bad credentials are caught immediately. Linked accounts live in `Shared/Accounts/AccountStore` (singleton, JSON-file-backed at `data/accounts.json`, keyed by chat ID with a list of `LinkedAccount`s each) — `Password`/`Crn`/`Pin` are AES-256-GCM ciphertext via `Shared/Security/CryptoService`, `Username`/`Dp` stay plaintext. A chat can link multiple accounts and pick a default (`/switch <n>`); most commands accept an optional `[n]` index and fall back to the default account via `Shared/Accounts/AccountResolver`. This is also how one chat manages accounts on behalf of people without a Telegram presence of their own (e.g. family members) — just link each one via a separate `/login` under that chat. `AccountStore.AddAccount` enforces a **system-wide** uniqueness constraint on `(Username, Dp)` — the same MeroShare account can never be linked under two different chats.

### State across webhook requests

Each webhook update runs in its own DI scope, so multi-step conversations cannot live in scoped services. Follow the established singleton-`ConcurrentDictionary`-keyed-by-chat-ID pattern for any new multi-step flow: `PendingApplyStore` (`/apply`'s two-step account → IPO selection), `LoginWizardState` (`/login`'s field-by-field wizard), `SettingsKittaPromptState` (`/settings`' kitta-amount free-text prompt).

### MeroShare API client

All MeroShare interaction goes through `Shared/MeroShare/MeroShareApiClient` — a stateless, `HttpClient`-based typed client (base address `https://webbackend.cdsc.com.np/api/`) with **no mutable session field**: every authenticated method takes an explicit `MeroShareSession` (the raw `Authorization` response-header token from login, no `Bearer` prefix) so the single registered instance is safe under concurrent chats/accounts. Every operation does a **fresh login** (no session caching across calls) — sidesteps MeroShare's undocumented token-expiry behavior. `Shared/MeroShare/MeroShareDpCatalog` resolves a DP code/name to the numeric `clientId` login needs, cached with a 12h TTL. Errors surface as `MeroShareApiException` (any non-2xx, carries the parsed `message` field when present) or `MeroShareLoginException` (200 OK but no `Authorization` header — a contract violation distinct from bad credentials). DTOs live in `Shared/MeroShare/MeroShareDtos.cs` with exact `JsonPropertyName` casing from the API — note `ApplyRequest.CompanyShareId`/`BankId` serialize as **strings** despite being numeric IDs everywhere else, and the PIN field is `transactionPIN` (capital PIN).

`IsEligibleIpo.Check` filters which open issues qualify for `/apply` (ordinary shares only) — reads `ApplicableIssue.ShareTypeName`/`ShareGroupName` from the live API, mapped onto `IpoData.Type`/`ShareType`.

### Autoapply — never submits unattended

`/autoapply <n> on <kitta>` opts an account in. The scheduler (`Features/AutoApply/AutoApplyScheduler`, called from `IpoCheckerJob`) only ever **sends a confirm-tap message** (`autoapply_go_*`/`autoapply_skip_*` callback buttons) for each new eligible IPO an autoapply-enabled account hasn't been prompted for or applied to yet — it never calls the real apply API itself. Only `Features/AutoApply/AutoApplyCallbackEndpoint`, reachable exclusively from a genuine Telegram `callback_query` (a human tap), reaches `ApplyIpoHandler`/`MeroShareApiClient.ApplyAsync`. There is no timer/retry/background path that submits an application without an explicit tap.

### Scheduler

`IpoCheckerService` is a `BackgroundService` that parses `Scheduler:IpoCron` with Cronos (UTC), sleeps until the next occurrence via the injected `TimeProvider`, then runs `IpoCheckerJob` in a fresh scope. The job fetches the (account-agnostic) open-issue list using any one linked account anywhere, DMs chats that opted in via `/notify` (`AccountStore.GetNotifyEnabledChatIds`), then hands the issue list to `AutoApplyScheduler`. Notifications are opt-in per chat, not a static admin broadcast.

### `/market` and `/watch`

`/market` is currently a stub (`Features/Market/MarketEndpoint`) — no NEPSE index/quote data source has been picked yet. `/watch` (`Features/Watchlist/`) is pure per-chat symbol-list CRUD (`Shared/Watchlist/WatchlistStore`, `data/watchlists.json`) with no price-based alerting until a market-data source exists.
