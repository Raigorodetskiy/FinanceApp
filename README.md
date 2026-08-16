# FinanceApp

Finance portfolio management application with ASP.NET Core 8 Backend and React Frontend.

## Backend

The backend is built with ASP.NET Core 8.

### Requirements
- .NET 8 SDK
- MySQL / MariaDB

### Local Configuration

`appsettings.json` in this repository contains **only empty placeholders** — no real credentials are stored in source control.

Supply real values via environment variables (recommended) or [.NET user secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets):

**Environment variables (recommended for CI/Docker):**
```bash
export ConnectionStrings__DefaultConnection="server=localhost;port=3306;database=financeapp;user=financeapp_user;******"
export Jwt__Key="YOUR_JWT_SIGNING_KEY_AT_LEAST_32_CHARS"
export Finnhub__ApiKey="YOUR_FINNHUB_KEY"
export YahooFinance__MinRequestInterval="00:00:01.500"
export YahooFinance__CooldownDuration="00:30:00"
```

**User secrets (recommended for local development):**
```bash
cd FinanceApp.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "server=localhost;port=3306;database=financeapp;user=financeapp_user;******"
dotnet user-secrets set "Jwt:Key" "YOUR_JWT_SIGNING_KEY_AT_LEAST_32_CHARS"
dotnet user-secrets set "Finnhub:ApiKey" "YOUR_FINNHUB_KEY"
```

> ⚠️ **Credential rotation notice:** Previously committed versions of `appsettings.json` contained a database password, JWT signing key, and Finnhub API key. Those values **must be considered compromised** and rotated at the infrastructure level. Deleting them from the latest commit does **not** remove them from Git history — anyone with repository access can recover them from past commits. Rotate all three credentials outside the repository immediately.

### Run
```bash
cd FinanceApp.API
dotnet run
```

### Account login and migration notes

- Login now accepts one identifier field: **username or email** (case-insensitive, with trim).
- Legacy mobile payload `{ email, password }` is still accepted by `/api/Auth/login`.
- Password hashes are stored with ASP.NET Core `PasswordHasher` (PBKDF2). Existing legacy SHA-256 hashes are upgraded automatically on successful login.
- Username rules (validated on backend): 3-32 chars, only letters/digits/`. _ -`, no `@`, case-insensitive unique.

Before applying migration `AddNormalizedUserAuth` to existing production data, run a preflight check for:
- duplicate usernames ignoring case/whitespace;
- usernames conflicting with any user email ignoring case/whitespace;
- usernames/emails exceeding the new lengths.

If conflicts exist, migration should be stopped and data corrected manually before retry.

### Yahoo Finance throttling and cooldown

FinanceApp routes all Yahoo Finance quote and chart requests through one shared, process-wide coordinator.

| Option | Default | Description |
|---|---|---|
| `YahooFinance:MinRequestInterval` | `00:00:01.500` | Minimum delay between Yahoo request starts across the entire backend process. |
| `YahooFinance:CooldownDuration` | `00:30:00` | Shared fallback cooldown activated for all Yahoo callers after HTTP 429 when `Retry-After` is absent or invalid. |
| `YahooFinance:QuoteCacheDuration` | `00:00:10` | Short-term in-memory cache for successful Yahoo quote responses. Provider timestamps are preserved so cached quotes are not presented as freshly sourced. |
| `YahooFinance:RequestTimeout` | `00:00:10` | Per-request HTTP timeout for Yahoo calls. |

Notes:
- Yahoo concurrency is limited to **1** process-wide.
- Current quotes and historical refreshes share the same throttle, cooldown, and in-flight request coalescing.
- When Yahoo returns HTTP `429`, FinanceApp stops sending new Yahoo requests until the shared cooldown expires.

### DJIA constituents import source (index constituents)

- In this PR, **only DJIA** (`MarketIndex.Code = DJIA`) has real constituent import.
- Runtime source is a **curated versioned snapshot** stored in:
  - `FinanceApp.API/Data/index-constituents/djia.curated.snapshot.json`
- Source attribution:
  - `https://www.spglobal.com/spdji/en/indices/equity/dow-jones-industrial-average/`
- Snapshot metadata includes:
  - `asOfDate` (date of verified snapshot),
  - source URL,
  - curated flag (UI shows it as **"Проверенный снимок"**, not live feed).
- Why curated snapshot:
  - official DJIA owner (S&P Dow Jones Indices) does not provide a free/public stable structured endpoint suitable for production runtime import in this app.
- Explicit non-goal:
  - ETF holdings (e.g. DIA) are **not** used as a silent substitute for DJIA constituents.
- Manual update workflow:
  1. Verify current DJIA list from authoritative source.
  2. Update `djia.curated.snapshot.json` (`asOfDate` + constituents only with confirmed ticker/name/exchange).
  3. Run backend/frontend tests and build.
  4. Open PR describing source and as-of date.
- Imported constituent stocks are created as `CatalogOnly` and do **not** trigger automatic price/history/fundamentals tracking.

---

## Frontend

A React + TypeScript SPA that connects to the backend API.

