# Frontend Testing Design

**Date:** 2026-04-18
**Scope:** Unit/component tests and E2E tests for `src/Pacevite.Web`

---

## Overview

The frontend has no tests today. This spec introduces two complementary test layers:

- **Unit/component tests** — Vitest + React Testing Library + MSW. Fast, isolated, run without a live backend.
- **E2E tests** — Playwright against the real running stack (Vite dev server + .NET API + PostgreSQL).

No CI integration in this phase — local infrastructure only.

---

## Unit / Component Tests

### Stack

| Tool | Role |
|---|---|
| Vitest | Test runner — native Vite integration, zero transform overhead, shares path alias config |
| React Testing Library | Component rendering and user interaction |
| MSW (Mock Service Worker) | Network-layer API mocking — intercepts axios requests realistically |
| `@testing-library/jest-dom` | Additional DOM matchers (`toBeInTheDocument`, `toHaveValue`, etc.) |
| `jsdom` | Browser DOM simulation in Node |

### Configuration

- `vitest.config.ts` at `src/Pacevite.Web/` extends `vite.config.ts` so `@/` path aliases resolve identically in tests.
- `environment: 'jsdom'` set globally.
- A shared `src/test/setup.ts` file:
  - Starts the MSW server before all tests
  - Resets handlers after each test (prevents handler bleed between tests)
  - Stops the server after all tests
  - Extends `expect` with `@testing-library/jest-dom`
- MSW happy-path handlers in `src/test/handlers.ts` — one per API endpoint, returning realistic response shapes. Individual tests override these for error scenarios.

### Test File Location

Colocated with source — `RegisterPage.test.tsx` alongside `RegisterPage.tsx`. Keeps tests close to the code they cover and makes it obvious when a component has no test.

### Coverage

| File | Scenarios |
|---|---|
| `AuthGuard` | Redirects unauthenticated user to `/login`; renders children when authenticated |
| `RegisterPage` | Field error under email on 409; field errors under inputs on 400 validation failure; banner on network failure; navigates to `/dashboard` on success |
| `LoginPage` | Error message on 401 invalid credentials; navigates to `/dashboard` on success |
| `AuthContext` | `login()` sets user and stores token; `logout()` nulls user and clears token |
| `DashboardPage` | Renders event list from API response; renders personal bests section; delete button fires correct API call |

### Test Helper

A shared `src/test/render.tsx` helper wraps `render` from RTL with `QueryClientProvider`, `AuthProvider`, and `MemoryRouter` so every test gets the full provider tree without boilerplate.

---

## E2E Tests

### Stack

| Tool | Role |
|---|---|
| Playwright | Browser automation — multi-browser capable, fast, strong DX |
| Chromium | Only browser for local dev; multi-browser deferred to CI phase |

### Configuration

`playwright.config.ts` at `src/Pacevite.Web/`:
- `baseURL: 'http://localhost:5173'`
- `webServer` block starts `npm run dev` automatically if not already running
- Screenshots and traces captured on failure only
- Timeout: 30s per test

### Test Location

`src/Pacevite.Web/e2e/` — separate directory so Vitest does not pick up Playwright tests. Vitest config explicitly excludes the `e2e/` directory.

### Fixtures

`e2e/fixtures/events.csv` — a small valid CSV file committed to the repo, used by the upload test.

### Coverage — Golden Paths Only

| Flow | Steps |
|---|---|
| **Register** | Fill email + password → submit → assert `/dashboard` with empty state message |
| **Login** | Register user via API directly → fill login form → submit → assert `/dashboard` |
| **Upload** | Login → navigate to upload → attach `events.csv` fixture → submit → assert events appear on dashboard |
| **Delete** | Login → create event via API → assert event visible on dashboard → click delete → assert event gone |

### Test Data Isolation

Each test registers a unique email (`test-${Date.now()}@example.com`). No shared seeds or `beforeAll` DB setup — API calls within the test handle any required state. Tests are fully independent and can run in any order.

---

## What Is Explicitly Out of Scope

- CI pipeline configuration (deferred)
- Multi-browser E2E (deferred to CI phase)
- Visual regression testing
- Accessibility auditing
- Coverage thresholds / enforcement
