# Analytics Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a progress chart and PB panel to the Dashboard, plus a new Event Detail page with split breakdown and historical comparison, all backed by client-side aggregation over the existing events API.

**Architecture:** One new backend endpoint (`GET /api/events/{id}`) following the existing vertical-slice pattern. All chart aggregation lives in a pure `chartUtils.ts` module. Four new React components (ProgressChart, PbPanel, SplitChart, RaceComparison) use Recharts. The Dashboard gains two analytics panels above the existing event list; a new `/events/:id` route renders the detail view. `EventDetailPage` consumes both `useEvent(id)` and the existing events query (to compute type averages).

**Tech Stack:** .NET 10 / TUnit / Testcontainers (backend), React 19 / Recharts 2 / TanStack Query v5 / Vitest / Playwright (frontend)

---

## File Map

| Action | File | Responsibility |
|---|---|---|
| Create | `src/Pacevite.Api/Features/Events/GetEventById/GetEventByIdQuery.cs` | Query record |
| Create | `src/Pacevite.Api/Features/Events/GetEventById/GetEventByIdHandler.cs` | EF Core fetch, ownership check |
| Create | `src/Pacevite.Api/Features/Events/GetEventById/GetEventByIdValidator.cs` | Input validation |
| Modify | `src/Pacevite.Api/Features/Events/EventEndpoints.cs` | Register `GET /{id:guid}` |
| Create | `tests/Pacevite.Api.Tests/Integration/GetEventByIdTests.cs` | 200, 404, 401, ownership |
| Create | `src/Pacevite.Web/src/lib/chartUtils.ts` | Pure aggregation functions |
| Create | `src/Pacevite.Web/src/lib/chartUtils.test.ts` | Unit tests for all five functions |
| Create | `src/Pacevite.Web/src/hooks/useEvents.ts` | Extract events query (shared by Dashboard + Detail) |
| Create | `src/Pacevite.Web/src/hooks/useEvent.ts` | Fetch single event by id |
| Modify | `src/Pacevite.Web/src/test/handlers.ts` | Add MSW handler for `GET /api/events/:id` |
| Create | `src/Pacevite.Web/src/components/ProgressChart.tsx` | Recharts LineChart, pace over time |
| Create | `src/Pacevite.Web/src/components/PbPanel.tsx` | PB per event type with bar indicator |
| Create | `src/Pacevite.Web/src/components/SplitChart.tsx` | Recharts BarChart, splits vs average |
| Create | `src/Pacevite.Web/src/components/RaceComparison.tsx` | Delta vs historical average + sparkline |
| Modify | `src/Pacevite.Web/src/pages/DashboardPage.tsx` | Add analytics panels, use `useEvents` hook |
| Modify | `src/Pacevite.Web/src/pages/DashboardPage.test.tsx` | Add chart panel assertions |
| Create | `src/Pacevite.Web/src/pages/EventDetailPage.tsx` | New detail route `/events/:id` |
| Create | `src/Pacevite.Web/src/pages/EventDetailPage.test.tsx` | Component tests |
| Modify | `src/Pacevite.Web/src/App.tsx` | Register `/events/:id` route |
| Modify | `src/Pacevite.Web/e2e/dashboard.spec.ts` | Assert chart panels visible |
| Create | `src/Pacevite.Web/e2e/event-detail.spec.ts` | Navigate to detail, assert splits |
| Create | `src/Pacevite.Web/e2e/fixtures/events-with-splits.json` | Seeding fixture with split data |

---

## Task 1: Backend — GetEventById vertical slice

**Files:**
- Create: `src/Pacevite.Api/Features/Events/GetEventById/GetEventByIdQuery.cs`
- Create: `src/Pacevite.Api/Features/Events/GetEventById/GetEventByIdHandler.cs`
- Create: `src/Pacevite.Api/Features/Events/GetEventById/GetEventByIdValidator.cs`
- Modify: `src/Pacevite.Api/Features/Events/EventEndpoints.cs`
- Create: `tests/Pacevite.Api.Tests/Integration/GetEventByIdTests.cs`

- [ ] **Step 1: Write the failing integration tests**

Create `tests/Pacevite.Api.Tests/Integration/GetEventByIdTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pacevite.Api.Contracts.Requests;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using TUnit.Core;

namespace Pacevite.Api.Tests.Integration;

[Category("Integration")]
public sealed class GetEventByIdTests
{
    private PostgreSqlContainer _postgres = null!;
    private HttpClient _client = null!;
    private WebApplicationFactory<Program> _factory = null!;

    [Before(Test)]
    public async Task SetUpAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:17")
            .WithDatabase("pacevite_getbyid_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (descriptor is not null)
                        services.Remove(descriptor);

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseNpgsql(_postgres.GetConnectionString()));

                    using var scope = services.BuildServiceProvider().CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.Database.Migrate();
                });

                host.UseSetting("Jwt:Secret", "super-secret-key-for-testing-only-32c");
                host.UseSetting("Jwt:Issuer", "pacevite-test");
                host.UseSetting("Jwt:Audience", "pacevite-test");
            });

        _client = _factory.CreateClient();
    }

    [After(Test)]
    public async Task TearDownAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<string> GetTokenAsync(string email)
    {
        var reg = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "P@ssword1!"));

        if (reg.IsSuccessStatusCode)
        {
            var body = await reg.Content.ReadFromJsonAsync<AuthResponse>();
            return body!.Token;
        }

        var login = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "P@ssword1!"));
        var loginBody = await login.Content.ReadFromJsonAsync<AuthResponse>();
        return loginBody!.Token;
    }

    private static MultipartFormDataContent BuildJsonUpload(string json)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(json));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Add(fileContent, "file", "events.json");
        return content;
    }

    [Test]
    public async Task GetEventById_Returns200WithSplits_WhenEventExists()
    {
        // Arrange
        var token = await GetTokenAsync("getbyid-found@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        const string json = """
            [{
              "event_type": "MARATHON",
              "event_name": "Berlin Marathon",
              "event_date": "2024-09-29",
              "completion": "FINISHED",
              "elapsed_secs": 14400,
              "splits": [
                { "split_type": "RUN", "split_label": "10km", "split_secs": 2940, "cumulative_secs": 2940 },
                { "split_type": "RUN", "split_label": "21km", "split_secs": 3180, "cumulative_secs": 6120 }
              ]
            }]
            """;

        var uploadResponse = await _client.PostAsync("/api/events/upload", BuildJsonUpload(json));
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<List<EventResponse>>();
        var eventId = uploaded![0].Id;

        // Act
        var response = await _client.GetAsync($"/api/events/{eventId}");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var ev = await response.Content.ReadFromJsonAsync<EventResponse>();
        await Assert.That(ev!.Id).IsEqualTo(eventId);
        await Assert.That(ev.EventName).IsEqualTo("Berlin Marathon");
        await Assert.That(ev.Splits.Count).IsEqualTo(2);
        await Assert.That(ev.Splits[0].SplitLabel).IsEqualTo("10km");
        await Assert.That(ev.Splits[1].SplitLabel).IsEqualTo("21km");
    }

    [Test]
    public async Task GetEventById_Returns404_WhenEventDoesNotExist()
    {
        // Arrange
        var token = await GetTokenAsync("getbyid-notfound@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/events/{nonExistentId}");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetEventById_Returns404_WhenEventBelongsToAnotherUser()
    {
        // Arrange — user A uploads an event
        var tokenA = await GetTokenAsync("getbyid-owner@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);

        const string json = """
            [{ "event_type": "10K", "event_name": "Test 10K", "event_date": "2024-06-01",
               "completion": "FINISHED", "elapsed_secs": 2900 }]
            """;
        var uploadResponse = await _client.PostAsync("/api/events/upload", BuildJsonUpload(json));
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<List<EventResponse>>();
        var eventId = uploaded![0].Id;

        // Act — user B tries to fetch it
        var tokenB = await GetTokenAsync("getbyid-thief@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var response = await _client.GetAsync($"/api/events/{eventId}");

        // Assert — 404 not 403, to avoid leaking ownership (OWASP A01)
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetEventById_Returns401_WhenNotAuthenticated()
    {
        var response = await _client.GetAsync($"/api/events/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --filter "Category=Integration&FullyQualifiedName~GetEventById"
```

