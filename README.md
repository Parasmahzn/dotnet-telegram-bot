# MeroShareBot (.NET 10)

.NET 10 port of the Node.js MeroShare Telegram bot — Vertical Slice Architecture, Telegram.Bot 22.x, Microsoft.Playwright, Cronos scheduler, Serilog.

## Run

```powershell
dotnet build
# one-time: install the Playwright Chromium browser
powershell -ExecutionPolicy Bypass -File .\bin\Debug\net10.0\playwright.ps1 install chromium
dotnet run
```

Development (`dotnet run`) uses `appsettings.Development.json` (gitignored — holds real credentials), listens on http://localhost:4040, and runs Playwright headed with SlowMo 300ms, leaving the browser open after each automation. Production runs headless and closes the browser.

## Configuration

| Key | Purpose |
|---|---|
| `Telegram:BotToken` | Bot token from BotFather |
| `Telegram:WebhookUrl` | Optional — when set, registered with Telegram at startup; leave empty if the webhook is registered externally |
| `Telegram:AllowedChatIds` | Chat IDs allowed to use protected commands (`/profile`, `/apply`) and inline buttons |
| `MeroShare:Users` | Array of `{ Username, Password, Dp, Crn, Pin }` |
| `MeroShare:DefaultApplyKitta` | Units applied per IPO (default 10) |
| `Scheduler:IpoCron` | Daily IPO check, UTC (default `20 4 * * *` = 10:05 AM NPT) |

Production overrides via environment variables use `__` as the separator, e.g. `MeroShare__Users__0__Username=89303`.

## Endpoints

- `POST /telegram/webhook` — Telegram updates (responds 200 immediately, processes asynchronously)
- `GET /healthz` — health check

## Bot commands

`/start`, `/help`, `/ipo` (public) · `/profile [n|username]`, `/apply [name]` (allowed chats only)
