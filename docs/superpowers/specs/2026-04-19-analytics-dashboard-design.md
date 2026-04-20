# Analytics Dashboard — Design Spec

**Date:** 2026-04-19
**Status:** Approved

## Goal

Surface meaningful race analytics without changing the existing event upload or list experience. Athletes should be able to see how they are improving over time, where their personal bests stand, and how each individual race broke down by split — all from data already stored in the database.

---

## Scope

### In scope
- Progress-over-time chart on the Dashboard (pace/elapsed time by event type)
- Personal bests panel on the Dashboard (best time per event type)
- New Event Detail page (`/events/:id`) with split breakdown chart and comparison to historical average
- New `GET /api/events/{id}` API endpoint (returns event + splits)
- `chartUtils.ts` pure utility module for all aggregation logic
- Unit, component, integration, and E2E tests for all new code

### Out of scope
- AI coach / predictive pace analysis (sub-project 2)
- Strava / Garmin / FIT file import (sub-project 3)
- Goals and target times
- Social sharing

---

## Architecture

### Data flow

```
GET /api/events  ──→  useEvents() (existing hook)
                         ├──→ ProgressChart   (groups by type + date, sorts ascending)
                         ├──→ PbPanel         (finds min elapsed per event type)
                         └──→ EventDetailPage (filters to same event type for average computations)

GET /api/events/:id ──→  useEvent(id) (new hook)
                         └──→ EventDetailPage (the specific race + its splits)

EventDetailPage uses BOTH hooks:
  useEvent(id)   → the race being viewed (header, raw splits)
  useEvents()    → full history filtered to same event type (averages for SplitChart + RaceComparison)
```

All aggregation is client-side. The existing `GET /api/events` response contains enough data to power both Dashboard panels without new query endpoints. The only new backend work is `GET /api/events/{id}` to support the detail page.

### Charting library

**Recharts** — best TypeScript support in the React 19 / Vite ecosystem, composable API, tree-shakeable. No other charting dependency is introduced.

---

## Backend

### New endpoint: `GET /api/events/{id}`

- Returns a single `EventDto` with its `Splits` collection populated.
- Returns `404` if the event does not exist or does not belong to the authenticated user.
- Follows the existing vertical slice pattern: `GetEventById/` folder under `Features/Events/` with its own query, handler, and validator.
- Registered on the `MapEventEndpoints()` group — no changes to `Program.cs` routing.
- Integration test: success (200 + body), not-found (404), wrong-user (404).

---

## Frontend

### Dashboard changes (`DashboardPage.tsx`)

Two new panels are added **above** the existing event list. The event list is not modified.

#### `ProgressChart`
- Recharts `LineChart` — X axis: event date, Y axis: elapsed seconds formatted as `h:mm:ss`.
- One series per rendered view; user selects event type via a dropdown (defaults to the type with the most events).
- Data sourced from `useEvents()` — filtered and sorted by `chartUtils.groupByEventType`.
- Personal best point is highlighted in green.

#### `PbPanel`
- One row per event type present in the athlete's history.
- Displays the best finish time and a relative progress bar scaled to the athlete's own range (worst → best).
- Clicking a row filters the `ProgressChart` to that event type.

### New page: `EventDetailPage` (`/events/:id`)

#### `SplitChart`
- Recharts `BarChart` — one bar per split.
- Each bar is coloured green if that split was faster than the athlete's average split pace for the same event type, red if slower.
- Average split pace is derived from `chartUtils.computeAverageSplits` across all events of the same type.
- Displays the raw split time and the delta (e.g. `+0:46`) as a label on each bar.

#### `RaceComparison`
- Shows the finish time delta vs the athlete's historical average for the same event type (e.g. `−4:21 faster than your avg`).
- Includes a Recharts `LineChart` sparkline of all races of that type, with the current race highlighted.
- Displays best and worst times for the event type.

### `chartUtils.ts` (`src/lib/chartUtils.ts`)

Pure functions with no side effects — fully unit-testable without rendering.

| Function | Input | Output |
|---|---|---|
| `groupByEventType` | `EventDto[]` | `Record<string, EventDto[]>` sorted by date ascending |
| `computePbs` | `EventDto[]` | `Record<string, EventDto>` — best (min elapsed) per event type |
| `computeAverageSplits` | `EventDto[]` (same type) | `ParsedSplit[]` — mean `splitSecs` per split label |
| `computeSplitDeltas` | `EventDto`, avg splits | `Array<{ label, secs, delta, faster }>` |
| `formatElapsed` | `number` (seconds) | `string` (`h:mm:ss` or `m:ss`) |

### New hook: `useEvent(id: string)`

- Wraps `GET /api/events/{id}` via Axios.
- Uses TanStack Query — key `['event', id]`.
- Returns `{ data, isLoading, isError }`.

### Routing

Add `<Route path="/events/:id" element={<EventDetailPage />} />` inside the authenticated route group in `App.tsx`.

---

## Testing plan

| Layer | What | Tool |
|---|---|---|
| Unit | `chartUtils.ts` — all five functions, including edge cases (empty input, single event, missing splits) | Vitest |
| Component | `ProgressChart`, `PbPanel`, `SplitChart`, `RaceComparison` — renders correct data, handles loading/empty states | Vitest + Testing Library + MSW |
| Integration | `GET /api/events/{id}` — 200 with splits, 404 not found, 404 wrong user | TUnit + Testcontainers |
| E2E | Dashboard shows chart panels after login; clicking a race navigates to detail page and shows splits | Playwright |

---

## File map

| Action | File |
|---|---|
| Create | `src/Pacevite.Api/Features/Events/GetEventById/GetEventByIdQuery.cs` |
| Create | `src/Pacevite.Api/Features/Events/GetEventById/GetEventByIdHandler.cs` |
| Create | `src/Pacevite.Api/Features/Events/GetEventById/GetEventByIdValidator.cs` |
| Modify | `src/Pacevite.Api/Features/Events/EventEndpoints.cs` |
| Create | `tests/Pacevite.Api.Tests/Integration/GetEventByIdTests.cs` |
| Create | `src/Pacevite.Web/src/lib/chartUtils.ts` |
| Create | `src/Pacevite.Web/src/lib/chartUtils.test.ts` |
| Create | `src/Pacevite.Web/src/hooks/useEvent.ts` |
| Modify | `src/Pacevite.Web/src/pages/DashboardPage.tsx` |
| Modify | `src/Pacevite.Web/src/pages/DashboardPage.test.tsx` |
| Create | `src/Pacevite.Web/src/pages/EventDetailPage.tsx` |
| Create | `src/Pacevite.Web/src/pages/EventDetailPage.test.tsx` |
| Modify | `src/Pacevite.Web/src/App.tsx` |
| Modify | `src/Pacevite.Web/src/test/handlers.ts` (add MSW handler for `/api/events/:id`) |
| Modify | `src/Pacevite.Web/e2e/dashboard.spec.ts` (extend with chart assertions) |
| Create | `src/Pacevite.Web/e2e/event-detail.spec.ts` |
