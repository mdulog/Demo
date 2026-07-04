# Server-Side Events Pagination Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the fetch-everything `GET /api/events` with keyset-cursor pagination + server-side search, and give the chart consumers purpose-built read models (`/events/timeline`, `averageSplits` on event detail) so no page needs the full event list in browser memory.

**Architecture:** Keyset pagination on `(EventDate DESC, Id DESC)` with an opaque base64url cursor; list items exclude splits. A lightweight timeline endpoint feeds `ProgressChart`/`PbPanel`/`PredictionTeaser`/`PredictPage`/`RaceComparison`; the event-detail response gains SQL-computed per-label average splits, replacing client-side `computeAverageSplits`. Frontend moves to `useInfiniteQuery` with a debounced server-side search box.

**Tech Stack:** .NET 10 / EF Core 10 / Npgsql / Mediator (source-gen) / FluentValidation / TUnit + Testcontainers · React 19 / TanStack Query v5 / MSW v2 / Vitest / Playwright

**Spec:** `docs/specs/2026-07-04-events-pagination-design.md`

## Global Constraints

- Use `podman`, not `docker` (`podman compose up -d db` must be running for migrations and is auto-handled by Testcontainers for tests).
- .NET tests run via `dotnet run --project tests/Pacevite.Api.Tests` — **`dotnet test` does not work** (TUnit on Microsoft.Testing.Platform). Category filter: `-- --treenode-filter "/*/*/*/*[Category=Unit]"` (exactly 4 wildcards).
- Mediator is source-generated: after adding a new `IQuery`/handler, run `dotnet build` before expecting it to be discoverable.
- Enum-ish strings in API responses are UPPERCASE (`"MARATHON"`, `"FINISHED"`) — `EventMapper` uppercases; keep that convention.
- EF migrations use the 3-step `/ef-migrate` workflow (add migration → database update → `dbcontext optimize` to regenerate the AOT compiled model into `Infrastructure/Persistence/Compiled`).
- Handlers wrap their body in try/catch → `LogCritical` with method name + identifiers → rethrow. Structured logging with named holes only. No user emails/PII in logs.
- Magic numbers become named constants (`0`, `1`, self-evident sizes exempt).
- Frontend tests: `cd src/Pacevite.Web && npm test`. E2E: `cd src/Pacevite.Web && npm run test:e2e`.
- New Testcontainers fixtures must use `new PostgreSqlBuilder("postgres:17")` (constructor with image — the parameterless one is obsolete).
- Commit after every green task. Commit messages end with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: EventCursor codec

The opaque keyset cursor: encodes `(EventDate, Id)` as base64url of `"yyyy-MM-dd|<guid>"`. Pure, static-free, unit-testable in isolation.

**Files:**
- Create: `src/Pacevite.Api/Features/Events/GetEvents/EventCursor.cs`
- Test: `tests/Pacevite.Api.Tests/Unit/Events/EventCursorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `readonly record struct EventCursor(DateOnly EventDate, Guid Id)` with `string Encode()` and `static bool TryDecode(string? value, out EventCursor cursor)`. Task 2's validator and handler call both.

- [ ] **Step 1: Write the failing tests**

Create `tests/Pacevite.Api.Tests/Unit/Events/EventCursorTests.cs`:

```csharp
using Pacevite.Api.Features.Events.GetEvents;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Events;

[Category("Unit")]
public sealed class EventCursorTests
{
    [Test]
    public async Task Encode_then_TryDecode_round_trips_date_and_id()
    {
        // Arrange
        var original = new EventCursor(new DateOnly(2024, 9, 29), Guid.Parse("6f1a2b3c-4d5e-6f70-8192-a3b4c5d6e7f8"));

        // Act
        var encoded = original.Encode();
        var decoded = EventCursor.TryDecode(encoded, out var cursor);

        // Assert
        await Assert.That(decoded).IsTrue();
        await Assert.That(cursor).IsEqualTo(original);
    }

