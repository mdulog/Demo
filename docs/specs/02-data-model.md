# Pacevite — Data Model

## Overview

Pacevite uses PostgreSQL 17 via EF Core 10. The schema has four application tables (`Events`, `EventSplits`, `RefreshTokens`, `SyncConnections`) alongside the standard ASP.NET Identity tables (`AspNetUsers`, `AspNetRoles`, etc.). All schema changes are managed via EF Core migrations in `src/Pacevite.Api/Migrations/`.

User ownership is enforced at the application layer — there is no database-level FK from `Events.UserId` to `AspNetUsers.Id`. See [ADR 0006](../decisions/0006-no-fk-from-event-userid-to-identity.md).

---

## Tables

### `Events`

Primary table for race results. Each row represents one athlete's result in one race.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `Id` | `uuid` | NOT NULL | PK, generated via `Guid.NewGuid()` |
| `UserId` | `text` | NOT NULL | FK-by-convention to `AspNetUsers.Id` (no DB constraint) |
| `EventType` | `text` | NOT NULL | Stored as string: `Marathon`, `Hyrox`, `Spartan`, `Generic` |
| `EventName` | `text` | NOT NULL | Human-readable race name (e.g. "Berlin Marathon 2025") |
| `EventDate` | `date` | NOT NULL | Date of the race |
| `Completion` | `text` | NOT NULL | `Finished`, `Dnf`, `Dns` |
| `ElapsedSecs` | `integer` | NOT NULL | Total finish time in seconds |
| `OverallRank` | `integer` | NULL | Overall finishing position in field |
| `AgeGroupRank` | `integer` | NULL | Finishing position within age group |
| `FieldSize` | `integer` | NULL | Total starters in overall field |
| `AgeGroupFieldSize` | `integer` | NULL | Total starters in age group |
| `Location` | `jsonb` | NOT NULL | Structured location data (city, country, etc.) — see below |
| `Metadata` | `jsonb` | NOT NULL | Event-type-specific supplementary data — see below |
| `Source` | `text` | NOT NULL | Set by the creating path: `"CSV"` / `"JSON"` / `"GPX"` (file upload), `"STRAVA"` (import), `"MANUAL"` (default, manual entry) |
| `NeedsEnrichment` | `boolean` | NOT NULL | Flags an event (e.g. from GPX) that's missing data a user may want to fill in later |
| `ExternalActivityId` | `text` | NULL | Set only for events created via sync — the originating external activity ID, so re-sync doesn't re-offer it |
| `SyncConnectionId` | `uuid` | NULL | FK → `SyncConnections.Id` — which sync connection produced this event, if any |
| `CreatedAt` | `timestamp with time zone` | NOT NULL | UTC timestamp of row creation |

**Indexes:**

| Name | Columns | Method | Purpose |
|---|---|---|---|
| `PK_Events` | `Id` | B-tree | Primary key |
| `IX_Events_UserId_EventType` | `(UserId, EventType)` | B-tree | Personal bests query — filter by user and type |
| `IX_Events_UserId_EventDate` | `(UserId, EventDate)` | B-tree | Date-range queries |
| `IX_Events_Metadata` | `Metadata` | GIN | Future station/obstacle queries on JSONB content |

**Enum values (stored as strings):**

`EventType`: `Marathon` · `Hyrox` · `Spartan` · `Generic`

`CompletionStatus`: `Finished` · `Dnf` · `Dns`

Parsers uppercase raw values before producing `ParsedEvent`. The handler then calls `Enum.TryParse(ignoreCase: true)` and skips rows with unrecognised values.

**Duplicate detection key:**

`(UserId, EventType, EventName, EventDate)` — checked in-memory by `UploadEventHandler` before insert to prevent re-uploading the same race.

---

### `EventSplits`

Child table for individual split segments within a race. Ordered by `SplitSecs` ascending at query time; no ordinal column is stored.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `Id` | `uuid` | NOT NULL | PK |
| `EventId` | `uuid` | NOT NULL | FK → `Events.Id` CASCADE DELETE |
| `SplitType` | `text` | NOT NULL | Category label (e.g. `"Running"`, `"Station"`) |
| `SplitLabel` | `text` | NOT NULL | Human-readable name (e.g. `"SkiErg"`, `"10K Split"`) |
| `SplitSecs` | `integer` | NOT NULL | Duration of this split in seconds |
| `CumulativeSecs` | `integer` | NOT NULL | Running total from race start |
| `Metadata` | `jsonb` | NOT NULL | Split-level supplementary data |

**Indexes:**

| Name | Columns | Method | Purpose |
|---|---|---|---|
| `PK_EventSplits` | `Id` | B-tree | Primary key |
| `IX_EventSplits_EventId` | `EventId` | B-tree | Join from `Events` |

