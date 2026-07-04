# Pacevite

[![CI](https://github.com/mdulog/Pacevite/actions/workflows/ci.yml/badge.svg)](https://github.com/mdulog/Pacevite/actions/workflows/ci.yml)

Fitness event tracking for endurance and functional-fitness athletes. Upload race results (CSV, JSON, or GPX) or enter them manually, import activities from Strava, track personal bests and split performance, get a finish-time prediction with AI-generated coaching notes, and chat with an AI coach that can query your own event history.

## Features

- **Race result ingestion** — CSV/JSON/GPX upload, manual entry, or Strava OAuth import
- **Progress tracking** — finish-time trends, personal bests per event type, split-level comparison against your own average
- **Prediction** — linear-regression finish-time forecast with AI-generated coaching commentary
- **AI coach chat** — SSE-streamed conversation that can call tools against your own events and personal bests
- **Auth** — JWT access tokens with httpOnly refresh-token cookies (rotated on use)
- **Light/dark theme**

## Tech Stack

| Layer | Tech |
|---|---|
| API | .NET 10, ASP.NET Core (Slim), EF Core 10, PostgreSQL 17 |
| Auth | ASP.NET Core Identity + JWT Bearer |
| Mediator | [Mediator](https://github.com/martinothamar/Mediator) (source-generated) + FluentValidation pipeline |
| Frontend | React 19, Vite, TypeScript, TanStack Query v5, React Router v7, Tailwind v4 |
| AI | Anthropic SDK (`claude-haiku-4-5-20251001`) via an `IChatToolHandler` plugin pattern |

See [`docs/specs/00-overview.md`](docs/specs/00-overview.md) for the full architecture writeup and [`docs/decisions/`](docs/decisions/) for the ADRs behind the major design choices.

## Getting Started

**Prerequisite:** [Podman](https://podman.io/) installed (this project uses `podman`, not `docker`).

```bash
# 1. Start the database
podman compose up -d db

# 2. Start the API (auto-migrates the DB on first run)
dotnet run --project src/Pacevite.Api --launch-profile http

# 3. Start the frontend
cd src/Pacevite.Web && npm run dev
```

Dev credentials are already set in `appsettings.Development.json` (committed — dev only, never used in production). Open `http://localhost:5173`.

### Environment variables (production)

| Variable | Purpose |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key for prediction coaching and chat |
| `DB_USER` / `DB_PASSWORD` | PostgreSQL credentials |
| `JWT_SECRET` | HMAC signing key (min 32 chars) |
| `JWT_ISSUER` / `JWT_AUDIENCE` | JWT validation values |
| `STRAVA_CLIENT_ID` / `STRAVA_CLIENT_SECRET` / `STRAVA_REDIRECT_URI` | Strava OAuth app credentials |

## Running Tests

```bash
# .NET unit + integration tests (Testcontainers spins up real Postgres)
# NOTE: TUnit uses Microsoft.Testing.Platform — dotnet test is NOT supported on .NET 10; use dotnet run
dotnet run --project tests/Pacevite.Api.Tests

# Frontend unit tests
cd src/Pacevite.Web && npm test

# E2E tests (Playwright — auto-starts API + frontend if not already running)
cd src/Pacevite.Web && npm run test:e2e
```

## Project Structure

```
src/
  Pacevite.Api/        # ASP.NET Core API — vertical-slice Features/, Infrastructure/, Domain/
  Pacevite.Web/         # React 19 / Vite / TypeScript frontend
tests/
  Pacevite.Api.Tests/   # TUnit unit + integration tests
docs/
  specs/                # Architecture, data model, and feature design docs
  decisions/            # ADRs (MADR format)
```

See [`CLAUDE.md`](CLAUDE.md) for the full directory breakdown, per-feature conventions, and common gotchas.

## Port Map

| Service | URL |
|---|---|
| API (dev) | `http://localhost:5291` |
| Frontend (dev) | `http://localhost:5173` |
| Nginx proxy | `http://localhost:8080` → `/apis/pacevite/` → API |
| PostgreSQL | `localhost:5432` |
