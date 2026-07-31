# FinanceApp

Finance portfolio management application with ASP.NET Core 8 Backend and React Frontend.

## Backend

The backend is built with ASP.NET Core 8 and runs at `http://173.249.42.11:5000`.

### Requirements
- .NET 8 SDK
- MySQL / MariaDB

### Run
```bash
cd FinanceApp.API
dotnet run
```

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

By default the frontend connects to `http://173.249.42.11:5000/api`. Set `VITE_API_BASE_URL` in `.env.local` to override.

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

> **⚠️ Experimental — for development use only. Not supported in production.**

### Overview

The `FinanzenNetQuoteService` is an optional, disabled-by-default enrichment provider that attempts to retrieve explicitly labeled pre-market prices from [finanzen.net](https://www.finanzen.net) stock pages. It is not a replacement for the primary Yahoo Finance / Finnhub providers.

**Key characteristics:**
- Disabled by default (`Enabled: false`)
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

In `appsettings.Development.json` (never enable in production `appsettings.json`):

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
| `Enabled` | `false` | Must be `true` to activate. Never `true` in production. |
| `BaseUrl` | `https://www.finanzen.net` | Base URL for the site. |
| `CacheDuration` | `00:05:00` | How long to cache a successfully parsed pre-market quote. |
| `MinRequestInterval` | `00:00:05` | Minimum delay between outgoing HTTP requests (process-level throttle). |
| `RequestTimeout` | `00:00:15` | Per-request HTTP timeout. |
| `UserAgent` | *(default dev string)* | User-Agent header sent with requests. |

### How to Configure a Slug

A finanzen.net instrument slug is the path segment after `/aktien/` in the stock's URL.
For example, `https://www.finanzen.net/aktien/microsoft-aktie` → slug is `microsoft-aktie`.

Add the slug to a stock via the **Stocks** form in the UI (field: **finanzen.net Slug**), or directly in the database. Valid slugs contain only lowercase letters, digits, and hyphens, and must start with a letter or digit.

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