**Cascade behaviour:** Deleting an `Event` row cascades and deletes all related `EventSplits` rows.

---

### `RefreshTokens`

Standalone table for refresh-token session continuity (see [ADR 0007](../decisions/0007-standalone-refresh-tokens-table.md) — not part of `AspNetUsers`/Identity's own token storage).

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `Id` | `uuid` | NOT NULL | PK |
| `UserId` | `text` | NOT NULL | FK-by-convention to `AspNetUsers.Id` (no DB constraint, consistent with `Events.UserId`) |
| `TokenHash` | `text` | NOT NULL | Hash of the raw token — the raw value is never persisted |
| `ExpiresAt` | `timestamp with time zone` | NOT NULL | 7-day lifetime from issuance |
| `CreatedAt` | `timestamp with time zone` | NOT NULL | UTC timestamp of issuance |
| `RevokedAt` | `timestamp with time zone` | NULL | Set on logout or rotation |
| `ReplacedByTokenHash` | `text` | NULL | Links a rotated-out token to its replacement |

**Indexes:** `PK_RefreshTokens` (`Id`), unique index on `TokenHash`, index on `(UserId, RevokedAt)`.

---

### `SyncConnections`

One row per athlete-platform OAuth connection (currently Strava only — see `docs/specs/2026-07-03-external-sync-scope-decisions.md` for why Garmin/Apple Health are parked).

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `Id` | `uuid` | NOT NULL | PK |
| `UserId` | `text` | NOT NULL | FK-by-convention to `AspNetUsers.Id` |
| `Platform` | `text` | NOT NULL | `SyncPlatform` enum, stored as string (`Strava` today) |
| `ExternalAthleteId` | `text` | NOT NULL | The platform's own athlete/user ID |
| `AccessTokenEncrypted` / `RefreshTokenEncrypted` | `text` | NOT NULL | Encrypted at rest via ASP.NET Core Data Protection (`IDataProtector`) — raw OAuth tokens are never stored or logged (OWASP A02) |
| `ExpiresAt` | `timestamp with time zone` | NOT NULL | Access-token expiry; refreshed proactively before requests when close to expiring |
| `ConnectedAt` | `timestamp with time zone` | NOT NULL | UTC timestamp the connection was created |

**Indexes:** `PK_SyncConnections` (`Id`), unique index on `(UserId, Platform)` — one connection per platform per user.

`SyncConnection.Events` is a navigation property: `Events.SyncConnectionId` optionally links an event back to the connection that imported it.

---

## JSONB Columns

The `Location` and `Metadata` columns on `Events`, and `Metadata` on `EventSplits`, store structured supplementary data without requiring per-event-type schema migrations. See [ADR 0003](../decisions/0003-jsonb-for-location-and-metadata.md).

**Known key conventions by event type (not enforced by schema):**

| EventType | `Location` keys | `Metadata` / `EventSplit.Metadata` keys |
|---|---|---|
| Marathon | `city`, `country` | — |
| Hyrox | `city`, `country` | `station` (on splits) |
| Spartan | `city`, `country`, `venue` | `obstacles`, `penalty_laps` |
| Generic | — | — |

These conventions are established by `CsvEventParser` and `JsonEventParser`. New parsers must follow the same conventions or document new keys here.

---

## ASP.NET Identity Tables

The standard Identity schema (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserLogins`, `AspNetUserTokens`) is managed entirely by `IdentityDbContext<IdentityUser>`. No customisation has been applied to the `IdentityUser` type.

`AspNetUsers.Id` is a `text` primary key (GUID string). This value is stored as `Events.UserId` without a FK constraint.

---

## Entity Relationships

```
AspNetUsers (Identity-managed)
  │
  │  UserId (string, no FK constraint — see ADR 0006)
  │
  ├──< Events (one user → many events)
  │         │
  │         │  EventId (uuid, FK with CASCADE DELETE)
  │         │
  │         └──< EventSplits (one event → many splits)
  │
  ├──< RefreshTokens (one user → many tokens, historical + active)
  │
  └──< SyncConnections (one user → at most one per Platform)
            │
            │  SyncConnectionId (uuid, nullable FK, no cascade)
            │
            └──< Events (one connection → many imported events)
```

---

## EF Core Configuration Notes

- Enum columns (`EventType`, `Completion`) use `.HasConversion<string>()` — stored as text, not integer ordinals.
- JSONB columns use a `ValueConverter<Dictionary<string, object>, string>` with explicit `.HasColumnType("jsonb")` so PostgreSQL stores binary JSONB rather than a plain text column.
- `Database.Migrate()` runs automatically at startup in the `Development` environment. No manual migration step is required locally.
- Migration files live in `src/Pacevite.Api/Migrations/`. The current baseline is `20260329202444_InitialCreate`.
