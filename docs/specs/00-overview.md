# Pacevite — Project Overview

## Project Overview

Pacevite is a fitness event tracking web application for endurance and functional-fitness athletes. Athletes upload race results (CSV, JSON, or GPX) or enter them manually, import activities from Strava, review their event history, inspect personal bests, visualise split performance against their own averages, get a linear-regression finish-time prediction with AI-generated coaching notes, and chat with an AI coach that can call tools against their own event data.

The project is a mixed monorepo: an ASP.NET Core Slim API on .NET 10 sits beside a React 19 / TypeScript / Vite frontend. Both are containerised and served in production behind an Nginx reverse proxy.

## Purpose

| Actor | Need |
|---|---|
| Athlete | Upload race results without manual data entry, or enter a result manually |
| Athlete | Import activities directly from Strava |
| Athlete | Track finish-time progress per event type over time |
| Athlete | Identify personal bests across event types (Finished completions only) |
| Athlete | Understand split-level performance relative to personal average |
| Athlete | Get a predicted finish time with AI-generated coaching notes |
| Athlete | Receive AI coaching insights via a chat interface that can query their own event data |
| Athlete | Stay signed in across sessions without re-entering credentials every 15 minutes |

## Core Use Cases

1. Register a new account with an email address and password.
2. Log in to receive a short-lived JWT plus an httpOnly refresh cookie.
3. Silently refresh the access token when it expires, without re-authenticating.
4. Upload a CSV, JSON, or GPX file containing one or more race results.
5. Manually enter a single race result.
6. Connect a Strava account (OAuth) and import selected activities as events.
7. View the full event history with client-side search and pagination.
8. View personal bests (fastest `Finished` result per event type).
9. Drill into a single event to see split breakdown and comparison to personal averages.
10. View a predicted finish time (linear regression over past results) with AI-generated coaching commentary.
11. Chat with an AI coach that can call tools to look up the athlete's own events and personal bests.
12. Delete an event from the history.
13. Toggle between light and dark UI themes.
14. Log out (revokes the refresh token).

## Technology Stack

| Layer | Technology | Version |
|---|---|---|
| API runtime | .NET / ASP.NET Core Slim (`WebApplication.CreateSlimBuilder`) | .NET 10 |
| ORM | EF Core | 10 |
| Database | PostgreSQL | 17 |
| Auth | ASP.NET Core Identity + JWT Bearer | bundled with .NET 10 |
| Mediator | martinothamar/Mediator (source-generated) | — |
| Validation | FluentValidation | — |
| OpenAPI UI | Scalar | — |
| Frontend runtime | Node / Vite | — |
| UI framework | React | 19 |
| Language | TypeScript | — |
| Routing | React Router | v7 |
| Server state | TanStack Query | v5 |
| Styling | Tailwind CSS | v4 |
| Charts | Recharts | — |
| HTTP client | Axios | — |
| Test runner (.NET) | TUnit + Testcontainers | — |
| Test runner (frontend) | Vitest + React Testing Library | — |
| Mock server | MSW | v2 |
| E2E | Playwright | — |
| Container runtime | Podman (dev) / Docker-compatible compose | — |
| Reverse proxy | Nginx | alpine |
| AI SDK | Anthropic SDK | — |