### Technologies
- React 18, TypeScript, Vite
- Ant Design 5, Recharts, Axios, Day.js

### Configuration

Copy `.env.example` to `.env.local` and adjust as needed:
```bash
cp .env.example .env.local
```

By default the frontend connects to `/api`. Set `VITE_API_BASE_URL` in `.env.local` to override.

### Install
```bash
cd FinanceApp.Frontend
npm install
```

### Run
```bash
npm run dev
```

The app will be available at `http://localhost:3000`

### Build
```bash
npm run build
```

---

## Experimental: finanzen.net Pre-Market Quote Provider

> **⚠️ Experimental — disabled by default. For development research only. Not for production use.**

The `FinanzenNetQuoteService` is an optional, disabled-by-default enrichment provider that attempts to retrieve explicitly labeled pre-market prices from [finanzen.net](https://www.finanzen.net) stock pages. It is not a replacement for the primary Yahoo Finance / Finnhub providers.

**Key characteristics:**
- Disabled by default (`Enabled: false`) — this setting must remain `false` in all tracked configuration files
- Only provides a price when the page **explicitly** labels the value as pre-market ("Vorbörslich" / "Pre-Market")
- Session labels are **never inferred** from clock time or market schedule
- All failures (timeout, changed markup, ambiguous data, HTTP errors) fall back silently to the Yahoo/Finnhub result
- Per-instrument `FinanzenNetSlug` field maps a stock to its finanzen.net page path
- Process-level request throttling and in-memory caching prevent excessive requests

### Legal and Robots.txt Notice

**Before enabling this provider, you must:**

1. Review `https://www.finanzen.net/robots.txt` at the time of use and verify that automated access to `/aktien/` paths is not disallowed for your User-Agent.
2. Review finanzen.net's Terms of Service regarding automated data access.
3. Ensure you are in compliance with all applicable usage restrictions.

> If automated access to the relevant path is disallowed or restricted, keep `Enabled: false` and do not use this feature. The service is designed so it can remain safely disabled indefinitely; the rest of the application is not affected.

**Disable immediately** if:
- You receive 403/429 responses
- finanzen.net's markup changes and parsing breaks
- You are uncertain about compliance

### Configuration

To enable locally (never in tracked `appsettings.json`), use user secrets or `appsettings.Development.json`:

```json
{
  "FinanzenNet": {
    "Enabled": true,
    "BaseUrl": "https://www.finanzen.net",
    "CacheDuration": "00:02:00",
    "MinRequestInterval": "00:00:05",
    "RequestTimeout": "00:00:15",
    "UserAgent": "FinanceApp-Dev/1.0 (development research tool)"
  }
}
```

| Option | Default | Description |
|---|---|---|
| `Enabled` | `false` | Must be `true` to activate. Never `true` in production config. |
| `BaseUrl` | `https://www.finanzen.net` | Base URL for the site. |
| `CacheDuration` | `00:05:00` | How long to cache a successfully parsed pre-market quote. |
| `MinRequestInterval` | `00:00:05` | Minimum delay between outgoing HTTP requests (process-level throttle). |
| `RequestTimeout` | `00:00:15` | Per-request HTTP timeout. |
| `UserAgent` | *(default dev string)* | User-Agent header sent with requests. |

### How to Configure a Slug

A finanzen.net instrument slug is the path segment after `/aktien/` in the stock's URL.
For example, `https://www.finanzen.net/aktien/microsoft-aktie` → slug is `microsoft-aktie`.

Add the slug to a stock via the **Stocks** form in the UI (field: **finanzen.net Slug**), or directly in the database. Valid slugs contain only lowercase letters, digits, hyphens, and underscores, and must start with a letter or digit. For example, `https://www.finanzen.net/aktien/western_digital-aktie` → slug is `western_digital-aktie`.

The `finanzenNetSlug` field is nullable. Stocks without a slug are unaffected by this provider.

### API Usage

The `GET /api/stockprice/{symbol}` endpoint accepts an optional `finanzenNetSlug` query parameter:

```
GET /api/stockprice/MSFT?exchange=NYSE&finanzenNetSlug=microsoft-aktie
```

When the provider is enabled and a valid pre-market price is found, the response includes:
- `priceSession: "PRE"` — explicitly labeled pre-market session
- `priceSource: "finanzen.net"` — auditable attribution
- `priceTimestampUtc` — provider timestamp (null if not supplied by the page)

When disabled, or when no pre-market price is found, the response reflects the normal Yahoo/Finnhub quote.

### Rate and Caching Behavior

- Successful pre-market results are cached in-memory for `CacheDuration` (default: 5 minutes).
- A process-level semaphore enforces `MinRequestInterval` (default: 5 seconds) between HTTP requests, preventing request bursts when the UI refreshes multiple stocks.
- Cache keys are per-slug.

### Markup Stability Caveat

finanzen.net's page structure may change without notice. If the parser cannot find an **unambiguous, explicitly labeled** pre-market section, it returns failure and the normal provider is used. This is intentional. Monitor logs for `FinanzenNet request failed` or `No explicitly labeled pre-market price found` messages.
