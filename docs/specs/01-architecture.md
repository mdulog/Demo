# Pacevite — Architecture

## Architectural Style

Pacevite uses **vertical slice architecture** on the backend. Each feature (e.g., `Upload`, `GetEvents`, `GetPersonalBests`, `Register`, `Login`) lives in its own folder under `Features/` and owns its command or query record, handler, validator, and endpoint registration. Nothing crosses feature boundaries except shared contracts in `Contracts/` and domain entities in `Domain/`.

The mediator is **source-generated** via martinothamar/Mediator. Handlers are discovered at compile time; adding a new `ICommand`/`IQuery` implementation requires a build before the source-generated dispatcher recognises it.

The frontend uses a **feature-per-page** approach with shared hooks and a centralised server-state cache (TanStack Query). There is no Redux or Zustand store; all remote data lives in the query cache.

## Backend Layer Breakdown

```
src/Pacevite.Api/
├── Domain/                 # Pure domain types — no DI, no EF references
│   ├── Entities/           # Event, EventSplit, RefreshToken, SyncConnection
│   └── Enums/              # EventType, CompletionStatus, SyncPlatform
│
├── Contracts/              # DTOs crossing the API boundary
│   ├── Requests/           # RegisterRequest, LoginRequest, CreateEventRequest,
│   │                       # ConfirmStravaActivityRequest, SendMessageRequest
│   └── Responses/          # AuthResponse, RefreshResponse, EventResponse,
│                           # PersonalBestResponse, PredictionResponse, SyncResponses
│
├── Features/               # Vertical slices — one folder per feature
│   ├── Auth/
│   │   ├── AuthResult.cs   # Discriminated union (Ok / Fail / FailDuplicate)
│   │   ├── AuthEndpoints.cs
│   │   ├── Register/       # RegisterCommand, RegisterHandler, RegisterValidator
│   │   ├── Login/          # LoginCommand, LoginHandler
│   │   ├── Refresh/        # RefreshCommand, RefreshHandler — rotates the refresh cookie
│   │   └── Logout/         # LogoutCommand, LogoutHandler — revokes the refresh token
│   ├── Events/
│   │   ├── EventEndpoints.cs
│   │   ├── EventMapper.cs  # Entity → response DTO mapping
│   │   ├── Upload/         # UploadEventCommand, UploadEventHandler, UploadEventValidator
│   │   ├── CreateEvent/    # CreateEventCommand, CreateEventHandler — manual single-event entry
│   │   ├── GetEvents/      # GetEventsQuery, GetEventsHandler, GetEventsValidator
│   │   ├── GetEventById/   # GetEventByIdQuery, GetEventByIdHandler
│   │   ├── GetPersonalBests/ # GetPersonalBestsQuery, GetPersonalBestsHandler
│   │   ├── GetPrediction/  # GetPredictionQuery, GetPredictionHandler — linear regression
│   │   ├── PredictionCoaching/ # PredictionCoachingHandler — Anthropic-generated commentary
│   │   └── DeleteEvent/    # DeleteEventCommand, DeleteEventHandler
│   ├── Sync/               # SyncEndpoints — Strava OAuth connect/callback/activities/confirm
│   └── Chat/               # ChatEndpoints — SSE-streamed AI coach chat (POST /api/chat/message)
│
├── Pipeline/               # Cross-cutting Mediator behaviors
│   ├── ValidationBehavior.cs      # Runs FluentValidation before every handler
│   └── ValidationExceptionHandler.cs  # Converts ValidationException → RFC 7807 400
│
└── Infrastructure/         # I/O adapters and external integrations
    ├── Auth/               # IJwtTokenService, JwtTokenService, IRefreshTokenService, RefreshTokenService
    ├── Chat/               # IChatToolHandler, IChatToolExecutor, ChatToolExecutor, SseEvent
    │   └── Tools/          # GetEventsToolHandler, GetPersonalBestsToolHandler,
    │                       # ScrapeRaceResultsToolHandler, FetchTrainingTipsToolHandler
    ├── Regression/         # LinearRegression — the prediction feature's fitting logic
    ├── Sync/                # IStravaClient, StravaClient, SyncTokenProtection
    ├── OpenApi/            # ForwardedPrefixTransformer
    ├── Parsing/            # IEventParser, ParsedEvent, ParsedSplit,
    │                       # CsvEventParser, JsonEventParser, GpxEventParser
    └── Persistence/        # AppDbContext (EF Core + Identity)
```