## High-Level Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                        Client Browser                       │
│                React 19 SPA (Vite / TypeScript)             │
│          TanStack Query │ React Router v7 │ Recharts         │
└──────────────────────────────┬──────────────────────────────┘
                               │ /api/*  (dev: Vite proxy)
                               │ /apis/pacevite/* (prod: Nginx)
┌──────────────────────────────▼──────────────────────────────┐
│                       Nginx Reverse Proxy                   │
│      Strips /apis/pacevite/ prefix, forwards X-Forwarded-*  │
└──────────────────────────────┬──────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────┐
│                    ASP.NET Core Slim API                    │
│  ┌──────────┐  ┌─────────────┐  ┌──────────────────────┐   │
│  │ Features │  │  Pipeline   │  │   Infrastructure     │   │
│  │ (slices) │→ │ Validation  │→ │ Auth / Parsing /     │   │
│  │          │  │ Behavior    │  │ Persistence / Chat / │   │
│  └──────────┘  └─────────────┘  │ OpenApi              │   │
│                                 └──────────────────────┘   │
└──────────────────────────────┬──────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────┐
│                      PostgreSQL 17                          │
│   Events │ EventSplits │ RefreshTokens │ SyncConnections     │
│                  │ AspNetUsers (Identity)                    │
└─────────────────────────────────────────────────────────────┘
```

The API also makes outbound calls to the Anthropic API (AI coaching/chat) and the Strava API (OAuth + activity import), and reads/writes an encrypted `SyncConnections` table via ASP.NET Core Data Protection.

## Major Components

### HTTP API

| Route | Auth | Purpose |
|---|---|---|
| `POST /api/auth/register` | anon (rate-limited) | Create account, return JWT + set refresh cookie |
| `POST /api/auth/login` | anon (rate-limited) | Authenticate, return JWT + set refresh cookie |
| `POST /api/auth/refresh` | anon (rate-limited) | Rotate refresh cookie, return new access JWT |
| `POST /api/auth/logout` | authorized | Revoke refresh token, clear cookie |
| `POST /api/events/upload` | authorized | Parse and persist uploaded CSV/JSON/GPX file |
| `POST /api/events` | authorized | Manually create a single event |
| `GET /api/events` | authorized | List events (filterable by type and date range) |
| `GET /api/events/personal-bests` | authorized | Fastest Finished result per event type |
| `GET /api/events/prediction` | authorized | Linear-regression finish-time prediction |
| `GET /api/events/prediction/coaching` | authorized | AI-generated coaching text for a prediction |
| `GET /api/events/{id}` | authorized | Single event with ordered splits |
| `DELETE /api/events/{id}` | authorized | Remove an event (always returns 204) |
| `GET /api/sync/strava/connect` | authorized | Begin Strava OAuth (returns authorize URL) |
| `GET /api/sync/strava/callback` | anon | Strava OAuth callback (redirects to `/sync?connected=`) |
| `GET /api/sync/strava/activities` | authorized | List importable Strava activities |
| `POST /api/sync/strava/activities/confirm` | authorized | Import a chosen Strava activity as an event |
| `POST /api/chat/message` | authorized | SSE-streamed AI coach chat with tool calls |
| `GET /health` | anon | Liveness/readiness health check (JSON) |

### Non-HTTP Services

| Component | Type | Status |
|---|---|---|
| `ValidationBehavior<TMessage, TResponse>` | Mediator pipeline behavior — runs before every command/query handler | Registered and active |
| `ValidationExceptionHandler` | `IExceptionHandler` — converts `ValidationException` to RFC 7807 `400` | Registered and active |
| `JwtTokenService` | Scoped service generating HMAC-SHA256 JWTs (expiry via `Jwt:AccessTokenExpiryMinutes`, default 15) | Registered and active |
| `RefreshTokenService` | Scoped service issuing/rotating/revoking refresh tokens | Registered and active |
| `CsvEventParser` | Singleton `IEventParser` — parses `text/csv` uploads | Registered and active |
| `JsonEventParser` | Singleton `IEventParser` — parses `application/json` uploads | Registered and active |
| `GpxEventParser` | Singleton `IEventParser` — parses `application/gpx+xml` uploads | Registered and active |
| `IChatToolHandler` / `ChatToolExecutor` | Plugin pattern for Anthropic AI tool calls — 4 handlers wired (`get_events`, `get_personal_bests`, `scrape_race_results`, `fetch_training_tips`) | Registered and active |
| `PredictionCoachingHandler` | Scoped service — generates AI coaching text for a prediction via Anthropic | Registered and active |
| `IStravaClient` / `StravaClient` | `IHttpClientFactory`-backed client for Strava OAuth + activity API | Registered and active |
| Data Protection | Encrypts `SyncConnection` access/refresh tokens at rest | Registered and active |
| EF Core auto-migration | Runs `Database.Migrate()` at startup in `Development` | Development only |
| Rate limiter (`auth` fixed-window) | ASP.NET Core rate limiting middleware (10 req/min prod, 1000 req/min dev) | Active |

### Frontend Feature Areas

| Area | Pages / Components |
|---|---|
| Authentication | `LoginPage`, `RegisterPage`, `AuthContext`, `AuthGuard` |
| Event dashboard | `DashboardPage` — event list with search, client-side pagination, delete |
| Progress analytics | `ProgressChart` (finish-time trend line), `PbPanel` (type-selector + PB bar) |
| Personal bests | `DashboardPage` PB grid cards (sourced from `/events/personal-bests`) |
| Event detail | `EventDetailPage` — split breakdown (`SplitChart`), race comparison (`RaceComparison`) |
| File upload | `UploadPage` — drag-zone, format validation (CSV/JSON/GPX), mutation + cache invalidation |
| Manual entry | `AddEventPage` (`/events/new`) — form-based single-event creation |
| Prediction | `PredictPage` (`/predict`) — `PredictionCard`, `PredictionChart`, `PredictionCoaching`, `PredictionTeaser` |
| External sync | `SyncPage` (`/sync`) — Strava connect flow, importable-activity list |
| AI chat | `ChatWidget` (floating launcher), `ChatPanel`, `ChatMessage` (markdown rendering), `ChatToolStatus` |
| Theme | `ThemeContext`, `ThemeToggle` — light/dark persisted to `localStorage` |

### Shared UI Libraries

| Module | Purpose |
|---|---|
| `lib/api.ts` | Axios client, `tokenStore` (in-memory JWT), request interceptor, silent refresh |
| `lib/chatApi.ts` | SSE stream client for `/api/chat/message` |
| `lib/types.ts` | Shared TypeScript interfaces mirroring API contracts + `formatTime` |
| `lib/chartUtils.ts` | `groupByEventType`, `computePbs`, `computeAverageSplits`, `computeSplitDeltas`, `formatElapsed` |
| `hooks/usePrediction` | TanStack Query hook for the prediction endpoint |
| `hooks/useChatStream` | Hook wrapping the SSE chat stream (deltas, tool-start events, errors) |
| `test/render.tsx` | `renderWithProviders` — wraps components with `QueryClientProvider`, `MemoryRouter`, mocked `AuthContext` |
| `test/handlers.ts` | MSW v2 request handlers for unit tests |

## External Integrations

| Integration | Purpose | Config key |
|---|---|---|
| PostgreSQL 17 | Primary data store | `ConnectionStrings__Default` |
| ASP.NET Identity | User management (password hashing, `UserManager`) | built-in |
| Anthropic API | AI coaching and chat tool calls (active) | `Anthropic:ApiKey`, `Anthropic:Model` (config section; app fails fast at startup if `Anthropic:ApiKey` is absent) |
| Strava API | OAuth2 connect + activity import | `Strava:ClientId`, `Strava:ClientSecret`, `Strava:RedirectUri` |
| Nginx | Reverse proxy in production | `./nginx/prod.conf` (exists on disk; not currently referenced by `docker-compose.yml`) |

## Configuration

### Required Environment Variables (Production)

| Variable | Purpose |
|---|---|
| `DB_USER` / `DB_PASSWORD` | PostgreSQL credentials |
| `JWT_SECRET` | HMAC signing key (minimum 32 characters) |
| `JWT_ISSUER` | JWT `iss` claim value |
| `JWT_AUDIENCE` | JWT `aud` claim value |
| `ANTHROPIC_API_KEY` | Anthropic API key — the app throws at startup if missing |
| `ANTHROPIC_MODEL` | Anthropic model identifier (defaults to `claude-sonnet-4-6` in `appsettings.json`) |
| `STRAVA_CLIENT_ID` / `STRAVA_CLIENT_SECRET` / `STRAVA_REDIRECT_URI` | Strava OAuth app credentials and callback URL |

### Development Overrides

Dev credentials live in `appsettings.Development.json` (committed — dev only). The rate limiter defaults to 1000 req/min in development via `RateLimit:Auth:PermitLimit`.

## Deployment

### Development

```bash
# 1. Start the database
podman compose up -d db

# 2. Start the API (auto-migrates on first run)
dotnet run --project src/Pacevite.Api --launch-profile http

# 3. Start the frontend (Vite dev server with /api proxy to :5291)
cd src/Pacevite.Web && npm run dev
```

### Production (Compose)

`docker-compose.yml` defines two services: `proxy` (Nginx, mounting `./nginx/dev.conf`) and `db` (PostgreSQL 17). There is no `api` or `web` service in this compose file — the API and frontend are run on the host as described under Development. A separate `.devcontainer/docker-compose.devcontainer.yml` exists for the dev container setup. `nginx/prod.conf` exists on disk but is not currently referenced by `docker-compose.yml`.

## Port Map

| Service | URL |
|---|---|
| API (dev) | `http://localhost:5291` |
| Frontend (dev) | `http://localhost:5173` |
| Nginx proxy (prod) | `http://localhost:8080` |
| PostgreSQL | `localhost:5432` |

## Known Limitations and In-Progress Features

| Area | Status / Gap |
|---|---|
| MSW mock casing drift | The upload MSW handler returns `eventType: 'Marathon'` and `source: 'csv'` (mixed/lower case), but `EventMapper` uppercases enum strings, so real API responses return `'MARATHON'` / `'CSV'`. Casing drift remains between mock and API (`src/test/handlers.ts`). |
| Anthropic model ambiguity | `CLAUDE.md` names `claude-haiku-4-5-20251001`; `appsettings.json` defaults `Anthropic:Model` to `claude-sonnet-4-6`. The active model is determined by configuration at runtime. |
| Client-side pagination only | The `GET /api/events` endpoint returns all events for the user. Pagination (page size 10) is applied client-side in `DashboardPage`. |
| No DB-level FK from `Event.UserId` | Enforced at the application level only (queries always filter by the authenticated `UserId`), by design — see `docs/decisions/0006-no-fk-from-event-userid-to-identity.md`. |

## Assumptions

- `Source` is set by the parser/import path that created the event: `"CSV"` / `"JSON"` / `"GPX"` from file uploads, `"STRAVA"` from Strava import, `"MANUAL"` as the default for manually-created events.
- The Anthropic API key and JWT signing secret are both required at startup — the app throws `InvalidOperationException` immediately if either is missing (`Program.cs`), rather than failing lazily on first use.