Expected: build error or 404 on the route (endpoint doesn't exist yet).

- [ ] **Step 3: Create the query record**

Create `src/Pacevite.Api/Features/Events/GetEventById/GetEventByIdQuery.cs`:

```csharp
using Mediator;
using Pacevite.Api.Contracts.Responses;

namespace Pacevite.Api.Features.Events.GetEventById;

public sealed record GetEventByIdQuery(Guid EventId, string UserId) : IQuery<EventResponse?>;
```

- [ ] **Step 4: Create the handler**

Create `src/Pacevite.Api/Features/Events/GetEventById/GetEventByIdHandler.cs`:

```csharp
using Mediator;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Features.Events.GetEventById;

public sealed class GetEventByIdHandler(AppDbContext db)
    : IQueryHandler<GetEventByIdQuery, EventResponse?>
{
    public async ValueTask<EventResponse?> Handle(
        GetEventByIdQuery query, CancellationToken cancellationToken)
    {
        var ev = await db.Events
            .Include(e => e.Splits)
            .Where(e => e.Id == query.EventId && e.UserId == query.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        return ev is null ? null : EventMapper.ToResponse(ev);
    }
}
```

- [ ] **Step 5: Create the validator**

Create `src/Pacevite.Api/Features/Events/GetEventById/GetEventByIdValidator.cs`:

```csharp
using FluentValidation;

namespace Pacevite.Api.Features.Events.GetEventById;

public sealed class GetEventByIdValidator : AbstractValidator<GetEventByIdQuery>
{
    public GetEventByIdValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}
```

- [ ] **Step 6: Register the endpoint in EventEndpoints.cs**

Add the using and the new route. In `src/Pacevite.Api/Features/Events/EventEndpoints.cs`:

At the top, add:
```csharp
using Pacevite.Api.Features.Events.GetEventById;
```

In `MapEventEndpoints`, add alongside the existing routes:
```csharp
app.MapGet("/{id:guid}", GetEventByIdAsync).WithName("GetEventById");
```

Add the private handler method (after `GetPersonalBestsAsync`):
```csharp
private static async Task<Results<Ok<EventResponse>, NotFound>> GetEventByIdAsync(
    Guid id,
    ClaimsPrincipal user,
    IMediator mediator,
    CancellationToken ct)
{
    var userId = GetUserId(user);
    var result = await mediator.Send(new GetEventByIdQuery(id, userId), ct);

    return result is null
        ? TypedResults.NotFound()
        : TypedResults.Ok(result);
}
```

- [ ] **Step 7: Run tests to verify they pass**

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --filter "Category=Integration&FullyQualifiedName~GetEventById"
```

Expected: 4 passed.

- [ ] **Step 8: Commit**

```bash
git add src/Pacevite.Api/Features/Events/GetEventById/ \
        src/Pacevite.Api/Features/Events/EventEndpoints.cs \
        tests/Pacevite.Api.Tests/Integration/GetEventByIdTests.cs
git commit -m "feat(api): add GET /api/events/{id} endpoint"
```

---

## Task 2: chartUtils.ts — pure aggregation functions

**Files:**
- Create: `src/Pacevite.Web/src/lib/chartUtils.ts`
- Create: `src/Pacevite.Web/src/lib/chartUtils.test.ts`

- [ ] **Step 1: Write failing unit tests**

Create `src/Pacevite.Web/src/lib/chartUtils.test.ts`:

```typescript
import { describe, it, expect } from 'vitest'
import {
  groupByEventType,
  computePbs,
  computeAverageSplits,
  computeSplitDeltas,
  formatElapsed,
  type AverageSplit,
} from './chartUtils'
import type { EventResponse } from './types'

const makeEvent = (overrides: Partial<EventResponse> = {}): EventResponse => ({
  id: 'event-1',
  eventType: 'MARATHON',
  eventName: 'Test Marathon',
  eventDate: '2024-01-01',
  completion: 'FINISHED',
  elapsedSecs: 14400,
  overallRank: null,
  ageGroupRank: null,
  fieldSize: null,
  ageGroupFieldSize: null,
  source: 'manual',
  createdAt: '2024-01-01T00:00:00Z',
  splits: [],
  ...overrides,
})

describe('groupByEventType', () => {
  it('returns empty object for empty input', () => {
    expect(groupByEventType([])).toEqual({})
  })

  it('groups events by eventType', () => {
    const events = [
      makeEvent({ id: 'e1', eventType: 'MARATHON' }),
      makeEvent({ id: 'e2', eventType: '10K' }),
      makeEvent({ id: 'e3', eventType: 'MARATHON' }),
    ]
    const result = groupByEventType(events)
    expect(Object.keys(result)).toHaveLength(2)
    expect(result['MARATHON']).toHaveLength(2)
    expect(result['10K']).toHaveLength(1)
  })

  it('sorts events within each group by eventDate ascending', () => {
    const events = [
      makeEvent({ id: 'e1', eventDate: '2024-09-01' }),
      makeEvent({ id: 'e2', eventDate: '2024-03-01' }),
      makeEvent({ id: 'e3', eventDate: '2024-06-01' }),
    ]
    const result = groupByEventType(events)
    const dates = result['MARATHON'].map(e => e.eventDate)
    expect(dates).toEqual(['2024-03-01', '2024-06-01', '2024-09-01'])
  })
})

describe('computePbs', () => {
  it('returns empty object for empty input', () => {
    expect(computePbs([])).toEqual({})
  })

  it('returns the event with the lowest elapsedSecs per type', () => {
    const events = [
      makeEvent({ id: 'e1', eventType: 'MARATHON', elapsedSecs: 14400 }),
      makeEvent({ id: 'e2', eventType: 'MARATHON', elapsedSecs: 13500 }),
      makeEvent({ id: 'e3', eventType: 'MARATHON', elapsedSecs: 15000 }),
    ]
    const result = computePbs(events)
    expect(result['MARATHON'].id).toBe('e2')
    expect(result['MARATHON'].elapsedSecs).toBe(13500)
  })

  it('tracks PBs independently per event type', () => {
    const events = [
      makeEvent({ id: 'e1', eventType: 'MARATHON', elapsedSecs: 14400 }),
      makeEvent({ id: 'e2', eventType: '10K', elapsedSecs: 2900 }),
    ]
    const result = computePbs(events)
    expect(result['MARATHON'].id).toBe('e1')
    expect(result['10K'].id).toBe('e2')
  })
})

describe('computeAverageSplits', () => {
  it('returns empty array for events with no splits', () => {
    expect(computeAverageSplits([makeEvent()])).toEqual([])
  })

  it('returns empty array for empty input', () => {
    expect(computeAverageSplits([])).toEqual([])
  })

  it('computes mean splitSecs per split label', () => {
    const events = [
      makeEvent({
        splits: [
          { id: 's1', splitType: 'RUN', splitLabel: '10km', splitSecs: 3000, cumulativeSecs: 3000 },
        ],
      }),
      makeEvent({
        splits: [
          { id: 's2', splitType: 'RUN', splitLabel: '10km', splitSecs: 3600, cumulativeSecs: 3600 },
        ],
      }),
    ]
    const result = computeAverageSplits(events)
    expect(result).toHaveLength(1)
    expect(result[0].label).toBe('10km')
    expect(result[0].avgSecs).toBe(3300)
  })

  it('handles multiple distinct split labels', () => {
    const events = [
      makeEvent({
        splits: [
          { id: 's1', splitType: 'RUN', splitLabel: '10km', splitSecs: 3000, cumulativeSecs: 3000 },
          { id: 's2', splitType: 'RUN', splitLabel: '21km', splitSecs: 3300, cumulativeSecs: 6300 },
        ],
      }),
    ]
    const result = computeAverageSplits(events)
    expect(result).toHaveLength(2)
    expect(result.find(s => s.label === '10km')!.avgSecs).toBe(3000)
    expect(result.find(s => s.label === '21km')!.avgSecs).toBe(3300)
  })
})

describe('computeSplitDeltas', () => {
  it('returns faster=true when split is below average', () => {
    const event = makeEvent({
      splits: [{ id: 's1', splitType: 'RUN', splitLabel: '10km', splitSecs: 2800, cumulativeSecs: 2800 }],
    })
    const avgSplits: AverageSplit[] = [{ label: '10km', avgSecs: 3000 }]
    const result = computeSplitDeltas(event, avgSplits)
    expect(result[0].delta).toBe(-200)
    expect(result[0].faster).toBe(true)
  })

  it('returns faster=false when split is above average', () => {
    const event = makeEvent({
      splits: [{ id: 's1', splitType: 'RUN', splitLabel: '10km', splitSecs: 3200, cumulativeSecs: 3200 }],
    })
    const avgSplits: AverageSplit[] = [{ label: '10km', avgSecs: 3000 }]
    const result = computeSplitDeltas(event, avgSplits)
    expect(result[0].delta).toBe(200)
    expect(result[0].faster).toBe(false)
  })

  it('returns delta=0 when no matching average split exists', () => {
    const event = makeEvent({
      splits: [{ id: 's1', splitType: 'RUN', splitLabel: 'Unknown', splitSecs: 3000, cumulativeSecs: 3000 }],
    })
    const result = computeSplitDeltas(event, [])
    expect(result[0].delta).toBe(0)
  })

  it('returns empty array for event with no splits', () => {
    expect(computeSplitDeltas(makeEvent(), [])).toEqual([])
  })
})

describe('formatElapsed', () => {
  it('formats sub-hour as m:ss', () => {
    expect(formatElapsed(330)).toBe('5:30')
  })

  it('formats exactly one hour as 1:00:00', () => {
    expect(formatElapsed(3600)).toBe('1:00:00')
  })

  it('formats multi-hour correctly', () => {
    expect(formatElapsed(14523)).toBe('4:02:03')
  })

  it('pads minutes and seconds', () => {
    expect(formatElapsed(65)).toBe('1:05')
  })
})
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd src/Pacevite.Web && npm run test:run -- chartUtils
```

Expected: fail with "Cannot find module './chartUtils'".

- [ ] **Step 3: Implement chartUtils.ts**

Create `src/Pacevite.Web/src/lib/chartUtils.ts`:

```typescript
import type { EventResponse, EventSplitResponse } from './types'

export interface AverageSplit {
  label: string
  avgSecs: number
}

export interface SplitDelta {
  label: string
  secs: number
  delta: number
  faster: boolean
}

export function groupByEventType(events: EventResponse[]): Record<string, EventResponse[]> {
  const grouped: Record<string, EventResponse[]> = {}
  for (const ev of events) {
    if (!grouped[ev.eventType]) grouped[ev.eventType] = []
    grouped[ev.eventType].push(ev)
  }
  for (const key of Object.keys(grouped)) {
    grouped[key].sort((a, b) => a.eventDate.localeCompare(b.eventDate))
  }
  return grouped
}

export function computePbs(events: EventResponse[]): Record<string, EventResponse> {
  const pbs: Record<string, EventResponse> = {}
  for (const ev of events) {
    if (!pbs[ev.eventType] || ev.elapsedSecs < pbs[ev.eventType].elapsedSecs) {
      pbs[ev.eventType] = ev
    }
  }
  return pbs
}

export function computeAverageSplits(events: EventResponse[]): AverageSplit[] {
  const byLabel: Record<string, number[]> = {}
  for (const ev of events) {
    for (const split of ev.splits) {
      if (!byLabel[split.splitLabel]) byLabel[split.splitLabel] = []
      byLabel[split.splitLabel].push(split.splitSecs)
    }
  }
  return Object.entries(byLabel).map(([label, values]) => ({
    label,
    avgSecs: Math.round(values.reduce((a, b) => a + b, 0) / values.length),
  }))
}

export function computeSplitDeltas(event: EventResponse, avgSplits: AverageSplit[]): SplitDelta[] {
  return event.splits.map(split => {
    const avg = avgSplits.find(a => a.label === split.splitLabel)
    const delta = avg ? split.splitSecs - avg.avgSecs : 0
    return { label: split.splitLabel, secs: split.splitSecs, delta, faster: delta < 0 }
  })
}

export function formatElapsed(secs: number): string {
  const h = Math.floor(secs / 3600)
  const m = Math.floor((secs % 3600) / 60)
  const s = secs % 60
  if (h > 0) return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
  return `${m}:${String(s).padStart(2, '0')}`
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd src/Pacevite.Web && npm run test:run -- chartUtils
```

Expected: 16 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Web/src/lib/chartUtils.ts src/Pacevite.Web/src/lib/chartUtils.test.ts
git commit -m "feat(web): add chartUtils aggregation functions with unit tests"
```

---

## Task 3: Hooks and MSW handler

**Files:**
- Create: `src/Pacevite.Web/src/hooks/useEvents.ts`
- Create: `src/Pacevite.Web/src/hooks/useEvent.ts`
- Modify: `src/Pacevite.Web/src/test/handlers.ts`

- [ ] **Step 1: Create useEvents hook**

Create `src/Pacevite.Web/src/hooks/useEvents.ts`:

```typescript
import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api'
import type { EventResponse } from '@/lib/types'

export function useEvents() {
  return useQuery({
    queryKey: ['events'],
    queryFn: async () => {
      const { data } = await apiClient.get<EventResponse[]>('/events')
      return data
    },
  })
}
```

- [ ] **Step 2: Create useEvent hook**

Create `src/Pacevite.Web/src/hooks/useEvent.ts`:

```typescript
import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api'
import type { EventResponse } from '@/lib/types'

export function useEvent(id: string) {
  return useQuery({
    queryKey: ['event', id],
    queryFn: async () => {
      const { data } = await apiClient.get<EventResponse>(`/events/${id}`)
      return data
    },
    enabled: !!id,
  })
}
```

- [ ] **Step 3: Add MSW handler for GET /api/events/:id**

In `src/Pacevite.Web/src/test/handlers.ts`, add a new handler **before** the existing `http.get('http://localhost/api/events', ...)` handler (more specific routes first):

```typescript
http.get('http://localhost/api/events/:id', ({ params }) =>
  HttpResponse.json({
    id: params.id as string,
    eventType: 'MARATHON',
    eventName: 'Berlin Marathon',
    eventDate: '2024-09-29',
    completion: 'FINISHED',
    elapsedSecs: 14400,
    overallRank: 1500,
    ageGroupRank: null,
    fieldSize: 45000,
    ageGroupFieldSize: null,
    source: 'manual',
    createdAt: '2024-10-01T00:00:00Z',
    splits: [
      { id: 'split-1', splitType: 'RUN', splitLabel: '10km', splitSecs: 2940, cumulativeSecs: 2940 },
      { id: 'split-2', splitType: 'RUN', splitLabel: '21km', splitSecs: 3180, cumulativeSecs: 6120 },
    ],
  })
),
```

- [ ] **Step 4: Run existing tests to confirm nothing broke**

```bash
cd src/Pacevite.Web && npm run test:run
```

Expected: 21 passed (all existing tests still pass).

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Web/src/hooks/useEvents.ts \
        src/Pacevite.Web/src/hooks/useEvent.ts \
        src/Pacevite.Web/src/test/handlers.ts
git commit -m "feat(web): add useEvents/useEvent hooks and MSW handler for event detail"
```

---

## Task 4: Install Recharts + ProgressChart + PbPanel + Dashboard wiring

**Files:**
- Create: `src/Pacevite.Web/src/components/ProgressChart.tsx`
- Create: `src/Pacevite.Web/src/components/PbPanel.tsx`
- Modify: `src/Pacevite.Web/src/pages/DashboardPage.tsx`
- Modify: `src/Pacevite.Web/src/pages/DashboardPage.test.tsx`

- [ ] **Step 1: Install recharts**

```bash
cd src/Pacevite.Web && npm install recharts
```

Expected: `recharts` added to `dependencies` in `package.json`.

- [ ] **Step 2: Write failing component tests**

Add to `src/Pacevite.Web/src/pages/DashboardPage.test.tsx` (inside the existing `describe('DashboardPage', ...)` block):

```typescript
import { http, HttpResponse } from 'msw'
import { server } from '@/test/handlers'

// Add to existing imports at top of file if not already present:
// import { within, waitFor } from '@testing-library/react'

it('shows progress chart panel when events are present', async () => {
  renderDashboard()
  await waitFor(() => {
    expect(screen.getByTestId('progress-chart-panel')).toBeInTheDocument()
  })
})

it('shows empty state in progress chart when no events exist', async () => {
  server.use(
    http.get('http://localhost/api/events', () => HttpResponse.json([])),
    http.get('http://localhost/api/events/personal-bests', () => HttpResponse.json([]))
  )
  renderDashboard()
  await waitFor(() => {
    expect(screen.getByTestId('progress-chart-empty')).toBeInTheDocument()
  })
})

it('shows PB panel with event types when events are present', async () => {
  renderDashboard()
  await waitFor(() => {
    expect(screen.getByTestId('pb-panel')).toBeInTheDocument()
    expect(within(screen.getByTestId('pb-panel')).getByText('MARATHON')).toBeInTheDocument()
  })
})
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
cd src/Pacevite.Web && npm run test:run -- DashboardPage
```

Expected: 3 new tests fail with "Unable to find element by testid".

- [ ] **Step 4: Create ProgressChart component**

Create `src/Pacevite.Web/src/components/ProgressChart.tsx`:

```typescript
import { LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer } from 'recharts'
import type { EventResponse } from '@/lib/types'
import { formatElapsed } from '@/lib/chartUtils'

interface Props {
  events: EventResponse[]
  pbId: string | undefined
}

export function ProgressChart({ events, pbId }: Props) {
  if (events.length === 0) {
    return (
      <p data-testid="progress-chart-empty" className="text-xs text-gray-400 py-8 text-center">
        No events yet
      </p>
    )
  }

  const data = events.map(ev => ({
    date: ev.eventDate,
    secs: ev.elapsedSecs,
    name: ev.eventName,
    id: ev.id,
  }))

  return (
    <div data-testid="progress-chart">
      <ResponsiveContainer width="100%" height={120}>
        <LineChart data={data} margin={{ top: 4, right: 4, bottom: 4, left: 40 }}>
          <XAxis dataKey="date" tick={{ fontSize: 10, fill: '#9ca3af' }} tickLine={false} />
          <YAxis
            tickFormatter={formatElapsed}
            tick={{ fontSize: 10, fill: '#9ca3af' }}
            tickLine={false}
            axisLine={false}
            reversed
            domain={['auto', 'auto']}
          />
          <Tooltip
            formatter={(value: number) => [formatElapsed(value), 'Time']}
            contentStyle={{ background: '#1f2937', border: 'none', fontSize: 12 }}
          />
          <Line
            type="monotone"
            dataKey="secs"
            stroke="#6366f1"
            strokeWidth={2}
            dot={({ cx, cy, payload }: { cx: number; cy: number; payload: { id: string } }) => (
              <circle
                key={`dot-${payload.id}`}
                cx={cx}
                cy={cy}
                r={payload.id === pbId ? 5 : 3}
                fill={payload.id === pbId ? '#4ade80' : '#6366f1'}
              />
            )}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  )
}
```

- [ ] **Step 5: Create PbPanel component**

Create `src/Pacevite.Web/src/components/PbPanel.tsx`:

```typescript
import type { EventResponse } from '@/lib/types'
import { computePbs, groupByEventType, formatElapsed } from '@/lib/chartUtils'

interface Props {
  events: EventResponse[]
  selectedType: string
  onSelectType: (type: string) => void
}

export function PbPanel({ events, selectedType, onSelectType }: Props) {
  const pbs = computePbs(events)
  const grouped = groupByEventType(events)
  const eventTypes = Object.keys(pbs)

  if (eventTypes.length === 0) return null

  return (
    <div data-testid="pb-panel" className="space-y-2">
      {eventTypes.map(type => {
        const pb = pbs[type]
        const eventsOfType = grouped[type] ?? []
        const worst = Math.max(...eventsOfType.map(e => e.elapsedSecs))
        const best = pb.elapsedSecs
        const range = worst - best || 1
        const barPct = Math.round(((worst - best) / range) * 100)

        return (
          <button
            key={type}
            onClick={() => onSelectType(type)}
            className={`w-full flex items-center gap-3 text-left rounded px-2 py-1.5 transition-colors ${
              selectedType === type ? 'bg-gray-100' : 'hover:bg-gray-50'
            }`}
          >
            <span className="text-xs font-medium text-gray-600 w-28 truncate">{type}</span>
            <div className="flex-1 bg-gray-200 rounded-full h-1.5">
              <div
                className="bg-indigo-500 h-1.5 rounded-full"
                style={{ width: `${Math.max(barPct, 10)}%` }}
              />
            </div>
            <span className="text-xs font-semibold text-green-600 w-16 text-right">
              {formatElapsed(best)}
            </span>
          </button>
        )
      })}
    </div>
  )
}
```

- [ ] **Step 6: Update DashboardPage to use useEvents hook and add analytics panels**

Replace the inline events query and add the analytics panels in `src/Pacevite.Web/src/pages/DashboardPage.tsx`:

Add imports at the top:
```typescript
import { useState } from 'react'
import { useEvents } from '@/hooks/useEvents'
import { groupByEventType, computePbs } from '@/lib/chartUtils'
import { ProgressChart } from '@/components/ProgressChart'
import { PbPanel } from '@/components/PbPanel'
import { ChartLine } from 'lucide-react'
```

Remove the existing inline `useQuery` for events:
```typescript
// Remove this block:
const { data: events = [], isLoading: eventsLoading } = useQuery({
  queryKey: ['events'],
  queryFn: async () => {
    const { data } = await apiClient.get<EventResponse[]>('/events')
    return data
  },
})
```

Replace with:
```typescript
const { data: events = [], isLoading: eventsLoading } = useEvents()
const grouped = groupByEventType(events)
const pbs = computePbs(events)
const defaultType = Object.keys(grouped)[0] ?? ''
const [selectedType, setSelectedType] = useState<string>(defaultType)
const chartType = selectedType || defaultType
const chartEvents = grouped[chartType] ?? []
const pbId = pbs[chartType]?.id
```

Remove the `useQuery` import from `@tanstack/react-query` if it's no longer used after removing the inline events query (keep `useMutation` and `useQueryClient`). Remove the unused `apiClient` and `EventResponse` imports if no longer needed directly.

Add the analytics panels section in the JSX, inside `<main>`, before the existing Personal Bests section:

```tsx
{/* Analytics panels */}
{events.length > 0 && (
  <section data-testid="progress-chart-panel">
    <div className="flex items-center justify-between mb-3">
      <h2 className="text-sm font-semibold text-gray-500 uppercase tracking-wide flex items-center gap-2">
        <ChartLine size={14} /> Progress
      </h2>
      {Object.keys(grouped).length > 1 && (
        <select
          value={chartType}
          onChange={e => setSelectedType(e.target.value)}
          className="text-xs border border-gray-200 rounded px-2 py-1 text-gray-600"
        >
          {Object.keys(grouped).map(t => (
            <option key={t} value={t}>{t}</option>
          ))}
        </select>
      )}
    </div>
    <div className="bg-white rounded-lg border border-gray-200 p-4 grid grid-cols-1 lg:grid-cols-2 gap-4">
      <ProgressChart events={chartEvents} pbId={pbId} />
      <PbPanel events={events} selectedType={chartType} onSelectType={setSelectedType} />
    </div>
  </section>
)}

{events.length === 0 && !eventsLoading && (
  <div data-testid="progress-chart-empty" className="hidden" />
)}
```

Also update the event list rows to include a "View" link. In the event map inside the All Events section, add a `Link` alongside the delete button:

```tsx
<Link
  to={`/events/${ev.id}`}
  className="text-xs text-indigo-600 hover:text-indigo-800"
>
  View
</Link>
```

- [ ] **Step 7: Run tests to verify they pass**

```bash
cd src/Pacevite.Web && npm run test:run -- DashboardPage
```

Expected: all dashboard tests pass (including the 3 new ones).

- [ ] **Step 8: Commit**

```bash
git add src/Pacevite.Web/src/components/ProgressChart.tsx \
        src/Pacevite.Web/src/components/PbPanel.tsx \
        src/Pacevite.Web/src/pages/DashboardPage.tsx \
        src/Pacevite.Web/src/pages/DashboardPage.test.tsx \
        src/Pacevite.Web/package.json \
        src/Pacevite.Web/package-lock.json
git commit -m "feat(web): add ProgressChart and PbPanel to Dashboard"
```

---

## Task 5: EventDetailPage — SplitChart + RaceComparison + routing

**Files:**
- Create: `src/Pacevite.Web/src/components/SplitChart.tsx`
- Create: `src/Pacevite.Web/src/components/RaceComparison.tsx`
- Create: `src/Pacevite.Web/src/pages/EventDetailPage.tsx`
- Create: `src/Pacevite.Web/src/pages/EventDetailPage.test.tsx`
- Modify: `src/Pacevite.Web/src/App.tsx`

- [ ] **Step 1: Write failing component tests**

Create `src/Pacevite.Web/src/pages/EventDetailPage.test.tsx`:

```typescript
import { describe, it, expect } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { server } from '@/test/handlers'
import { EventDetailPage } from '@/pages/EventDetailPage'
import { renderWithProviders } from '@/test/render'
import { Route, Routes } from 'react-router-dom'

function renderDetail(id = 'event-1') {
  return renderWithProviders(
    <Routes>
      <Route path="/events/:id" element={<EventDetailPage />} />
    </Routes>,
    { initialEntries: [`/events/${id}`], authenticated: true }
  )
}

describe('EventDetailPage', () => {
  it('renders the event name and date', async () => {
    renderDetail()
    await waitFor(() => {
      expect(screen.getByText('Berlin Marathon')).toBeInTheDocument()
    })
    expect(screen.getByText('2024-09-29')).toBeInTheDocument()
  })

  it('renders the split chart when splits exist', async () => {
    renderDetail()
    await waitFor(() => {
      expect(screen.getByTestId('split-chart')).toBeInTheDocument()
    })
  })

  it('renders the race comparison section', async () => {
    renderDetail()
    await waitFor(() => {
      expect(screen.getByTestId('race-comparison')).toBeInTheDocument()
    })
  })

  it('shows no-splits message when event has no splits', async () => {
    server.use(
      http.get('http://localhost/api/events/:id', () =>
        HttpResponse.json({
          id: 'event-nosplits',
          eventType: 'MARATHON',
          eventName: 'Berlin Marathon',
          eventDate: '2024-09-29',
          completion: 'FINISHED',
          elapsedSecs: 14400,
          overallRank: null,
          ageGroupRank: null,
          fieldSize: null,
          ageGroupFieldSize: null,
          source: 'manual',
          createdAt: '2024-10-01T00:00:00Z',
          splits: [],
        })
      )
    )
    renderDetail('event-nosplits')
    await waitFor(() => {
      expect(screen.getByTestId('split-chart-empty')).toBeInTheDocument()
    })
  })

  it('shows loading state initially', () => {
    renderDetail()
    expect(screen.getByText(/loading/i)).toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd src/Pacevite.Web && npm run test:run -- EventDetailPage
```

Expected: fail with "Cannot find module '@/pages/EventDetailPage'".

- [ ] **Step 3: Create SplitChart component**

Create `src/Pacevite.Web/src/components/SplitChart.tsx`:

```typescript
import { BarChart, Bar, XAxis, YAxis, Tooltip, Cell, ResponsiveContainer, LabelList } from 'recharts'
import type { SplitDelta } from '@/lib/chartUtils'
import { formatElapsed } from '@/lib/chartUtils'

interface Props {
  deltas: SplitDelta[]
}

export function SplitChart({ deltas }: Props) {
  if (deltas.length === 0) {
    return (
      <p data-testid="split-chart-empty" className="text-xs text-gray-400 py-8 text-center">
        No split data for this event
      </p>
    )
  }

  const data = deltas.map(d => ({
    label: d.label,
    secs: d.secs,
    delta: d.delta,
    faster: d.faster,
    deltaLabel: d.delta === 0 ? '—' : `${d.delta > 0 ? '+' : ''}${formatElapsed(Math.abs(d.delta))}`,
  }))

  return (
    <div data-testid="split-chart">
      <ResponsiveContainer width="100%" height={160}>
        <BarChart data={data} margin={{ top: 16, right: 8, bottom: 4, left: 40 }}>
          <XAxis dataKey="label" tick={{ fontSize: 10, fill: '#9ca3af' }} tickLine={false} />
          <YAxis
            tickFormatter={formatElapsed}
            tick={{ fontSize: 10, fill: '#9ca3af' }}
            tickLine={false}
            axisLine={false}
            domain={['auto', 'auto']}
          />
          <Tooltip
            formatter={(value: number) => [formatElapsed(value), 'Split time']}
            contentStyle={{ background: '#1f2937', border: 'none', fontSize: 12 }}
          />
          <Bar dataKey="secs" radius={[3, 3, 0, 0]}>
            {data.map((entry, index) => (
              <Cell key={`cell-${index}`} fill={entry.faster ? '#4ade80' : '#f87171'} />
            ))}
            <LabelList
              dataKey="deltaLabel"
              position="top"
              style={{ fontSize: 10, fill: '#6b7280' }}
            />
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  )
}
```

- [ ] **Step 4: Create RaceComparison component**

Create `src/Pacevite.Web/src/components/RaceComparison.tsx`:

```typescript
import { LineChart, Line, Tooltip, ResponsiveContainer, ReferenceDot } from 'recharts'
import type { EventResponse } from '@/lib/types'
import { formatElapsed } from '@/lib/chartUtils'

interface Props {
  event: EventResponse
  sameTypeEvents: EventResponse[]
}

export function RaceComparison({ event, sameTypeEvents }: Props) {
  if (sameTypeEvents.length < 2) return null

  const sorted = [...sameTypeEvents].sort((a, b) => a.eventDate.localeCompare(b.eventDate))
  const avg = Math.round(sorted.reduce((s, e) => s + e.elapsedSecs, 0) / sorted.length)
  const delta = event.elapsedSecs - avg
  const best = Math.min(...sorted.map(e => e.elapsedSecs))
  const worst = Math.max(...sorted.map(e => e.elapsedSecs))

  const sparkData = sorted.map(e => ({ date: e.eventDate, secs: e.elapsedSecs, id: e.id }))

  return (
    <div data-testid="race-comparison" className="space-y-3">
      <div className="text-center">
        <p className={`text-3xl font-bold ${delta < 0 ? 'text-green-600' : 'text-red-500'}`}>
          {delta < 0 ? '−' : '+'}{formatElapsed(Math.abs(delta))}
        </p>
        <p className="text-xs text-gray-500 mt-1">
          {delta < 0 ? 'faster' : 'slower'} than your avg ({formatElapsed(avg)})
        </p>
      </div>

      <ResponsiveContainer width="100%" height={60}>
        <LineChart data={sparkData} margin={{ top: 4, right: 4, bottom: 4, left: 4 }}>
          <Tooltip
            formatter={(value: number) => [formatElapsed(value), 'Time']}
            contentStyle={{ background: '#1f2937', border: 'none', fontSize: 11 }}
          />
          <Line
            type="monotone"
            dataKey="secs"
            stroke="#9ca3af"
            strokeWidth={1.5}
            dot={({ cx, cy, payload }: { cx: number; cy: number; payload: { id: string } }) => (
              <circle
                key={`spark-dot-${payload.id}`}
                cx={cx}
                cy={cy}
                r={payload.id === event.id ? 5 : 3}
                fill={payload.id === event.id ? '#4ade80' : '#6b7280'}
              />
            )}
          />
        </LineChart>
      </ResponsiveContainer>

      <div className="flex justify-between text-xs text-gray-500 pt-1 border-t border-gray-100">
        <span>Best <strong className="text-green-600">{formatElapsed(best)}</strong></span>
        <span>Worst <strong className="text-gray-700">{formatElapsed(worst)}</strong></span>
      </div>
    </div>
  )
}
```

- [ ] **Step 5: Create EventDetailPage**

Create `src/Pacevite.Web/src/pages/EventDetailPage.tsx`:

```typescript
import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useEvent } from '@/hooks/useEvent'
import { apiClient } from '@/lib/api'
import { computeAverageSplits, computeSplitDeltas, formatElapsed } from '@/lib/chartUtils'
import { SplitChart } from '@/components/SplitChart'
import { RaceComparison } from '@/components/RaceComparison'
import type { EventResponse } from '@/lib/types'

export function EventDetailPage() {
  const { id } = useParams<{ id: string }>()

  const { data: event, isLoading } = useEvent(id!)

  const { data: allEvents = [] } = useQuery({
    queryKey: ['events'],
    queryFn: async () => {
      const { data } = await apiClient.get<EventResponse[]>('/events')
      return data
    },
  })

  if (isLoading) return <p className="p-8 text-gray-500">Loading…</p>
  if (!event) return <p className="p-8 text-gray-500">Event not found.</p>

  const sameTypeEvents = allEvents.filter(e => e.eventType === event.eventType)
  const avgSplits = computeAverageSplits(sameTypeEvents)
  const splitDeltas = computeSplitDeltas(event, avgSplits)

  return (
    <div className="min-h-screen bg-gray-50">
      <header className="bg-white border-b border-gray-200 px-6 py-4 flex items-center justify-between">
        <h1 className="text-lg font-semibold text-gray-900">Pacevite</h1>
        <Link to="/dashboard" className="text-sm text-indigo-600 hover:text-indigo-800">
          ← Dashboard
        </Link>
      </header>

      <main className="max-w-4xl mx-auto px-6 py-8 space-y-6">
        {/* Header */}
        <div>
          <p className="text-xs uppercase tracking-widest text-gray-400 mb-1">
            {event.eventType} · {event.eventDate}
          </p>
          <h2 className="text-2xl font-bold text-gray-900">{event.eventName}</h2>
          <div className="flex gap-6 mt-2 text-sm text-gray-500">
            <span>Time: <strong className="text-gray-900">{formatElapsed(event.elapsedSecs)}</strong></span>
            {event.overallRank && event.fieldSize && (
              <span>Overall: <strong className="text-gray-900">#{event.overallRank} / {event.fieldSize}</strong></span>
            )}
            {event.ageGroupRank && event.ageGroupFieldSize && (
              <span>Age group: <strong className="text-gray-900">#{event.ageGroupRank} / {event.ageGroupFieldSize}</strong></span>
            )}
          </div>
        </div>

        {/* Charts row */}
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          <div className="bg-white rounded-lg border border-gray-200 p-4">
            <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-3">
              Split Breakdown
            </h3>
            <SplitChart deltas={splitDeltas} />
          </div>

          <div className="bg-white rounded-lg border border-gray-200 p-4">
            <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-3">
              vs. Your {event.eventType} Average
            </h3>
            <RaceComparison event={event} sameTypeEvents={sameTypeEvents} />
          </div>
        </div>
      </main>
    </div>
  )
}
```

- [ ] **Step 6: Add route in App.tsx**

In `src/Pacevite.Web/src/App.tsx`, add the import and the route:

Add to imports:
```typescript
import { EventDetailPage } from '@/pages/EventDetailPage'
```

Add inside the router array, after the `/upload` route:
```typescript
{
  path: '/events/:id',
  element: (
    <AuthGuard>
      <EventDetailPage />
    </AuthGuard>
  ),
},
```

- [ ] **Step 7: Run tests to verify they pass**

```bash
cd src/Pacevite.Web && npm run test:run
```

Expected: all tests pass (21 existing + 5 new = 26 passing).

- [ ] **Step 8: Commit**

```bash
git add src/Pacevite.Web/src/components/SplitChart.tsx \
        src/Pacevite.Web/src/components/RaceComparison.tsx \
        src/Pacevite.Web/src/pages/EventDetailPage.tsx \
        src/Pacevite.Web/src/pages/EventDetailPage.test.tsx \
        src/Pacevite.Web/src/App.tsx
git commit -m "feat(web): add EventDetailPage with SplitChart and RaceComparison"
```

---

## Task 6: E2E tests

**Files:**
- Create: `src/Pacevite.Web/e2e/fixtures/events-with-splits.json`
- Modify: `src/Pacevite.Web/e2e/dashboard.spec.ts`
- Create: `src/Pacevite.Web/e2e/event-detail.spec.ts`

- [ ] **Step 1: Create the E2E fixture with splits**

Create `src/Pacevite.Web/e2e/fixtures/events-with-splits.json`:

```json
[
  {
    "event_type": "HALF_MARATHON",
    "event_name": "Brighton Half Marathon",
    "event_date": "2024-03-17",
    "completion": "FINISHED",
    "elapsed_secs": 6443,
    "splits": [
      { "split_type": "RUN", "split_label": "5km",    "split_secs": 1450, "cumulative_secs": 1450 },
      { "split_type": "RUN", "split_label": "10km",   "split_secs": 1471, "cumulative_secs": 2921 },
      { "split_type": "RUN", "split_label": "15km",   "split_secs": 1518, "cumulative_secs": 4439 },
      { "split_type": "RUN", "split_label": "Finish", "split_secs": 2004, "cumulative_secs": 6443 }
    ]
  }
]
```

- [ ] **Step 2: Extend dashboard E2E spec to assert chart panels**

In `src/Pacevite.Web/e2e/dashboard.spec.ts`, add a new test after the existing delete test:

```typescript
test('shows analytics panels after uploading events', async ({ page }) => {
  const email = uniqueEmail()
  await registerViaApi(email)
  await loginViaUi(page, email)

  // Upload an event
  await page.click('a[href="/upload"]')
  await page.waitForURL('/upload')
  const csvPath = path.join(__dirname, 'fixtures/events.csv')
  await page.setInputFiles('input[type="file"]', csvPath)
  await page.waitForFunction(() => {
    const btn = document.querySelector<HTMLButtonElement>('button[type="submit"]')
    return btn !== null && !btn.disabled
  })
  await page.click('button[type="submit"]')
  await page.waitForURL('/dashboard')

  // Analytics panels should be visible
  await expect(page.getByTestId('progress-chart-panel')).toBeVisible()
  await expect(page.getByTestId('pb-panel')).toBeVisible()
})
```

- [ ] **Step 3: Create event-detail E2E spec**

Create `src/Pacevite.Web/e2e/event-detail.spec.ts`:

```typescript
import { test, expect } from '@playwright/test'
import path from 'path'
import { fileURLToPath } from 'url'
import { uniqueEmail, registerViaApi, loginViaUi } from './helpers'

const __dirname = path.dirname(fileURLToPath(import.meta.url))

test('clicking View on an event navigates to the detail page', async ({ page }) => {
  const email = uniqueEmail()
  await registerViaApi(email)
  await loginViaUi(page, email)

  // Upload an event with splits
  await page.click('a[href="/upload"]')
  await page.waitForURL('/upload')
  const jsonPath = path.join(__dirname, 'fixtures/events-with-splits.json')
  await page.setInputFiles('input[type="file"]', jsonPath)
  await page.waitForFunction(() => {
    const btn = document.querySelector<HTMLButtonElement>('button[type="submit"]')
    return btn !== null && !btn.disabled
  })
  await page.click('button[type="submit"]')
  await page.waitForURL('/dashboard')

  // Click View on the uploaded event
  await page.getByText('View').first().click()
  await page.waitForURL(/\/events\/.+/)

  // Assert detail page content
  await expect(page.getByText('Brighton Half Marathon')).toBeVisible()
  await expect(page.getByText('2024-03-17')).toBeVisible()
  await expect(page.getByTestId('split-chart')).toBeVisible()
})

test('back link on detail page returns to dashboard', async ({ page }) => {
  const email = uniqueEmail()
  await registerViaApi(email)
  await loginViaUi(page, email)

  // Upload and navigate to detail
  await page.click('a[href="/upload"]')
  await page.waitForURL('/upload')
  const jsonPath = path.join(__dirname, 'fixtures/events-with-splits.json')
  await page.setInputFiles('input[type="file"]', jsonPath)
  await page.waitForFunction(() => {
    const btn = document.querySelector<HTMLButtonElement>('button[type="submit"]')
    return btn !== null && !btn.disabled
  })
  await page.click('button[type="submit"]')
  await page.waitForURL('/dashboard')
  await page.getByText('View').first().click()
  await page.waitForURL(/\/events\/.+/)

  // Click back
  await page.getByText('← Dashboard').click()
  await page.waitForURL('/dashboard')
  await expect(page.getByText('Brighton Half Marathon')).toBeVisible()
})
```

- [ ] **Step 4: Run all E2E tests**

```bash
cd src/Pacevite.Web && npm run test:e2e
```

Expected: all 7 E2E tests pass (5 existing + 2 new).

- [ ] **Step 5: Run full test suite**

```bash
# Backend
dotnet run --project tests/Pacevite.Api.Tests

# Frontend unit
cd src/Pacevite.Web && npm run test:run

# E2E
cd src/Pacevite.Web && npm run test:e2e
```

Expected: 37 .NET + 26 Vitest + 7 Playwright = 70 tests passing.

- [ ] **Step 6: Commit**

```bash
git add src/Pacevite.Web/e2e/fixtures/events-with-splits.json \
        src/Pacevite.Web/e2e/dashboard.spec.ts \
        src/Pacevite.Web/e2e/event-detail.spec.ts
git commit -m "test(web): add E2E specs for analytics panels and event detail page"
```