### Layer Responsibilities

| Layer | Responsibility | May depend on |
|---|---|---|
| Domain | Entity definitions, enum values | Nothing |
| Contracts | Request/response DTOs for the HTTP boundary | Nothing |
| Features | Business logic, DB queries via EF Core, handler orchestration | Domain, Contracts, Infrastructure |
| Pipeline | Cross-cutting validation, exception conversion | FluentValidation, Mediator |
| Infrastructure | I/O implementations (JWT, parsing, persistence, OpenAPI) | Domain |

**Rule:** Features may reference Infrastructure directly (e.g., `AppDbContext`, `IEventParser`). Infrastructure must never reference Features.

## Frontend Layer Breakdown

```
src/Pacevite.Web/src/
├── App.tsx             # Router, QueryClient, ThemeProvider, AuthProvider mount
├── pages/             # One component per route
│   ├── LoginPage.tsx
│   ├── RegisterPage.tsx
│   ├── DashboardPage.tsx
│   ├── UploadPage.tsx
│   ├── AddEventPage.tsx    # Manual single-event entry (/events/new)
│   ├── EventDetailPage.tsx
│   ├── PredictPage.tsx     # Finish-time prediction + AI coaching (/predict)
│   └── SyncPage.tsx        # Strava connect + activity import (/sync)
│
├── components/        # Reusable UI components
│   ├── AuthGuard.tsx       # Redirects unauthenticated users to /login
│   ├── ThemeToggle.tsx     # Light/dark switch button
│   ├── ProgressChart.tsx   # Recharts LineChart — finish-time trend
│   ├── PbPanel.tsx         # PB selector + progress bars
│   ├── SplitChart.tsx      # Recharts BarChart — split vs average delta
│   ├── RaceComparison.tsx  # Recharts spark line + stats vs average
│   ├── prediction/         # PredictionCard, PredictionChart, PredictionCoaching, PredictionTeaser
│   └── chat/               # ChatWidget, ChatPanel, ChatMessage, ChatToolStatus
│
├── context/
│   ├── AuthContext.tsx     # Holds user identity; delegates token to tokenStore
│   └── ThemeContext.tsx    # Theme state + OS preference detection
│
├── hooks/
│   ├── useAuth.ts          # AuthContext accessor
│   ├── useEvents.ts        # TanStack Query over GET /api/events
│   ├── useEvent.ts         # TanStack Query over GET /api/events/{id}
│   ├── usePrediction.ts    # TanStack Query over GET /api/events/prediction
│   └── useChatStream.ts    # Wraps the SSE stream from POST /api/chat/message
│
└── lib/
    ├── api.ts              # Axios client, tokenStore, Bearer interceptor, 401 refresh-and-retry
    ├── chatApi.ts           # SSE stream client for POST /api/chat/message
    ├── types.ts            # TypeScript interfaces matching API contracts
    └── chartUtils.ts       # Pure functions: groupByEventType, computePbs,
                            # computeAverageSplits, computeSplitDeltas, formatElapsed
```

## Dependency Flow

```
HTTP request
  └─→ Minimal API endpoint (EventEndpoints / AuthEndpoints)
        └─→ IMediator.Send(command/query)
              └─→ ValidationBehavior<TMessage, TResponse>
                    └─→ IValidator<TMessage>.Validate()   ← FluentValidation
                          [throws ValidationException if invalid]
                    └─→ Handler.Handle()
                          ├─→ AppDbContext (EF Core → PostgreSQL)
                          └─→ IEventParser / IJwtTokenService (Infrastructure)
```