    [Test]
    public async Task Encode_produces_url_safe_token_without_padding_characters()
    {
        // Arrange
        var original = new EventCursor(new DateOnly(2026, 1, 1), Guid.NewGuid());

        // Act
        var encoded = original.Encode();

        // Assert — base64url alphabet only: no '+', '/', or '='
        await Assert.That(encoded.Contains('+')).IsFalse();
        await Assert.That(encoded.Contains('/')).IsFalse();
        await Assert.That(encoded.Contains('=')).IsFalse();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("not-base64!!!")]
    [Arguments("aGVsbG8")] // decodes to "hello" — no separator
    [Arguments("MjAyNC0wOS0yOXxub3QtYS1ndWlk")] // "2024-09-29|not-a-guid"
    [Arguments("bm90LWEtZGF0ZXw2ZjFhMmIzYy00ZDVlLTZmNzAtODE5Mi1hM2I0YzVkNmU3Zjg")] // "not-a-date|<guid>"
    public async Task TryDecode_returns_false_for_malformed_input(string? value)
    {
        // Act
        var decoded = EventCursor.TryDecode(value, out _);

        // Assert
        await Assert.That(decoded).IsFalse();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/EventCursorTests/*"`
Expected: build FAILS with "The type or namespace name 'EventCursor' could not be found".

- [ ] **Step 3: Write the implementation**

Create `src/Pacevite.Api/Features/Events/GetEvents/EventCursor.cs`:

```csharp
using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Pacevite.Api.Features.Events.GetEvents;

// Opaque keyset-pagination cursor for GET /api/events.
// Wire format: base64url("yyyy-MM-dd|<guid>") — clients must treat it as opaque.
public readonly record struct EventCursor(DateOnly EventDate, Guid Id)
{
    private const char Separator = '|';
    private const string DateFormat = "yyyy-MM-dd";

    public string Encode()
    {
        var raw = $"{EventDate.ToString(DateFormat, CultureInfo.InvariantCulture)}{Separator}{Id}";
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(raw));
    }

    public static bool TryDecode(string? value, out EventCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(value);
        }
        catch (FormatException)
        {
            // TryDecode contract: malformed input is a 'false' result, not an exception.
            return false;
        }

        var parts = Encoding.UTF8.GetString(bytes).Split(Separator);
        if (parts.Length != 2)
            return false;

        if (!DateOnly.TryParseExact(parts[0], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;

        if (!Guid.TryParse(parts[1], out var id))
            return false;

        cursor = new EventCursor(date, id);
        return true;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/EventCursorTests/*"`
Expected: all tests PASS (9 test cases: 2 named + 7 parameterized).

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Api/Features/Events/GetEvents/EventCursor.cs tests/Pacevite.Api.Tests/Unit/Events/EventCursorTests.cs
git commit -m "feat: add opaque keyset cursor codec for events pagination

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Paginated GET /api/events (contract, validator, handler, endpoint)

Changes the list contract to `{ items, nextCursor }` with splitless summaries, server-side ILIKE search, and the keyset predicate. Updates the existing integration tests that consume the old array shape.

**Files:**
- Create: `src/Pacevite.Api/Contracts/Responses/PagedEventsResponse.cs`
- Modify: `src/Pacevite.Api/Features/Events/GetEvents/GetEventsQuery.cs`
- Modify: `src/Pacevite.Api/Features/Events/GetEvents/GetEventsValidator.cs`
- Modify: `src/Pacevite.Api/Features/Events/GetEvents/GetEventsHandler.cs`
- Modify: `src/Pacevite.Api/Features/Events/EventMapper.cs`
- Modify: `src/Pacevite.Api/Features/Events/EventEndpoints.cs:83-94`
- Test: `tests/Pacevite.Api.Tests/Unit/Events/GetEventsValidatorTests.cs` (create)
- Test: `tests/Pacevite.Api.Tests/Integration/GetEventsPaginationTests.cs` (create)
- Test: `tests/Pacevite.Api.Tests/Integration/EventEndpointsTests.cs` (modify — 5 tests consume the old shape)
- Test: `tests/Pacevite.Api.Tests/Integration/SyncEndpointsTests.cs` (modify if it reads `GET /api/events` as a list — verify in Step 6)

**Interfaces:**
- Consumes: `EventCursor.Encode()` / `EventCursor.TryDecode(string?, out EventCursor)` from Task 1.
- Produces: `PagedEventsResponse(IReadOnlyList<EventSummaryResponse> Items, string? NextCursor)`; `EventSummaryResponse` (all `EventResponse` fields except `Splits`); `GetEventsQuery` gains `string? Search`, `string? Cursor`, `int Limit` (const `DefaultLimit = 20`, `MaxLimit = 100`); `EventMapper.ToSummaryResponse(Event)`. Tasks 7–8 consume the JSON shape (camelCase: `items`, `nextCursor`).

- [ ] **Step 1: Write the failing validator unit tests**

Create `tests/Pacevite.Api.Tests/Unit/Events/GetEventsValidatorTests.cs`:

```csharp
using Pacevite.Api.Features.Events.GetEvents;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Events;

[Category("Unit")]
public sealed class GetEventsValidatorTests
{
    private readonly GetEventsValidator _validator = new();

    [Test]
    public async Task passes_for_default_query_with_only_user_id()
    {
        // Arrange
        var query = new GetEventsQuery("user-42");

        // Act
        var result = await _validator.ValidateAsync(query);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(101)]
    public async Task fails_when_limit_is_out_of_bounds(int limit)
    {
        // Arrange
        var query = new GetEventsQuery("user-42", Limit: limit);

        // Act
        var result = await _validator.ValidateAsync(query);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    [Arguments(1)]
    [Arguments(20)]
    [Arguments(100)]
    public async Task passes_when_limit_is_within_bounds(int limit)
    {
        // Arrange
        var query = new GetEventsQuery("user-42", Limit: limit);

        // Act
        var result = await _validator.ValidateAsync(query);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task fails_when_cursor_is_malformed()
    {
        // Arrange
        var query = new GetEventsQuery("user-42", Cursor: "not-a-real-cursor!!!");

        // Act
        var result = await _validator.ValidateAsync(query);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task passes_when_cursor_is_well_formed()
    {
        // Arrange
        var cursor = new EventCursor(new DateOnly(2024, 9, 29), Guid.NewGuid()).Encode();
        var query = new GetEventsQuery("user-42", Cursor: cursor);

        // Act
        var result = await _validator.ValidateAsync(query);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task fails_when_search_exceeds_max_length()
    {
        // Arrange
        var query = new GetEventsQuery("user-42", Search: new string('a', 101));

        // Act
        var result = await _validator.ValidateAsync(query);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }
}
```

- [ ] **Step 2: Write the failing integration tests**

Create `tests/Pacevite.Api.Tests/Integration/GetEventsPaginationTests.cs`. It reuses the fixture pattern from `EventEndpointsTests.cs` verbatim (Testcontainers Postgres + WebApplicationFactory + register-for-token). Events are seeded via CSV upload; distinct dates make ordering deterministic.

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
public sealed class GetEventsPaginationTests
{
    private PostgreSqlContainer _postgres = null!;
    private HttpClient _client = null!;
    private WebApplicationFactory<Program> _factory = null!;

    [Before(Test)]
    public async Task SetUpAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:17")
            .WithDatabase("pacevite_pagination_test")
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
        var regResponse = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "P@ssword1!"));
        var body = await regResponse.Content.ReadFromJsonAsync<AuthResponse>();
        return body!.Token;
    }

    private async Task UploadCsvAsync(string csv)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "events.csv");
        var response = await _client.PostAsync("/api/events/upload", content);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    // 5 marathons on distinct descending-friendly dates.
    private const string FiveEventsCsv = """
        MARATHON,Race One,2024-01-10,FINISHED,15000
        MARATHON,Race Two,2024-03-10,FINISHED,14800
        MARATHON,Berlin Marathon,2024-06-10,FINISHED,14600
        MARATHON,Race Four,2024-09-10,FINISHED,14400
        MARATHON,Race Five,2024-12-10,FINISHED,14200
        """;

    [Test]
    public async Task page_walk_visits_every_event_exactly_once_in_descending_date_order()
    {
        // Arrange
        var token = await GetTokenAsync("walk@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await UploadCsvAsync(FiveEventsCsv);

        // Act — walk with limit=2 until nextCursor is null
        var seen = new List<EventSummaryResponse>();
        string? cursor = null;
        do
        {
            var url = cursor is null ? "/api/events?limit=2" : $"/api/events?limit=2&cursor={cursor}";
            var page = await _client.GetFromJsonAsync<PagedEventsResponse>(url);
            seen.AddRange(page!.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);

        // Assert
        await Assert.That(seen.Count).IsEqualTo(5);
        await Assert.That(seen.Select(e => e.Id).Distinct().Count()).IsEqualTo(5);
        await Assert.That(seen[0].EventName).IsEqualTo("Race Five");
        await Assert.That(seen[4].EventName).IsEqualTo("Race One");
    }

    [Test]
    public async Task inserting_an_older_event_between_pages_causes_no_skip_or_duplicate()
    {
        // Arrange — the reason keyset was chosen over OFFSET
        var token = await GetTokenAsync("stability@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await UploadCsvAsync(FiveEventsCsv);

        // Act — fetch page 1, then backfill an OLD event (like a Strava import), then continue
        var page1 = await _client.GetFromJsonAsync<PagedEventsResponse>("/api/events?limit=2");
        await UploadCsvAsync("MARATHON,Backfilled Race,2023-05-05,FINISHED,15500");
        var seen = new List<EventSummaryResponse>(page1!.Items);
        var cursor = page1.NextCursor;
        while (cursor is not null)
        {
            var page = await _client.GetFromJsonAsync<PagedEventsResponse>($"/api/events?limit=2&cursor={cursor}");
            seen.AddRange(page!.Items);
            cursor = page.NextCursor;
        }

        // Assert — all 5 originals exactly once, plus the backfill (older than the cursor position, so included)
        await Assert.That(seen.Select(e => e.Id).Distinct().Count()).IsEqualTo(seen.Count);
        await Assert.That(seen.Count).IsEqualTo(6);
        await Assert.That(seen.Any(e => e.EventName == "Backfilled Race")).IsTrue();
    }

    [Test]
    public async Task search_matches_event_name_case_insensitively()
    {
        // Arrange
        var token = await GetTokenAsync("search@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await UploadCsvAsync(FiveEventsCsv);

        // Act
        var page = await _client.GetFromJsonAsync<PagedEventsResponse>("/api/events?search=berlin");

        // Assert
        await Assert.That(page!.Items.Count).IsEqualTo(1);
        await Assert.That(page.Items[0].EventName).IsEqualTo("Berlin Marathon");
    }

    [Test]
    public async Task search_treats_like_wildcards_as_literals()
    {
        // Arrange
        var token = await GetTokenAsync("wildcard@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await UploadCsvAsync("""
            MARATHON,50% Effort Run,2024-02-01,FINISHED,16000
            MARATHON,Full Effort Run,2024-02-02,FINISHED,15000
            """);

        // Act — '%' must match only the literal percent sign, not act as a wildcard
        var page = await _client.GetFromJsonAsync<PagedEventsResponse>("/api/events?search=50%25");

        // Assert
        await Assert.That(page!.Items.Count).IsEqualTo(1);
        await Assert.That(page.Items[0].EventName).IsEqualTo("50% Effort Run");
    }

    [Test]
    public async Task search_composes_with_event_type_filter_and_cursor()
    {
        // Arrange
        var token = await GetTokenAsync("compose@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await UploadCsvAsync("""
            MARATHON,City Run A,2024-01-10,FINISHED,15000
            MARATHON,City Run B,2024-03-10,FINISHED,14800
            MARATHON,City Run C,2024-06-10,FINISHED,14600
            HYROX,City Run HYROX,2024-07-10,FINISHED,5400
            """);

        // Act — filter+search page 1 then page 2
        var page1 = await _client.GetFromJsonAsync<PagedEventsResponse>("/api/events?eventType=MARATHON&search=City&limit=2");
        var page2 = await _client.GetFromJsonAsync<PagedEventsResponse>($"/api/events?eventType=MARATHON&search=City&limit=2&cursor={page1!.NextCursor}");

        // Assert
        await Assert.That(page1.Items.Count).IsEqualTo(2);
        await Assert.That(page2!.Items.Count).IsEqualTo(1);
        await Assert.That(page2.NextCursor).IsNull();
        await Assert.That(page1.Items.Concat(page2.Items).All(e => e.EventType == "MARATHON")).IsTrue();
    }

    [Test]
    public async Task limit_above_maximum_returns_400()
    {
        // Arrange
        var token = await GetTokenAsync("limit@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/events?limit=101");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task malformed_cursor_returns_400()
    {
        // Arrange
        var token = await GetTokenAsync("cursor@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/events?cursor=garbage!!!");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task list_items_do_not_include_splits_property()
    {
        // Arrange
        var token = await GetTokenAsync("splitless@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await UploadCsvAsync("MARATHON,Splitless Check,2024-01-10,FINISHED,15000");

        // Act
        var raw = await _client.GetStringAsync("/api/events");

        // Assert — the summary payload must not carry the splits array at all
        await Assert.That(raw.Contains("\"splits\"")).IsFalse();
    }

    [Test]
    public async Task pagination_is_scoped_to_the_authenticated_user()
    {
        // Arrange — user A has events; user B must see an empty page, not A's data
        var tokenA = await GetTokenAsync("owner-a@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        await UploadCsvAsync(FiveEventsCsv);

        var tokenB = await GetTokenAsync("other-b@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);

        // Act
        var page = await _client.GetFromJsonAsync<PagedEventsResponse>("/api/events");

        // Assert
        await Assert.That(page!.Items.Count).IsEqualTo(0);
        await Assert.That(page.NextCursor).IsNull();
    }
}
```

- [ ] **Step 3: Run new tests to verify they fail**

Run: `dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/GetEventsValidatorTests/*"`
Expected: build FAILS ("'GetEventsQuery' does not contain a definition for 'Limit'").

- [ ] **Step 4: Implement the contract + validator + handler + endpoint**

Create `src/Pacevite.Api/Contracts/Responses/PagedEventsResponse.cs`:

```csharp
namespace Pacevite.Api.Contracts.Responses;

// Splitless list item — splits are only served by GET /api/events/{id}.
public sealed record EventSummaryResponse(
    Guid Id,
    string EventType,
    string EventName,
    DateOnly EventDate,
    string Completion,
    int ElapsedSecs,
    int? OverallRank,
    int? AgeGroupRank,
    int? FieldSize,
    int? AgeGroupFieldSize,
    string Source,
    bool NeedsEnrichment,
    DateTimeOffset CreatedAt);

public sealed record PagedEventsResponse(
    IReadOnlyList<EventSummaryResponse> Items,
    string? NextCursor);
```

Replace the record in `src/Pacevite.Api/Features/Events/GetEvents/GetEventsQuery.cs`:

```csharp
using Mediator;
using Pacevite.Api.Contracts.Responses;

namespace Pacevite.Api.Features.Events.GetEvents;

public sealed record GetEventsQuery(
    string UserId,
    string? EventType = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? Search = null,
    string? Cursor = null,
    int Limit = GetEventsQuery.DefaultLimit) : IQuery<PagedEventsResponse>
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;
    public const int MaxSearchLength = 100;
}
```

Add to the constructor body in `src/Pacevite.Api/Features/Events/GetEvents/GetEventsValidator.cs` (after the existing `From <= To` rule):

```csharp
RuleFor(x => x.Limit)
    .InclusiveBetween(1, GetEventsQuery.MaxLimit)
    .WithMessage($"Limit must be between 1 and {GetEventsQuery.MaxLimit}.");

RuleFor(x => x.Search)
    .MaximumLength(GetEventsQuery.MaxSearchLength);

RuleFor(x => x.Cursor)
    .Must(c => EventCursor.TryDecode(c, out _))
    .When(x => x.Cursor is not null)
    .WithMessage("Cursor is malformed.");
```

Add to `src/Pacevite.Api/Features/Events/EventMapper.cs` (below `ToResponse`):

```csharp
internal static EventSummaryResponse ToSummaryResponse(Event ev) => new(
    ev.Id,
    ev.EventType.ToString().ToUpperInvariant(),
    ev.EventName,
    ev.EventDate,
    ev.Completion.ToString().ToUpperInvariant(),
    ev.ElapsedSecs,
    ev.OverallRank,
    ev.AgeGroupRank,
    ev.FieldSize,
    ev.AgeGroupFieldSize,
    ev.Source,
    ev.NeedsEnrichment,
    ev.CreatedAt);
```

Replace the body of `src/Pacevite.Api/Features/Events/GetEvents/GetEventsHandler.cs`:

```csharp
using Mediator;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Features.Events.GetEvents;

public sealed class GetEventsHandler(AppDbContext db, ILogger<GetEventsHandler> logger)
    : IQueryHandler<GetEventsQuery, PagedEventsResponse>
{
    public async ValueTask<PagedEventsResponse> Handle(
        GetEventsQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var q = db.Events.Where(e => e.UserId == query.UserId);

            if (query.EventType is not null && Enum.TryParse<EventType>(query.EventType, ignoreCase: true, out var eventType))
                q = q.Where(e => e.EventType == eventType);

            if (query.From.HasValue)
                q = q.Where(e => e.EventDate >= query.From.Value);

            if (query.To.HasValue)
                q = q.Where(e => e.EventDate <= query.To.Value);

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var pattern = $"%{EscapeLikePattern(query.Search)}%";
                q = q.Where(e => EF.Functions.ILike(e.EventName, pattern, @"\"));
            }

            if (EventCursor.TryDecode(query.Cursor, out var cursor))
            {
                q = q.Where(e => e.EventDate < cursor.EventDate
                    || (e.EventDate == cursor.EventDate && e.Id.CompareTo(cursor.Id) < 0));
            }

            // Fetch one extra row to know whether a next page exists without a COUNT query.
            var events = await q
                .OrderByDescending(e => e.EventDate)
                .ThenByDescending(e => e.Id)
                .Take(query.Limit + 1)
                .ToListAsync(cancellationToken);

            var hasMore = events.Count > query.Limit;
            var page = hasMore ? events[..query.Limit] : events;
            var last = page.Count > 0 ? page[^1] : null;
            var nextCursor = hasMore && last is not null
                ? new EventCursor(last.EventDate, last.Id).Encode()
                : null;

            return new PagedEventsResponse(
                page.Select(EventMapper.ToSummaryResponse).ToList(),
                nextCursor);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "GetEventsHandler failed for {UserId}", query.UserId);
            throw;
        }
    }

    // ILIKE treats % and _ as wildcards; escape them (and the escape char itself)
    // so user input always matches literally.
    private static string EscapeLikePattern(string input) =>
        input.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
```

Replace `GetEventsAsync` in `src/Pacevite.Api/Features/Events/EventEndpoints.cs:83-94`:

```csharp
private static async Task<Ok<PagedEventsResponse>> GetEventsAsync(
    ClaimsPrincipal user,
    IMediator mediator,
    CancellationToken ct,
    string? eventType = null,
    DateOnly? from = null,
    DateOnly? to = null,
    string? search = null,
    string? cursor = null,
    int limit = GetEventsQuery.DefaultLimit)
{
    var userId = GetUserId(user);
    var result = await mediator.Send(
        new GetEventsQuery(userId, eventType, from, to, search, cursor, limit), ct);
    return TypedResults.Ok(result);
}
```

Run: `dotnet build` (required — Mediator regenerates the handler registration for the changed response type).
Expected: build succeeds with 0 errors **except** in `tests/` (fixed next step).

- [ ] **Step 5: Update the existing integration tests that consume the old array shape**

In `tests/Pacevite.Api.Tests/Integration/EventEndpointsTests.cs`, apply this transformation to every `GET /api/events` list read — the pattern is:

```csharp
// OLD
var events = await response.Content.ReadFromJsonAsync<List<EventResponse>>();
await Assert.That(events!.Count).IsEqualTo(1);

// NEW
var page = await response.Content.ReadFromJsonAsync<PagedEventsResponse>();
await Assert.That(page!.Items.Count).IsEqualTo(1);
```

The five affected tests (upload-response reads keep `List<EventResponse>` — only `GET /api/events` reads change):

1. `Upload_GpxFile_ReturnedByGetEventsWithEnrichmentFlag` — `page.Items[0].NeedsEnrichment`.
2. `GetEvents_WithEventTypeFilter_ReturnsFilteredResults` — `page.Items[0].EventType`.
3. `DeleteEvent_OwnedByUser_Returns204` — `remaining.Items.Count`.
4. `DeleteEvent_BelongingToAnotherUser_Returns204ButDoesNotDelete` — `remaining.Items.Count`.
5. `Upload_JsonWithSplits_GetEventsReturnsSplits` — **rename to `Upload_JsonWithSplits_GetEventByIdReturnsSplits`** and change it to fetch the detail route (list items no longer carry splits):

```csharp
[Test]
public async Task Upload_JsonWithSplits_GetEventByIdReturnsSplits()
{
    var token = await GetTokenAsync("splits-get@example.com");
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    const string json = """
        [{
          "event_type": "HYROX",
          "event_name": "HYROX Berlin 2024",
          "event_date": "2024-11-10",
          "completion": "FINISHED",
          "elapsed_secs": 5400,
          "splits": [
            { "split_type": "STATION", "split_label": "SkiErg", "split_secs": 300, "cumulative_secs": 300 }
          ]
        }]
        """;

    var uploadResponse = await _client.PostAsync("/api/events/upload", BuildJsonUpload(json));
    var uploaded = await uploadResponse.Content.ReadFromJsonAsync<List<EventResponse>>();

    var response = await _client.GetAsync($"/api/events/{uploaded![0].Id}");
    var ev = await response.Content.ReadFromJsonAsync<EventResponse>();

    await Assert.That(ev!.Splits.Count).IsEqualTo(1);
    await Assert.That(ev.Splits[0].SplitType).IsEqualTo("STATION");
    await Assert.That(ev.Splits[0].SplitLabel).IsEqualTo("SkiErg");
    await Assert.That(ev.Splits[0].SplitSecs).IsEqualTo(300);
    await Assert.That(ev.Splits[0].CumulativeSecs).IsEqualTo(300);
}
```

- [ ] **Step 6: Sweep for other consumers of the old list shape**

Run: `grep -rn 'GetAsync("/api/events"' tests/ && grep -rn 'GetFromJsonAsync<List<EventResponse>>' tests/`
Expected known hits: `EventEndpointsTests.cs` (fixed in Step 5). If `SyncEndpointsTests.cs`, `CreateEventTests.cs`, `GetEventByIdTests.cs`, or `PredictionEndpointsTests.cs` read the **list** route as `List<EventResponse>`, apply the same `PagedEventsResponse`/`.Items` transformation from Step 5. Reads of `/api/events/{id}`, `/api/events/upload`, or `POST /api/events` responses are unchanged — do not touch them.

- [ ] **Step 7: Run the full .NET suite**

Run: `dotnet run --project tests/Pacevite.Api.Tests`
Expected: all tests PASS (the new pagination tests exercise the keyset SQL against real Postgres — a `Guid.CompareTo` translation failure would surface here as `InvalidOperationException: could not be translated`).

- [ ] **Step 8: Commit**

```bash
git add src/Pacevite.Api tests/Pacevite.Api.Tests
git commit -m "feat: keyset-cursor pagination and server-side search on GET /api/events

List items no longer include splits; contract is now { items, nextCursor }.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Composite keyset index migration

**Files:**
- Modify: `src/Pacevite.Api/Infrastructure/Persistence/AppDbContext.cs:53`
- Create (generated): `src/Pacevite.Api/Migrations/*_EventsKeysetPaginationIndex.cs` + regenerated `Infrastructure/Persistence/Compiled/*`

**Interfaces:**
- Consumes: nothing from prior tasks (Task 2's query benefits from it).
- Produces: index `IX_Events_UserId_EventDate_Id` replacing `IX_Events_UserId_EventDate`.

- [ ] **Step 1: Update the index definition**

In `src/Pacevite.Api/Infrastructure/Persistence/AppDbContext.cs`, replace line 53:

```csharp
// OLD
entity.HasIndex(e => new { e.UserId, e.EventDate });

// NEW — Id tiebreaker makes the keyset ORDER BY (EventDate DESC, Id DESC) fully index-served.
// Postgres btree scans backwards, so ASC columns serve the DESC walk.
entity.HasIndex(e => new { e.UserId, e.EventDate, e.Id });
```

- [ ] **Step 2: Ensure the dev database is running**

Run: `podman compose up -d db`
Expected: `db` container running (idempotent if already up).

- [ ] **Step 3: Run the 3-step migration workflow**

```bash
dotnet ef migrations add EventsKeysetPaginationIndex --project src/Pacevite.Api
dotnet ef database update --project src/Pacevite.Api
dotnet ef dbcontext optimize \
  --project src/Pacevite.Api \
  --output-dir Infrastructure/Persistence/Compiled \
  --namespace Pacevite.Api.Infrastructure.Persistence.Compiled
```

Expected: migration file drops `IX_Events_UserId_EventDate` and creates `IX_Events_UserId_EventDate_Id`; compiled model regenerated.

- [ ] **Step 4: Verify tests still pass**

Run: `dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/*/*[Category=Integration]"`
Expected: PASS (integration fixtures run `Database.Migrate()`, so they exercise the new migration).

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Api
git commit -m "feat: add composite (UserId, EventDate, Id) index for keyset pagination

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Timeline read model — GET /api/events/timeline

Lightweight `(id, eventDate, eventType, elapsedSecs, completion)` tuples, ascending by date, for chart consumers. No splits, no `Include`.

**Files:**
- Create: `src/Pacevite.Api/Features/Events/GetTimeline/GetTimelineQuery.cs`
- Create: `src/Pacevite.Api/Features/Events/GetTimeline/GetTimelineValidator.cs`
- Create: `src/Pacevite.Api/Features/Events/GetTimeline/GetTimelineHandler.cs`
- Create: `src/Pacevite.Api/Contracts/Responses/TimelineEntryResponse.cs`
- Modify: `src/Pacevite.Api/Features/Events/EventEndpoints.cs` (route + handler method)
- Test: `tests/Pacevite.Api.Tests/Integration/GetTimelineTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `TimelineEntryResponse(Guid Id, DateOnly EventDate, string EventType, int ElapsedSecs, string Completion)`; route `GET /api/events/timeline?eventType=` (authorized). Task 7's `useTimeline` hook consumes the camelCase JSON.

- [ ] **Step 1: Write the failing integration tests**

Create `tests/Pacevite.Api.Tests/Integration/GetTimelineTests.cs` — same fixture boilerplate as `GetEventsPaginationTests` (copy the `SetUpAsync`/`TearDownAsync`/`GetTokenAsync`/`UploadCsvAsync` members verbatim, database name `pacevite_timeline_test`), plus:

```csharp
[Test]
public async Task timeline_returns_all_events_ascending_by_date_without_splits()
{
    // Arrange
    var token = await GetTokenAsync("timeline@example.com");
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    await UploadCsvAsync("""
        MARATHON,Race Late,2024-09-10,FINISHED,14400
        MARATHON,Race Early,2024-01-10,FINISHED,15000
        HYROX,Hyrox One,2024-05-10,DNF,5400
        """);

    // Act
    var response = await _client.GetAsync("/api/events/timeline");
    var raw = await response.Content.ReadAsStringAsync();
    var entries = await _client.GetFromJsonAsync<List<TimelineEntryResponse>>("/api/events/timeline");

    // Assert
    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    await Assert.That(entries!.Count).IsEqualTo(3);
    await Assert.That(entries[0].EventDate).IsEqualTo(new DateOnly(2024, 1, 10));
    await Assert.That(entries[2].EventDate).IsEqualTo(new DateOnly(2024, 9, 10));
    await Assert.That(entries[1].EventType).IsEqualTo("HYROX");
    await Assert.That(entries[1].Completion).IsEqualTo("DNF");
    await Assert.That(raw.Contains("\"splits\"")).IsFalse();
}

[Test]
public async Task timeline_filters_by_event_type()
{
    // Arrange
    var token = await GetTokenAsync("timeline-filter@example.com");
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    await UploadCsvAsync("""
        MARATHON,Race One,2024-01-10,FINISHED,15000
        HYROX,Hyrox One,2024-05-10,FINISHED,5400
        """);

    // Act
    var entries = await _client.GetFromJsonAsync<List<TimelineEntryResponse>>("/api/events/timeline?eventType=HYROX");

    // Assert
    await Assert.That(entries!.Count).IsEqualTo(1);
    await Assert.That(entries[0].EventType).IsEqualTo("HYROX");
}

[Test]
public async Task timeline_is_scoped_to_the_authenticated_user()
{
    // Arrange
    var tokenA = await GetTokenAsync("timeline-a@example.com");
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
    await UploadCsvAsync("MARATHON,Private Race,2024-01-10,FINISHED,15000");

    var tokenB = await GetTokenAsync("timeline-b@example.com");
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);

    // Act
    var entries = await _client.GetFromJsonAsync<List<TimelineEntryResponse>>("/api/events/timeline");

    // Assert
    await Assert.That(entries!.Count).IsEqualTo(0);
}

[Test]
public async Task timeline_without_token_returns_401()
{
    // Act
    _client.DefaultRequestHeaders.Authorization = null;
    var response = await _client.GetAsync("/api/events/timeline");

    // Assert
    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
}

[Test]
public async Task timeline_with_invalid_event_type_returns_400()
{
    // Arrange
    var token = await GetTokenAsync("timeline-bad-type@example.com");
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    // Act
    var response = await _client.GetAsync("/api/events/timeline?eventType=ULTRA_SPRINT");

    // Assert
    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/GetTimelineTests/*"`
Expected: build FAILS ("'TimelineEntryResponse' could not be found").

- [ ] **Step 3: Implement**

Create `src/Pacevite.Api/Contracts/Responses/TimelineEntryResponse.cs`:

```csharp
namespace Pacevite.Api.Contracts.Responses;

public sealed record TimelineEntryResponse(
    Guid Id,
    DateOnly EventDate,
    string EventType,
    int ElapsedSecs,
    string Completion);
```

Create `src/Pacevite.Api/Features/Events/GetTimeline/GetTimelineQuery.cs`:

```csharp
using Mediator;
using Pacevite.Api.Contracts.Responses;

namespace Pacevite.Api.Features.Events.GetTimeline;

public sealed record GetTimelineQuery(
    string UserId,
    string? EventType = null) : IQuery<IReadOnlyList<TimelineEntryResponse>>;
```

Create `src/Pacevite.Api/Features/Events/GetTimeline/GetTimelineValidator.cs`:

```csharp
using FluentValidation;
using Pacevite.Api.Domain.Enums;

namespace Pacevite.Api.Features.Events.GetTimeline;

public sealed class GetTimelineValidator : AbstractValidator<GetTimelineQuery>
{
    private static readonly string[] ValidEventTypes = Enum.GetNames<EventType>();

    public GetTimelineValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();

        RuleFor(x => x.EventType)
            .Must(v => ValidEventTypes.Contains(v, StringComparer.OrdinalIgnoreCase))
            .When(x => x.EventType is not null)
            .WithMessage($"EventType must be one of: {string.Join(", ", ValidEventTypes)}.");
    }
}
```

Create `src/Pacevite.Api/Features/Events/GetTimeline/GetTimelineHandler.cs`:

```csharp
using Mediator;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Features.Events.GetTimeline;

public sealed class GetTimelineHandler(AppDbContext db, ILogger<GetTimelineHandler> logger)
    : IQueryHandler<GetTimelineQuery, IReadOnlyList<TimelineEntryResponse>>
{
    public async ValueTask<IReadOnlyList<TimelineEntryResponse>> Handle(
        GetTimelineQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var q = db.Events.Where(e => e.UserId == query.UserId);

            if (query.EventType is not null && Enum.TryParse<EventType>(query.EventType, ignoreCase: true, out var eventType))
                q = q.Where(e => e.EventType == eventType);

            // Project to columns only — enum ToString isn't SQL-translatable, so map in memory.
            var rows = await q
                .OrderBy(e => e.EventDate)
                .ThenBy(e => e.Id)
                .Select(e => new { e.Id, e.EventDate, e.EventType, e.ElapsedSecs, e.Completion })
                .ToListAsync(cancellationToken);

            return rows
                .Select(r => new TimelineEntryResponse(
                    r.Id,
                    r.EventDate,
                    r.EventType.ToString().ToUpperInvariant(),
                    r.ElapsedSecs,
                    r.Completion.ToString().ToUpperInvariant()))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "GetTimelineHandler failed for {UserId}", query.UserId);
            throw;
        }
    }
}
```

In `src/Pacevite.Api/Features/Events/EventEndpoints.cs`, add the route after the `personal-bests` line and the handler method after `GetPersonalBestsAsync`; add `using Pacevite.Api.Features.Events.GetTimeline;` to the imports:

```csharp
app.MapGet("/timeline", GetTimelineAsync).WithName("GetEventsTimeline");
```

```csharp
private static async Task<Ok<IReadOnlyList<TimelineEntryResponse>>> GetTimelineAsync(
    ClaimsPrincipal user,
    IMediator mediator,
    CancellationToken ct,
    string? eventType = null)
{
    var userId = GetUserId(user);
    var result = await mediator.Send(new GetTimelineQuery(userId, eventType), ct);
    return TypedResults.Ok(result);
}
```

Run: `dotnet build` (Mediator source-gen must discover the new handler).

- [ ] **Step 4: Run the tests**

Run: `dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/GetTimelineTests/*"`
Expected: all 5 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Api tests/Pacevite.Api.Tests
git commit -m "feat: add GET /api/events/timeline read model for chart consumers

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: averageSplits on GET /api/events/{id}

The detail response gains per-label average split seconds across the athlete's **Finished** events of the same type, computed in SQL — replacing the frontend's `computeAverageSplits` over the full list.

**Files:**
- Create: `src/Pacevite.Api/Contracts/Responses/EventDetailResponse.cs`
- Modify: `src/Pacevite.Api/Features/Events/GetEventById/GetEventByIdQuery.cs` (return type)
- Modify: `src/Pacevite.Api/Features/Events/GetEventById/GetEventByIdHandler.cs`
- Modify: `src/Pacevite.Api/Features/Events/EventMapper.cs` (add `ToDetailResponse`)
- Modify: `src/Pacevite.Api/Features/Events/EventEndpoints.cs` (`GetEventByIdAsync` result type)
- Test: `tests/Pacevite.Api.Tests/Integration/GetEventByIdTests.cs` (add tests; existing ones keep passing — `averageSplits` is additive)

**Interfaces:**
- Consumes: nothing new.
- Produces: `EventDetailResponse` = all `EventResponse` fields + `IReadOnlyList<AverageSplitResponse> AverageSplits`; `AverageSplitResponse(string Label, int AvgSecs)` — JSON `{ label, avgSecs }`, deliberately matching the frontend's existing `AverageSplit` interface in `chartUtils.ts`. Task 7 consumes it.

- [ ] **Step 1: Write the failing integration tests**

Add to `tests/Pacevite.Api.Tests/Integration/GetEventByIdTests.cs` (reuse that file's existing fixture and upload helpers; if it lacks a JSON upload helper, copy `BuildJsonUpload` from `EventEndpointsTests.cs`):

```csharp
[Test]
public async Task detail_includes_average_splits_across_same_type_finished_events()
{
    // Arrange — two HYROX events sharing the "SkiErg" label: (300 + 400) / 2 = 350
    var token = await GetTokenAsync("avg-splits@example.com");
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    const string json = """
        [
          {
            "event_type": "HYROX", "event_name": "HYROX Berlin", "event_date": "2024-11-10",
            "completion": "FINISHED", "elapsed_secs": 5400,
            "splits": [{ "split_type": "STATION", "split_label": "SkiErg", "split_secs": 300, "cumulative_secs": 300 }]
          },
          {
            "event_type": "HYROX", "event_name": "HYROX Hamburg", "event_date": "2025-02-15",
            "completion": "FINISHED", "elapsed_secs": 5600,
            "splits": [{ "split_type": "STATION", "split_label": "SkiErg", "split_secs": 400, "cumulative_secs": 400 }]
          }
        ]
        """;
    var uploadResponse = await _client.PostAsync("/api/events/upload", BuildJsonUpload(json));
    var uploaded = await uploadResponse.Content.ReadFromJsonAsync<List<EventResponse>>();

    // Act
    var detail = await _client.GetFromJsonAsync<EventDetailResponse>($"/api/events/{uploaded![0].Id}");

    // Assert
    await Assert.That(detail!.AverageSplits.Count).IsEqualTo(1);
    await Assert.That(detail.AverageSplits[0].Label).IsEqualTo("SkiErg");
    await Assert.That(detail.AverageSplits[0].AvgSecs).IsEqualTo(350);
}

[Test]
public async Task average_splits_exclude_non_finished_events()
{
    // Arrange — the DNF's 900s SkiErg must not drag the average
    var token = await GetTokenAsync("avg-dnf@example.com");
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    const string json = """
        [
          {
            "event_type": "HYROX", "event_name": "Good Race", "event_date": "2024-11-10",
            "completion": "FINISHED", "elapsed_secs": 5400,
            "splits": [{ "split_type": "STATION", "split_label": "SkiErg", "split_secs": 300, "cumulative_secs": 300 }]
          },
          {
            "event_type": "HYROX", "event_name": "Bad Day", "event_date": "2025-02-15",
            "completion": "DNF", "elapsed_secs": 900,
            "splits": [{ "split_type": "STATION", "split_label": "SkiErg", "split_secs": 900, "cumulative_secs": 900 }]
          }
        ]
        """;
    var uploadResponse = await _client.PostAsync("/api/events/upload", BuildJsonUpload(json));
    var uploaded = await uploadResponse.Content.ReadFromJsonAsync<List<EventResponse>>();
    var finishedId = uploaded!.Single(e => e.EventName == "Good Race").Id;

    // Act
    var detail = await _client.GetFromJsonAsync<EventDetailResponse>($"/api/events/{finishedId}");

    // Assert
    await Assert.That(detail!.AverageSplits.Single(a => a.Label == "SkiErg").AvgSecs).IsEqualTo(300);
}

[Test]
public async Task average_splits_is_empty_when_no_same_type_event_has_splits()
{
    // Arrange
    var token = await GetTokenAsync("avg-empty@example.com");
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    const string csv = "MARATHON,No Splits Race,2024-09-29,FINISHED,14400";
    var content = new MultipartFormDataContent();
    var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
    fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
    content.Add(fileContent, "file", "events.csv");
    var uploadResponse = await _client.PostAsync("/api/events/upload", content);
    var uploaded = await uploadResponse.Content.ReadFromJsonAsync<List<EventResponse>>();

    // Act
    var detail = await _client.GetFromJsonAsync<EventDetailResponse>($"/api/events/{uploaded![0].Id}");

    // Assert
    await Assert.That(detail!.AverageSplits.Count).IsEqualTo(0);
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/GetEventByIdTests/*"`
Expected: build FAILS ("'EventDetailResponse' could not be found").

- [ ] **Step 3: Implement**

Create `src/Pacevite.Api/Contracts/Responses/EventDetailResponse.cs`:

```csharp
namespace Pacevite.Api.Contracts.Responses;

// Shape matches chartUtils.ts's AverageSplit interface: { label, avgSecs }.
public sealed record AverageSplitResponse(string Label, int AvgSecs);

public sealed record EventDetailResponse(
    Guid Id,
    string EventType,
    string EventName,
    DateOnly EventDate,
    string Completion,
    int ElapsedSecs,
    int? OverallRank,
    int? AgeGroupRank,
    int? FieldSize,
    int? AgeGroupFieldSize,
    string Source,
    bool NeedsEnrichment,
    DateTimeOffset CreatedAt,
    IReadOnlyList<EventSplitResponse> Splits,
    IReadOnlyList<AverageSplitResponse> AverageSplits);
```

Change `src/Pacevite.Api/Features/Events/GetEventById/GetEventByIdQuery.cs` return type:

```csharp
public sealed record GetEventByIdQuery(Guid EventId, string UserId) : IQuery<EventDetailResponse?>;
```

Add to `src/Pacevite.Api/Features/Events/EventMapper.cs`:

```csharp
internal static EventDetailResponse ToDetailResponse(
    Event ev, IReadOnlyList<AverageSplitResponse> averageSplits) => new(
    ev.Id,
    ev.EventType.ToString().ToUpperInvariant(),
    ev.EventName,
    ev.EventDate,
    ev.Completion.ToString().ToUpperInvariant(),
    ev.ElapsedSecs,
    ev.OverallRank,
    ev.AgeGroupRank,
    ev.FieldSize,
    ev.AgeGroupFieldSize,
    ev.Source,
    ev.NeedsEnrichment,
    ev.CreatedAt,
    ev.Splits.Select(s => new EventSplitResponse(s.Id, s.SplitType, s.SplitLabel, s.SplitSecs, s.CumulativeSecs)).ToList(),
    averageSplits);
```

Replace `src/Pacevite.Api/Features/Events/GetEventById/GetEventByIdHandler.cs`:

```csharp
using Mediator;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Features.Events.GetEventById;

public sealed class GetEventByIdHandler(AppDbContext db, ILogger<GetEventByIdHandler> logger)
    : IQueryHandler<GetEventByIdQuery, EventDetailResponse?>
{
    public async ValueTask<EventDetailResponse?> Handle(
        GetEventByIdQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var ev = await db.Events
                .Include(e => e.Splits.OrderBy(s => s.CumulativeSecs))
                .Where(e => e.Id == query.EventId && e.UserId == query.UserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (ev is null)
                return null;

            // Per-label averages across the athlete's FINISHED events of the same type,
            // computed in SQL — replaces client-side computeAverageSplits over the full list.
            var averages = await db.Events
                .Where(e => e.UserId == query.UserId
                    && e.EventType == ev.EventType
                    && e.Completion == CompletionStatus.Finished)
                .SelectMany(e => e.Splits)
                .GroupBy(s => s.SplitLabel)
                .Select(g => new { Label = g.Key, AvgSecs = g.Average(s => (double)s.SplitSecs) })
                .ToListAsync(cancellationToken);

            var averageSplits = averages
                .Select(a => new AverageSplitResponse(a.Label, (int)Math.Round(a.AvgSecs)))
                .ToList();

            return EventMapper.ToDetailResponse(ev, averageSplits);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "GetEventByIdHandler failed for {UserId} and {EventId}", query.UserId, query.EventId);
            throw;
        }
    }
}
```

In `src/Pacevite.Api/Features/Events/EventEndpoints.cs`, change `GetEventByIdAsync`'s result type:

```csharp
private static async Task<Results<Ok<EventDetailResponse>, NotFound>> GetEventByIdAsync(
```

(body unchanged). Run `dotnet build`.

- [ ] **Step 4: Run the tests**

Run: `dotnet run --project tests/Pacevite.Api.Tests`
Expected: full suite PASS — the pre-existing `GetEventByIdTests` deserialize into `EventResponse`, which ignores the extra `averageSplits` JSON property, so they keep passing; the renamed splits test from Task 2 also passes.

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Api tests/Pacevite.Api.Tests
git commit -m "feat: add SQL-computed averageSplits to GET /api/events/{id}

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: Make chartUtils generic over minimal event shapes

`groupByEventType` / `computePbs` currently require full `EventResponse`; timeline entries must satisfy them. Pure type-level change — behavior identical.

**Files:**
- Modify: `src/Pacevite.Web/src/lib/chartUtils.ts:20-44`
- Test: `src/Pacevite.Web/src/lib/chartUtils.test.ts` (existing tests must keep passing unchanged)

**Interfaces:**
- Consumes: nothing.
- Produces: `export interface ChartEvent { id: string; eventType: string; eventDate: string; elapsedSecs: number }`; `groupByEventType<T extends ChartEvent>(events: T[]): Record<string, T[]>`; `computePbs<T extends ChartEvent>(events: T[]): Record<string, T>`. Task 7 passes `TimelineEntry[]` to both. `computeAverageSplits` is **not** touched here — it's deleted in Task 7 together with its last caller.

- [ ] **Step 1: Apply the change**

In `src/Pacevite.Web/src/lib/chartUtils.ts`, add the interface above `groupByEventType` and change both signatures (bodies unchanged):

```ts
// Minimal shape charts need — satisfied by both EventResponse and TimelineEntry.
export interface ChartEvent {
  id: string
  eventType: string
  eventDate: string
  elapsedSecs: number
}

export function groupByEventType<T extends ChartEvent>(events: T[]): Record<string, T[]> {
  const grouped: Record<string, T[]> = {}
  for (const ev of events) {
    if (!grouped[ev.eventType]) grouped[ev.eventType] = []
    grouped[ev.eventType].push(ev)
  }
  for (const key of Object.keys(grouped)) {
    grouped[key].sort((a, b) => a.eventDate.localeCompare(b.eventDate))
  }
  return grouped
}

export function computePbs<T extends ChartEvent>(events: T[]): Record<string, T> {
  const pbs: Record<string, T> = {}
  for (const ev of events) {
    if (!pbs[ev.eventType] || ev.elapsedSecs < pbs[ev.eventType].elapsedSecs) {
      pbs[ev.eventType] = ev
    }
  }
  return pbs
}
```

The existing doc comments above each function stay as they are; only the signatures and internal type annotations change.
```

- [ ] **Step 2: Run the frontend tests**

Run: `cd src/Pacevite.Web && npm test`
Expected: all PASS — generics with an extends-bound are backward compatible for `EventResponse[]` callers.

- [ ] **Step 3: Commit**

```bash
git add src/Pacevite.Web/src/lib/chartUtils.ts
git commit -m "refactor: make chartUtils grouping/PB helpers generic over minimal event shape

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: Frontend contract migration (types, hooks, MSW, all four consumers)

The atomic flip: hooks move to the paginated/timeline contracts, MSW mirrors the real API (including the documented casing-drift fix), and every `useEvents` consumer is migrated in the same commit so tests stay green.

**Files:**
- Modify: `src/Pacevite.Web/src/lib/types.ts`
- Modify: `src/Pacevite.Web/src/hooks/useEvents.ts`
- Create: `src/Pacevite.Web/src/hooks/useTimeline.ts`
- Modify: `src/Pacevite.Web/src/hooks/useEvent.ts`
- Modify: `src/Pacevite.Web/src/test/handlers.ts`
- Modify: `src/Pacevite.Web/src/pages/DashboardPage.tsx`
- Modify: `src/Pacevite.Web/src/pages/PredictPage.tsx`
- Modify: `src/Pacevite.Web/src/pages/EventDetailPage.tsx`
- Modify: `src/Pacevite.Web/src/components/PredictionTeaser.tsx`
- Modify: `src/Pacevite.Web/src/components/ProgressChart.tsx`
- Modify: `src/Pacevite.Web/src/components/PbPanel.tsx`
- Modify: `src/Pacevite.Web/src/components/RaceComparison.tsx`
- Modify: `src/Pacevite.Web/src/lib/chartUtils.ts` (delete `computeAverageSplits`)
- Test: `src/Pacevite.Web/src/lib/chartUtils.test.ts` (delete `computeAverageSplits` tests)
- Test: `src/Pacevite.Web/src/pages/DashboardPage.test.tsx`, `src/Pacevite.Web/src/pages/EventDetailPage.test.tsx`, `src/Pacevite.Web/src/components/PredictionTeaser.test.tsx` (update MSW overrides)

**Interfaces:**
- Consumes: JSON contracts from Tasks 2/4/5 (`{ items, nextCursor }`, `TimelineEntry[]`, `averageSplits`), `ChartEvent` generics from Task 6.
- Produces: `useEvents(filters?: { search?: string; eventType?: string })` returning `useInfiniteQuery` result (queryKey `['events', filters]`); `useTimeline()` (queryKey `['timeline']`); `useEvent(id)` returning `EventDetailResponse`. Task 8 consumes `useEvents`' `fetchNextPage`/`hasNextPage` and the `search` filter.

- [ ] **Step 1: Add the new types**

In `src/Pacevite.Web/src/lib/types.ts`, after `EventSplitResponse` add:

```ts
export type EventSummaryResponse = Omit<EventResponse, 'splits'>

export interface PagedEventsResponse {
  items: EventSummaryResponse[]
  nextCursor: string | null
}

export interface TimelineEntry {
  id: string
  eventDate: string
  eventType: string
  elapsedSecs: number
  completion: string
}

export interface AverageSplitResponse {
  label: string
  avgSecs: number
}

export interface EventDetailResponse extends EventResponse {
  averageSplits: AverageSplitResponse[]
}
```

- [ ] **Step 2: Rewrite the hooks**

Replace `src/Pacevite.Web/src/hooks/useEvents.ts`:

```ts
import { useInfiniteQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api'
import type { PagedEventsResponse } from '@/lib/types'

export interface EventsFilters {
  search?: string
  eventType?: string
}

export function useEvents(filters: EventsFilters = {}) {
  return useInfiniteQuery({
    queryKey: ['events', filters],
    queryFn: async ({ pageParam }) => {
      const params = new URLSearchParams()
      if (pageParam) params.set('cursor', pageParam)
      if (filters.search) params.set('search', filters.search)
      if (filters.eventType) params.set('eventType', filters.eventType)
      const qs = params.toString()
      const { data } = await apiClient.get<PagedEventsResponse>(`/events${qs ? `?${qs}` : ''}`)
      return data
    },
    initialPageParam: null as string | null,
    getNextPageParam: last => last.nextCursor,
  })
}
```

Create `src/Pacevite.Web/src/hooks/useTimeline.ts`:

```ts
import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api'
import type { TimelineEntry } from '@/lib/types'

export function useTimeline() {
  return useQuery({
    queryKey: ['timeline'],
    queryFn: async () => {
      const { data } = await apiClient.get<TimelineEntry[]>('/events/timeline')
      return data
    },
  })
}
```

In `src/Pacevite.Web/src/hooks/useEvent.ts`, change the imported type and the generic:

```ts
import type { EventDetailResponse } from '@/lib/types'
// ...
const { data } = await apiClient.get<EventDetailResponse>(`/events/${id}`)
```

- [ ] **Step 3: Update MSW handlers to mirror the real API**

In `src/Pacevite.Web/src/test/handlers.ts`:

a. Add a timeline handler **before** the `/api/events/:id` handler (MSW matches in order; note the existing comment about static-before-dynamic):

```ts
http.get('http://localhost/api/events/timeline', () =>
  HttpResponse.json([
    {
      id: 'event-1',
      eventDate: '2024-09-29',
      eventType: 'MARATHON',
      elapsedSecs: 12600,
      completion: 'FINISHED',
    },
  ])
),
```

b. Add `averageSplits` to the `/api/events/:id` payload (after `splits`) and fix its casing drift `source: 'manual'` → `source: 'MANUAL'`:

```ts
averageSplits: [
  { label: '10km', avgSecs: 2940 },
  { label: '21km', avgSecs: 3180 },
],
```

c. Replace the bare `/api/events` list handler body with the paginated shape, dropping `splits` and fixing `source: 'manual'` → `'MANUAL'`:

```ts
http.get('http://localhost/api/events', () =>
  HttpResponse.json({
    items: [
      {
        id: 'event-1',
        eventType: 'MARATHON',
        eventName: 'Berlin Marathon',
        eventDate: '2024-09-29',
        completion: 'FINISHED',
        elapsedSecs: 12600,
        overallRank: 1500,
        ageGroupRank: null,
        fieldSize: 45000,
        ageGroupFieldSize: null,
        source: 'MANUAL',
        needsEnrichment: false,
        createdAt: '2024-10-01T00:00:00Z',
      },
    ],
    nextCursor: null,
  })
),
```

d. Fix the upload handler's casing drift: `eventType: 'Marathon'` → `'MARATHON'`, `source: 'csv'` → `'CSV'` (the documented mock/API drift — upload responses keep the `EventResponse` shape with `splits`).

- [ ] **Step 4: Migrate DashboardPage's data sources**

In `src/Pacevite.Web/src/pages/DashboardPage.tsx` replace lines 19-26 (list + chart wiring — search/load-more UI comes in Task 8):

```tsx
import { useTimeline } from '@/hooks/useTimeline'
// ...
const { data, isLoading: eventsLoading } = useEvents()
const events = data?.pages.flatMap(p => p.items) ?? []

const { data: timeline = [] } = useTimeline()
const grouped = groupByEventType(timeline)
const pbs = computePbs(timeline)
const defaultType = Object.keys(grouped)[0] ?? ''
const [selectedType, setSelectedType] = useState<string>(defaultType)
const chartType = selectedType || defaultType
const chartEvents = grouped[chartType] ?? []
const pbId = pbs[chartType]?.id
```

Two consequential tweaks in the same file:
- The analytics section's visibility guard changes from `events.length > 0` to `timeline.length > 0` (and the `progress-chart-empty` guard from `events.length === 0 && !eventsLoading` to `timeline.length === 0 && !eventsLoading`) — charts are timeline-driven now.
- The delete mutation's `onSuccess` gains a third invalidation:

```ts
void queryClient.invalidateQueries({ queryKey: ['timeline'] })
```

- [ ] **Step 5: Add the timeline invalidation to the other mutation sites**

Add `void queryClient.invalidateQueries({ queryKey: ['timeline'] })` beside the existing `['events']` invalidations at:
- `src/Pacevite.Web/src/pages/AddEventPage.tsx:66`
- `src/Pacevite.Web/src/pages/UploadPage.tsx:24`
- `src/Pacevite.Web/src/pages/SyncPage.tsx:50`

- [ ] **Step 6: Migrate the remaining consumers**

`src/Pacevite.Web/src/components/PredictionTeaser.tsx` — swap the hook (lines 3, 9-14):

```tsx
import { useTimeline } from '@/hooks/useTimeline'
// ...
const { data: timeline = [], isLoading: eventsLoading } = useTimeline()

const mostRecentType = useMemo(() => {
  const sorted = [...timeline].sort((a, b) => b.eventDate.localeCompare(a.eventDate))
  return sorted[0]?.eventType ?? null
}, [timeline])
```

`src/Pacevite.Web/src/pages/PredictPage.tsx` — same swap: replace `import { useEvents } from '@/hooks/useEvents'` with `import { useTimeline } from '@/hooks/useTimeline'` and `const { data: events = [], isLoading: eventsLoading } = useEvents()` with `const { data: events = [], isLoading: eventsLoading } = useTimeline()`. The eligible-types loop reads `eventType`/`completion`, both present on `TimelineEntry`.

`src/Pacevite.Web/src/components/ProgressChart.tsx` — change the prop type and drop the unused `name` field:

```tsx
import type { TimelineEntry } from '@/lib/types'

interface Props {
  events: TimelineEntry[]
  pbId: string | undefined
}
// in the data mapping, remove `name: ev.eventName` (TimelineEntry has no name; the tooltip never displayed it)
const data = events.map(ev => ({ date: ev.eventDate, secs: ev.elapsedSecs, id: ev.id }))
```

`src/Pacevite.Web/src/components/PbPanel.tsx` — change the prop type:

```tsx
import type { TimelineEntry } from '@/lib/types'

interface Props {
  events: TimelineEntry[]
  selectedType: string
  onSelectType: (type: string) => void
}
```

`src/Pacevite.Web/src/components/RaceComparison.tsx` — the sparkline needs only `id`/`eventDate`/`elapsedSecs`:

```tsx
import type { EventResponse, TimelineEntry } from '@/lib/types'

interface Props {
  event: EventResponse
  sameTypeEvents: TimelineEntry[]
}
```

`src/Pacevite.Web/src/pages/EventDetailPage.tsx` — replace lines 3-4 and 14-21:

```tsx
import { useTimeline } from '@/hooks/useTimeline'
import { computeSplitDeltas, formatElapsed } from '@/lib/chartUtils'
// ...
const { data: event, isLoading } = useEvent(id)
const { data: timeline = [] } = useTimeline()

if (isLoading) return <p className="p-8 text-secondary">Loading…</p>
if (!event) return <p className="p-8 text-secondary">Event not found.</p>

const sameTypeEvents = timeline.filter(e => e.eventType === event.eventType)
const splitDeltas = computeSplitDeltas(event, event.averageSplits)
```

(`AverageSplitResponse` is structurally identical to chartUtils' `AverageSplit`, so `computeSplitDeltas` accepts it unchanged.)

Finally delete `computeAverageSplits` from `src/Pacevite.Web/src/lib/chartUtils.ts` (its last caller is gone) and its `describe`/`it` block from `src/Pacevite.Web/src/lib/chartUtils.test.ts`. Keep the `AverageSplit` interface — `computeSplitDeltas` still uses it.

- [ ] **Step 7: Update the component tests' MSW overrides**

Transformation pattern — every per-test override of the events list changes shape, and tests that drive the **charts** must now override the **timeline** handler:

```ts
// OLD override (list drove both list and charts)
http.get('http://localhost/api/events', () => HttpResponse.json([]))

// NEW — paginated list + timeline are separate sources
http.get('http://localhost/api/events', () => HttpResponse.json({ items: [], nextCursor: null }))
http.get('http://localhost/api/events/timeline', () => HttpResponse.json([]))
```

Known spots:
- `DashboardPage.test.tsx` — `shows empty state when no events exist` and `shows empty state in progress chart when no events exist`: apply the pattern above (both need list **and** timeline emptied). `shows a needs-enrichment badge…`: wrap its event in `{ items: [...], nextCursor: null }` and drop the `splits: []` property. The `renders personal bests… getAllByText('MARATHON')).toHaveLength(3)` assertion still holds (PB card + event row + PbPanel-from-timeline).
- `PredictionTeaser.test.tsx` — any `http.get('http://localhost/api/events', …)` override switches to `http://localhost/api/events/timeline` with `TimelineEntry[]` payloads (`{ id, eventDate, eventType, elapsedSecs, completion }`).
- `EventDetailPage.test.tsx` — overrides of the events list (used for same-type comparison) switch to timeline entries; detail-route overrides gain an `averageSplits` array.

- [ ] **Step 8: Run the frontend suite and fix stragglers**

Run: `cd src/Pacevite.Web && npm test`
Expected: all PASS. Any remaining failure will be an MSW override still returning the old array shape — apply the Step 7 pattern where the failure points.

- [ ] **Step 9: Commit**

```bash
git add src/Pacevite.Web
git commit -m "feat: migrate frontend to paginated events + timeline read model

useEvents is now an infinite query; charts read /events/timeline; event
detail uses server-computed averageSplits. MSW mocks now mirror the real
API shape, fixing the documented casing drift.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: Search box + Load More on DashboardPage

**Files:**
- Modify: `src/Pacevite.Web/src/pages/DashboardPage.tsx`
- Test: `src/Pacevite.Web/src/pages/DashboardPage.test.tsx`

**Interfaces:**
- Consumes: `useEvents({ search })` and `fetchNextPage`/`hasNextPage`/`isFetchingNextPage` from Task 7.
- Produces: user-facing search input (placeholder `Search events…`) and a `Load more` button.

- [ ] **Step 1: Write the failing tests**

Add to `src/Pacevite.Web/src/pages/DashboardPage.test.tsx`:

```tsx
it('requests the next page when Load more is clicked', async () => {
  server.use(
    http.get('http://localhost/api/events', ({ request }) => {
      const cursor = new URL(request.url).searchParams.get('cursor')
      if (cursor === 'cursor-page-2') {
        return HttpResponse.json({
          items: [{
            id: 'event-2', eventType: 'HYROX', eventName: 'HYROX Hamburg',
            eventDate: '2024-05-11', completion: 'FINISHED', elapsedSecs: 5400,
            overallRank: null, ageGroupRank: null, fieldSize: null, ageGroupFieldSize: null,
            source: 'MANUAL', needsEnrichment: false, createdAt: '2024-05-12T00:00:00Z',
          }],
          nextCursor: null,
        })
      }
      return HttpResponse.json({
        items: [{
          id: 'event-1', eventType: 'MARATHON', eventName: 'Berlin Marathon',
          eventDate: '2024-09-29', completion: 'FINISHED', elapsedSecs: 12600,
          overallRank: null, ageGroupRank: null, fieldSize: null, ageGroupFieldSize: null,
          source: 'MANUAL', needsEnrichment: false, createdAt: '2024-10-01T00:00:00Z',
        }],
        nextCursor: 'cursor-page-2',
      })
    })
  )

  renderDashboard()

  await waitFor(() => {
    expect(screen.getByRole('button', { name: /load more/i })).toBeInTheDocument()
  })

  await userEvent.click(screen.getByRole('button', { name: /load more/i }))

  await waitFor(() => {
    expect(screen.getByText('HYROX Hamburg')).toBeInTheDocument()
  })
  expect(screen.getByText('Berlin Marathon')).toBeInTheDocument()
})

it('hides Load more when there are no further pages', async () => {
  renderDashboard() // default MSW handler returns nextCursor: null

  const heading = await screen.findByRole('heading', { name: /all events/i })
  const section = heading.closest('section')!
  await waitFor(() => {
    expect(within(section).getByText('Berlin Marathon')).toBeInTheDocument()
  })

  expect(screen.queryByRole('button', { name: /load more/i })).not.toBeInTheDocument()
})

it('sends the debounced search term as a server-side query param', async () => {
  const capturedSearches: (string | null)[] = []
  server.use(
    http.get('http://localhost/api/events', ({ request }) => {
      capturedSearches.push(new URL(request.url).searchParams.get('search'))
      return HttpResponse.json({ items: [], nextCursor: null })
    })
  )

  renderDashboard()

  const input = await screen.findByPlaceholderText(/search events/i)
  await userEvent.type(input, 'berlin')

  await waitFor(() => {
    expect(capturedSearches).toContain('berlin')
  })
  // Debounce means we must NOT have fired one request per keystroke
  expect(capturedSearches.filter(s => s !== null && s !== 'berlin')).toHaveLength(0)
})
```

- [ ] **Step 2: Run to verify failure**

Run: `cd src/Pacevite.Web && npm test -- DashboardPage`
Expected: FAIL — no element with placeholder `Search events…`, no `Load more` button.

- [ ] **Step 3: Implement**

In `src/Pacevite.Web/src/pages/DashboardPage.tsx`:

a. Imports: add `useEffect` to the React import; add `Search` to the lucide-react import.

b. Search state + debounce (above the `useEvents` call), and pass the filter:

```tsx
const SEARCH_DEBOUNCE_MS = 300

// inside the component:
const [search, setSearch] = useState('')
const [debouncedSearch, setDebouncedSearch] = useState('')

useEffect(() => {
  const timer = setTimeout(() => setDebouncedSearch(search), SEARCH_DEBOUNCE_MS)
  return () => clearTimeout(timer)
}, [search])

const { data, isLoading: eventsLoading, fetchNextPage, hasNextPage, isFetchingNextPage } =
  useEvents({ search: debouncedSearch || undefined })
const events = data?.pages.flatMap(p => p.items) ?? []
```

(`debouncedSearch` is part of the query key, so changing it automatically resets to page 1 — no manual cursor reset needed.)

c. Search input — insert between the `All Events` heading and the loading indicator:

```tsx
<div className="relative mb-3">
  <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-muted" />
  <input
    type="search"
    value={search}
    onChange={e => setSearch(e.target.value)}
    placeholder="Search events…"
    className="w-full bg-surface border border-border rounded-md pl-9 pr-3 py-2 text-sm text-primary placeholder:text-muted"
  />
</div>
```

d. Empty-state: the "No events yet." block must not show for an empty *search result* — change its condition to `!eventsLoading && events.length === 0 && !debouncedSearch`, and add a no-matches state:

```tsx
{!eventsLoading && events.length === 0 && debouncedSearch && (
  <p className="text-sm text-secondary">No events match “{debouncedSearch}”.</p>
)}
```

e. Load More button — after the event-list `</div>` inside the section:

```tsx
{hasNextPage && (
  <div className="mt-3 text-center">
    <button
      onClick={() => fetchNextPage()}
      disabled={isFetchingNextPage}
      className="text-sm font-medium text-indigo-600 hover:text-indigo-800 disabled:opacity-40"
    >
      {isFetchingNextPage ? 'Loading…' : 'Load more'}
    </button>
  </div>
)}
```

- [ ] **Step 4: Run the tests**

Run: `cd src/Pacevite.Web && npm test`
Expected: all PASS, including the three new tests.

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Web/src/pages/DashboardPage.tsx src/Pacevite.Web/src/pages/DashboardPage.test.tsx
git commit -m "feat: add server-side search and Load More pagination to dashboard

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 9: E2E coverage, docs refresh, full-suite verification

**Files:**
- Modify: `src/Pacevite.Web/e2e/dashboard.spec.ts`
- Modify: `docs/specs/00-overview.md`

**Interfaces:**
- Consumes: the running app (Playwright auto-starts API + frontend).
- Produces: nothing downstream — this is the closing gate.

- [ ] **Step 1: Add the search E2E test**

Append to `src/Pacevite.Web/e2e/dashboard.spec.ts` (same conventions as the existing tests — `uniqueEmail`, `registerViaApi`, `loginViaUi`, CSV fixture upload):

```ts
test('search filters the event list server-side', async ({ page }) => {
  const email = uniqueEmail()
  await registerViaApi(email)
  await loginViaUi(page, email)

  // Seed events via the upload UI
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

  await expect(page.getByText('Test Half Marathon').first()).toBeVisible()

  // Search for a term that matches nothing
  await page.getByPlaceholder('Search events…').fill('zzz-no-such-event')
  await expect(page.getByText(/no events match/i)).toBeVisible({ timeout: 5000 })

  // Clear and search for the real event
  await page.getByPlaceholder('Search events…').fill('Half')
  await expect(page.getByText('Test Half Marathon').first()).toBeVisible({ timeout: 5000 })
})
```

(Deliberate scope trims vs. the spec's E2E bullet: a load-more E2E and a "search finds an event beyond page 1" E2E are both omitted — each would need 21+ seeded events to force a second page, and both behaviors are already covered by the API page-walk/search integration tests plus the Load-more and search component tests. The E2E layer verifies the wiring, which this search test does with the fixture-sized dataset.)

- [ ] **Step 2: Run E2E**

Run: `cd src/Pacevite.Web && npm run test:e2e`
Expected: all E2E tests PASS (existing dashboard/delete/upload specs confirm no regression from the contract flip).

- [ ] **Step 3: Refresh the docs that this feature invalidates**

In `docs/specs/00-overview.md`:
- Route table: change the `GET /api/events` row's purpose to `List events (keyset-cursor paginated; filterable by type, date range, and name search)` and add a row: `| GET /api/events/timeline | authorized | Lightweight (date, type, elapsed, completion) series for charts |`.
- Known Limitations table: delete the `Client-side pagination only` row and the `MSW mock casing drift` row (both fixed by this work).
- Frontend Feature Areas: change the Event dashboard line to `DashboardPage — paginated event list with server-side search (Load more), delete`.

- [ ] **Step 4: Full-suite verification**

```bash
dotnet run --project tests/Pacevite.Api.Tests
cd src/Pacevite.Web && npm test && npm run test:e2e
```

Expected: everything PASSES. If any step fails, fix before committing — do not commit red.

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Web/e2e/dashboard.spec.ts docs/specs/00-overview.md
git commit -m "test: add search e2e coverage; refresh overview spec for pagination

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```