Validation failures surface as `ValidationException` which `ValidationExceptionHandler` converts to an RFC 7807 `ValidationProblemDetails` response (HTTP 400). The endpoint never catches this — it propagates up the middleware pipeline.

## Request Lifecycle

### Typical query: GET /api/events

1. Nginx (prod) or Vite proxy (dev) forwards `GET /api/events` to the API.
2. `UseAuthentication` validates the JWT from the `Authorization: Bearer` header. Requests without a valid token are rejected at the middleware level (401).
3. `EventEndpoints.GetEventsAsync` extracts `UserId` from `ClaimTypes.NameIdentifier`.
4. `IMediator.Send(new GetEventsQuery(userId, eventType, from, to))` is called.
5. `ValidationBehavior` runs `GetEventsValidator`: rejects an `EventType` that isn't a valid enum name, and rejects `From > To`, both as `400`.
6. `GetEventsHandler` runs: filters `db.Events` by `UserId`, optionally by `EventType` and date range, orders by `EventDate` descending, eagerly loads `Splits`.
7. `EventMapper.ToResponse` converts each entity to `EventResponse` (enum strings are uppercased).
8. Handler returns `IReadOnlyList<EventResponse>`. Mediator forwards to endpoint. Endpoint returns `TypedResults.Ok(result)`.

### Typical command: POST /api/events/upload

1. Endpoint extracts `UserId` from claims, opens the `IFormFile` stream.
2. `IMediator.Send(new UploadEventCommand(userId, contentType, stream))`.
3. `ValidationBehavior` runs `UploadEventValidator`: validates non-empty userId, content type must start with `text/csv` or `application/json`, file must be non-empty and ≤ 10 MB.
4. `UploadEventHandler` selects the first `IEventParser` where `CanParse(contentType)` returns `true`.
5. Parser produces `IReadOnlyList<ParsedEvent>`.
6. Handler loads existing keys for the user (`UserId + EventType + EventName + EventDate`) as a hash set.
7. For each `ParsedEvent`: validate `EventType` and `Completion` against enums (skip with warning if invalid); skip if a duplicate key is found. Otherwise create `Event` and related `EventSplit` entities.
8. `db.SaveChangesAsync` persists all entities in a single round-trip.
9. Returns `IReadOnlyList<EventResponse>` for all newly created events.

## Auth Flow

### Registration

```
POST /api/auth/register  { email, password }
  → RegisterValidator (email format, password length 8–100)
  → RegisterHandler
      → UserManager.FindByEmailAsync (duplicate check)
      → UserManager.CreateAsync (ASP.NET Identity — hashes password)
      → JwtTokenService.GenerateToken
          → HMAC-SHA256 JWT, claims: sub=userId, email, jti
          → Expires: UtcNow + Jwt:AccessTokenExpiryMinutes (default 15)
      → RefreshTokenService issues a refresh token
  → AuthEndpoints sets an httpOnly, SameSite=Strict refresh cookie scoped to /api/auth (Secure in production)
  ← 201 Created  { userId, email, token }
  ← 409 Conflict (duplicate email)
  ← 400 Bad Request (validation failure)
```

### Login

```
POST /api/auth/login  { email, password }
  → LoginHandler
      → UserManager.FindByEmailAsync
      → UserManager.CheckPasswordAsync
      → JwtTokenService.GenerateToken
      → RefreshTokenService issues a refresh token
  → AuthEndpoints sets the refresh cookie (same as registration)
  ← 200 OK  { userId, email, token }
  ← 401 Unauthorized (bad credentials — email existence not revealed)
```

### Refresh and logout

```
POST /api/auth/refresh   (no body — refresh token read from the httpOnly cookie)
  → RefreshHandler validates and rotates the refresh token (old one revoked, new one issued)
  → AuthEndpoints sets the new refresh cookie
  ← 200 OK  { token }                    (new short-lived access token)
  ← 401 Unauthorized + cookie cleared    (missing/expired/revoked refresh token)

POST /api/auth/logout   (requires a valid access token)
  → LogoutHandler revokes the refresh token found in the cookie
  → AuthEndpoints clears the cookie
  ← 204 No Content
```

### Frontend token lifecycle

1. `AuthContext.login(userId, email, token)` calls `tokenStore.set(token)` (module-level variable in `lib/api.ts` — never written to `localStorage` or `sessionStorage`).
2. `apiClient` Axios request interceptor reads `tokenStore.get()` and adds `Authorization: Bearer <token>` to every outbound request.
3. On a `401`, the Axios response interceptor calls `POST /api/auth/refresh` (browser sends the httpOnly cookie automatically) to obtain a new access token, queueing any other requests that 401 concurrently so only one refresh call is in flight, then retries all of them; if refresh itself fails (or the retried request 401s again), it falls through to logout.
4. `AuthContext.logout()` calls `tokenStore.clear()`, `POST /api/auth/logout`, and sets `user` to `null`, which triggers `AuthGuard` to redirect to `/login` on the next navigation.
5. The access token has a 15-minute server-side lifetime (`Jwt:AccessTokenExpiryMinutes`); the refresh token is a 7-day httpOnly cookie, rotated on every use.

### Route protection

`AuthGuard` wraps all protected routes. If `isAuthenticated` is false it renders `<Navigate to="/login" replace />`. `isAuthenticated` is derived from `user !== null` in `AuthContext`.

## Parser Dispatch

All `IEventParser` implementations are registered as singletons in `Program.cs`: `CsvEventParser`, `JsonEventParser`, and `GpxEventParser`. `UploadEventHandler` receives `IEnumerable<IEventParser>` by DI injection and dispatches:

```csharp
var parser = parsers.FirstOrDefault(p => p.CanParse(command.ContentType))
    ?? throw new InvalidOperationException(...);
```

`CsvEventParser.CanParse` matches any `contentType` starting with `"text/csv"` (case-insensitive). `JsonEventParser.CanParse` matches `"application/json"`. `GpxEventParser.CanParse` matches GPX content types. Adding a new format requires: implementing `IEventParser`, registering it in `Program.cs`, and adding a `CanParse` guard — no existing code changes.

`EventType` and `Completion` values are uppercased by the parsers before returning `ParsedEvent`. The handler then parses them with `Enum.TryParse(ignoreCase: true)`.

## Chat Tool Plugin Pattern

`POST /api/chat/message` streams a Server-Sent Events response: text deltas as the model generates them, tool-start events when the model invokes a tool, and a final error event on failure. `SendMessageHandler` runs the agentic loop against the Anthropic SDK; when the model requests a tool call, it's routed through `IChatToolExecutor` and the result fed back to Anthropic for the next turn.

```
IChatToolHandler           — one implementation per Anthropic tool call
  ValueTask<string> ExecuteAsync(JsonNode input, string userId, CancellationToken ct)

IChatToolExecutor          — dispatches by tool name
  ValueTask<string> ExecuteAsync(string toolName, JsonNode input, string userId, CancellationToken ct)

ChatToolExecutor           — concrete implementation
  • Receives IReadOnlyDictionary<string, IChatToolHandler>
  • Dispatches to the matching handler by name
  • Logs unknown tool names at Warning; logs handler exceptions at Critical
```

Four tool handlers are registered in `Program.cs`:

| Tool name | Handler | Purpose |
|---|---|---|
| `get_events` | `GetEventsToolHandler` | Query the user's own events (DB-backed, scoped) |
| `get_personal_bests` | `GetPersonalBestsToolHandler` | Query the user's personal bests (DB-backed, scoped) |
| `scrape_race_results` | `ScrapeRaceResultsToolHandler` | Fetch and parse a race-results page (HTTP client) |
| `fetch_training_tips` | `FetchTrainingTipsToolHandler` | Fetch external training content (HTTP client) |

The Anthropic model is set via `Anthropic:Model` in configuration (defaults to `claude-sonnet-4-6` in `appsettings.json`; `CLAUDE.md` names `claude-haiku-4-5-20251001` — see the Known Limitations note on this ambiguity in `00-overview.md`). The frontend (`ChatWidget`/`ChatPanel`/`ChatMessage`/`ChatToolStatus`) consumes the stream via `lib/chatApi.ts` and the `useChatStream` hook.

## Development vs Production Topology

### Development

```
Browser → Vite dev server (:5173) → /api/* proxy → ASP.NET Core API (:5291)
                                                       ↓
                                                  PostgreSQL (:5432)
```

The Vite proxy (`vite.config.ts`) rewrites `/api` requests to `http://localhost:5291`. CORS is not required because the browser sees a single origin. EF Core runs `Database.Migrate()` automatically at startup (`Development` environment only).

### Production (target topology)

```
Browser → Nginx (:8080) → /apis/pacevite/* → ASP.NET Core API (:5291)
              ↓
        Static Vite build
                                                   ↓
                                              PostgreSQL (:5432)
```

Nginx strips the `/apis/pacevite/` path prefix and forwards `X-Forwarded-Prefix` so `ForwardedPrefixTransformer` can rewrite the OpenAPI server URL for Scalar. The API's `UseForwardedHeaders` middleware is restricted to RFC-1918 private networks (10/8, 172.16/12, 192.168/16) and loopback addresses to prevent spoofed `X-Forwarded-*` headers from public clients.

**Note:** the checked-in `docker-compose.yml` currently only defines `proxy` and `db` services (see `00-overview.md`'s Deployment section) — it does not yet containerize the API or the static frontend build. This diagram describes the intended production shape based on the Nginx config and `ForwardedPrefixTransformer`, not a topology `docker-compose.yml` fully implements today.

Auth endpoints are rate-limited to 10 requests/minute per the `"auth"` fixed-window policy. This limit is configurable via `RateLimit:Auth:PermitLimit`.

## Dark Mode Architecture

```
ThemeContext (React context)
  ├─ resolveInitialTheme() — checks localStorage, falls back to prefers-color-scheme
  ├─ useEffect: toggles class "dark" on document.documentElement
  ├─ useEffect: listens for OS changes (only when no localStorage override)
  └─ toggleTheme() — writes next theme to localStorage, updates state

ThemeToggle component — calls toggleTheme(), renders Sun/Moon icon

Chart components (ProgressChart, SplitChart, RaceComparison)
  └─ useTheme()  ← subscribes to ThemeContext, forces re-render on change
  └─ getComputedStyle(document.documentElement)
       → reads --color-secondary, --color-surface, --color-muted from CSS vars
       → passes values as tick/tooltip props to Recharts
```

Tailwind v4 generates dark-mode CSS variables. By calling `useTheme()` in each chart component without consuming its return value, the component re-renders whenever the theme changes and re-reads the CSS custom properties from the live document style.

## Component Hierarchy and Routing

```
App
├─ ThemeProvider
│   └─ QueryClientProvider
│       └─ AuthProvider
│           ├─ RouterProvider
│           │   ├─ /login              → LoginPage
│           │   ├─ /register           → RegisterPage
│           │   ├─ /dashboard          → AuthGuard → DashboardPage
│           │   │                          ├─ ProgressChart
│           │   │                          ├─ PbPanel
│           │   │                          └─ (personal bests grid, event table)
│           │   ├─ /upload             → AuthGuard → UploadPage
│           │   ├─ /events/new         → AuthGuard → AddEventPage
│           │   ├─ /events/:id         → AuthGuard → EventDetailPage
│           │   │                          ├─ SplitChart
│           │   │                          └─ RaceComparison
│           │   ├─ /predict            → AuthGuard → PredictPage
│           │   │                          ├─ PredictionCard / PredictionChart
│           │   │                          └─ PredictionCoaching / PredictionTeaser
│           │   ├─ /sync               → AuthGuard → SyncPage
│           │   ├─ /                   → Navigate /dashboard
│           │   └─ *                   → Navigate /dashboard
│           └─ ChatWidget                  (mounted outside the router — floats on every route)
```

`AuthGuard` performs client-side route protection. All API calls are secured at the server level by JWT bearer authentication. `ChatWidget` is mounted as a sibling of `RouterProvider` rather than inside a specific route, but it self-gates on `useAuth().isAuthenticated` and renders nothing when logged out.

## State Management Approach

| Data category | Storage |
|---|---|
| Server data (events, PBs) | TanStack Query cache (`queryKey: ['events']`, `['personal-bests']`, `['event', id]`) |
| Authentication state | `AuthContext` React state + `tokenStore` module variable |
| Theme preference | `ThemeContext` React state + `localStorage` |
| UI state (search, pagination, selected type) | Local `useState` in `DashboardPage` |

TanStack Query is configured with `staleTime: 30_000` ms and `retry: 1`. Mutations that modify events (`upload`, `delete`) call `queryClient.invalidateQueries` for both `['events']` and `['personal-bests']` to keep the cache consistent.

## Key Patterns and Rationale

| Pattern | Where | Rationale |
|---|---|---|
| Vertical slice architecture | `Features/` | Keeps all code for a feature co-located; avoids cross-feature coupling |
| Source-generated Mediator | All commands/queries | Zero-overhead dispatch vs reflection-based mediators; enforces single-handler-per-message |
| `AuthResult` discriminated union | `Features/Auth/` | Maps auth outcomes (success, duplicate, bad credentials) to specific HTTP status codes without throwing exceptions for expected business failures |
| `IEventParser` strategy | `Infrastructure/Parsing/` | Open/Closed: new file formats are added as new implementations, no existing code changes |
| `IChatToolHandler` plugin | `Infrastructure/Chat/` | OCP + ISP: each tool is a focused, independently testable class; `ChatToolExecutor` dispatches by name |
| In-memory JWT (`tokenStore`) | `lib/api.ts` | Prevents XSS from reading the token via `localStorage` |
| CSS var dark mode | `ThemeContext` + chart components | Single source of truth in CSS; Recharts cannot read Tailwind classes directly so CSS vars bridge the gap |

## Architectural Decisions

All significant architectural decisions for this project are recorded in `docs/decisions/` (MADR format):

1. `0001-vertical-slice-architecture.md`
2. `0002-source-generated-mediator.md`
3. `0003-jsonb-for-location-and-metadata.md`
4. `0004-in-memory-jwt-storage.md`
5. `0005-auth-result-discriminated-union.md`
6. `0006-no-fk-from-event-userid-to-identity.md`
7. `0007-standalone-refresh-tokens-table.md`
8. `0008-no-native-aot.md` — records why full Native AOT was attempted and abandoned (EF Core 10's precompiled-query support rejects standard ad-hoc LINQ).

## Assumptions

- The `ValidationBehavior` is the sole cross-cutting concern in the Mediator pipeline. No logging, tracing, or caching behaviors are registered.
- `GetEventsQuery` is validated by `GetEventsValidator` (rejects invalid `EventType` values and `From > To`). `LoginCommand` still has no FluentValidation validator — input validation for login relies on Identity's `CheckPasswordAsync` returning false for incorrect credentials.
- `nginx/prod.conf` exists on disk but is not currently referenced by `docker-compose.yml` (which mounts `nginx/dev.conf` for the `proxy` service). Its exact production wiring is not confirmed by this codebase alone.
- Pagination is client-side only. The `GET /api/events` endpoint returns all user events in a single response with no server-side page/cursor parameters.
- There is no CORS configuration in `Program.cs`. The Vite proxy in development and the Nginx proxy in production ensure the browser sees a single origin.
