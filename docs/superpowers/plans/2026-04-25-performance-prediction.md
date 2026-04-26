# Performance Prediction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `/predict` page that shows an algorithmically computed finish-time prediction per event type plus an on-demand AI coaching breakdown streamed from Claude.

**Architecture:** Two new API endpoints — `GET /api/events/prediction` (Mediator, JSON) and `GET /api/events/prediction/coaching` (raw SSE, bypasses Mediator). A pure linear-regression helper lives in `Infrastructure/Regression/` and is reused by both handlers. The frontend has a dedicated `PredictPage`, three new components (`PredictionCard`, `PredictionChart`, `PredictionCoaching`, `PredictionTeaser`), and a `usePrediction` hook.

**Tech Stack:** .NET 10, Mediator (source-gen), EF Core 10, Anthropic.SDK 5.10.0, React 19, TanStack Query v5, TUnit (API tests), Vitest + Testing Library + MSW (frontend tests)

---

## File Map

| File | Action | Purpose |
|---|---|---|
| `src/Pacevite.Api/Infrastructure/Regression/LinearRegression.cs` | Create | Pure linear-regression math — slope, intercept, R², predict |
| `src/Pacevite.Api/Contracts/Responses/PredictionResponse.cs` | Create | `PredictionResponse` + `PredictionDataPoint` records |
| `src/Pacevite.Api/Features/Events/GetPrediction/GetPredictionQuery.cs` | Create | Mediator query record |
| `src/Pacevite.Api/Features/Events/GetPrediction/GetPredictionHandler.cs` | Create | Runs regression, derives confidence, builds response |
| `src/Pacevite.Api/Features/Events/GetPrediction/GetPredictionValidator.cs` | Create | Validates eventType is a known enum value |
| `src/Pacevite.Api/Features/Events/PredictionCoaching/PredictionCoachingHandler.cs` | Create | Fetches events, builds prompt, streams Claude SSE |
| `src/Pacevite.Api/Features/Events/EventEndpoints.cs` | Modify | Register two new prediction routes |
| `src/Pacevite.Api/Program.cs` | Modify | Register `AnthropicClient`, `AnthropicOptions`, `PredictionCoachingHandler` |
| `tests/Pacevite.Api.Tests/Unit/Prediction/LinearRegressionTests.cs` | Create | Math unit tests |
| `tests/Pacevite.Api.Tests/Unit/Prediction/GetPredictionHandlerTests.cs` | Create | Handler unit tests |
| `tests/Pacevite.Api.Tests/Integration/PredictionEndpointsTests.cs` | Create | Integration tests for both endpoints |
| `src/Pacevite.Web/src/lib/types.ts` | Modify | Add `PredictionResponse`, `PredictionDataPoint` |
| `src/Pacevite.Web/src/hooks/usePrediction.ts` | Create | React Query hook |
| `src/Pacevite.Web/src/test/handlers.ts` | Modify | MSW handler for `/api/events/prediction` |
| `src/Pacevite.Web/src/components/PredictionCard.tsx` | Create | Predicted time + confidence badge + avg improvement |
| `src/Pacevite.Web/src/components/PredictionCard.test.tsx` | Create | Component unit tests |
| `src/Pacevite.Web/src/components/PredictionChart.tsx` | Create | Recharts chart with dashed projection trend line |
| `src/Pacevite.Web/src/components/PredictionChart.test.tsx` | Create | Component unit tests |
| `src/Pacevite.Web/src/components/PredictionCoaching.tsx` | Create | Streaming AI coaching UI |
| `src/Pacevite.Web/src/components/PredictionCoaching.test.tsx` | Create | Component unit tests |
| `src/Pacevite.Web/src/components/PredictionTeaser.tsx` | Create | Compact dashboard widget |
| `src/Pacevite.Web/src/components/PredictionTeaser.test.tsx` | Create | Component unit tests |
| `src/Pacevite.Web/src/pages/PredictPage.tsx` | Create | `/predict` route page |
| `src/Pacevite.Web/src/App.tsx` | Modify | Add `/predict` route |
| `src/Pacevite.Web/src/pages/DashboardPage.tsx` | Modify | Add `PredictionTeaser` + Predict nav link |
| `src/Pacevite.Web/src/pages/UploadPage.tsx` | Modify | Add Predict nav link |
| `src/Pacevite.Web/src/pages/EventDetailPage.tsx` | Modify | Add Predict nav link |

---

## Task 1: Linear Regression Helper

**Files:**
- Create: `src/Pacevite.Api/Infrastructure/Regression/LinearRegression.cs`
- Create: `tests/Pacevite.Api.Tests/Unit/Prediction/LinearRegressionTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Pacevite.Api.Tests/Unit/Prediction/LinearRegressionTests.cs
using Pacevite.Api.Infrastructure.Regression;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Prediction;

[Category("Unit")]
public sealed class LinearRegressionTests
{
    [Test]
    public async Task Fit_PerfectDecreasingLine_ReturnsCorrectSlopeInterceptAndRSquared()
    {
        // Arrange
        (double X, double Y)[] points = [(0, 4930), (100, 4730), (200, 4530)];

        // Act
        var result = LinearRegression.Fit(points);

        // Assert
        await Assert.That(result.Slope).IsEqualTo(-2.0).Within(0.0001);
        await Assert.That(result.Intercept).IsEqualTo(4930.0).Within(0.0001);
        await Assert.That(result.RSquared).IsEqualTo(1.0).Within(0.0001);
    }

    [Test]
    public async Task Fit_TwoPoints_ExactLinePassesThroughBoth()
    {
        (double X, double Y)[] points = [(0, 4930), (365, 4500)];
        var result = LinearRegression.Fit(points);

        await Assert.That(LinearRegression.Predict(result, 0)).IsEqualTo(4930.0).Within(1.0);
        await Assert.That(LinearRegression.Predict(result, 365)).IsEqualTo(4500.0).Within(1.0);
    }

    [Test]
    public async Task Fit_IdenticalXValues_ReturnsZeroSlopeAndRSquared()
    {
        (double X, double Y)[] points = [(0, 4930), (0, 4500)];
        var result = LinearRegression.Fit(points);

        await Assert.That(result.Slope).IsEqualTo(0.0).Within(0.0001);
        await Assert.That(result.RSquared).IsEqualTo(0.0).Within(0.0001);
    }

    [Test]
    public async Task Fit_FewerThanTwoPoints_ThrowsArgumentException()
    {
        await Assert.That(() => LinearRegression.Fit([(0, 4930)]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Predict_UsesSloperAndIntercept()
    {
        var result = new LinearRegression.Result(Slope: -2.0, Intercept: 5000.0, RSquared: 1.0);
        await Assert.That(LinearRegression.Predict(result, 100)).IsEqualTo(4800.0).Within(0.0001);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --filter "Category=Unit"
```

Expected: FAIL — `LinearRegression` does not exist.

- [ ] **Step 3: Implement `LinearRegression`**

```csharp
// src/Pacevite.Api/Infrastructure/Regression/LinearRegression.cs
namespace Pacevite.Api.Infrastructure.Regression;

public static class LinearRegression
{
    public sealed record Result(double Slope, double Intercept, double RSquared);

    public static Result Fit(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count < 2)
            throw new ArgumentException("At least 2 points required.", nameof(points));

        int n = points.Count;
        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0, sumYY = 0;

        foreach (var (x, y) in points)
        {
            sumX  += x;
            sumY  += y;
            sumXY += x * y;
            sumXX += x * x;
            sumYY += y * y;
        }

        double meanX = sumX / n;
        double meanY = sumY / n;
        double ssXY  = sumXY - n * meanX * meanY;
        double ssXX  = sumXX - n * meanX * meanX;
        double ssYY  = sumYY - n * meanY * meanY;

        double slope     = ssXX == 0 ? 0 : ssXY / ssXX;
        double intercept = meanY - slope * meanX;
        double rSquared  = ssXX == 0 || ssYY == 0 ? 0 : (ssXY * ssXY) / (ssXX * ssYY);

        return new Result(slope, intercept, rSquared);
    }

    public static double Predict(Result regression, double x) =>
        regression.Slope * x + regression.Intercept;
}
```

- [ ] **Step 4: Run tests — all must pass**

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --filter "Category=Unit"
```

Expected: PASS (all 5 new tests green).

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Api/Infrastructure/Regression/LinearRegression.cs \
        tests/Pacevite.Api.Tests/Unit/Prediction/LinearRegressionTests.cs
git commit -m "feat: add LinearRegression helper with unit tests"
```

---

## Task 2: PredictionResponse Contract + GetPrediction Query/Handler/Validator

**Files:**
- Create: `src/Pacevite.Api/Contracts/Responses/PredictionResponse.cs`
- Create: `src/Pacevite.Api/Features/Events/GetPrediction/GetPredictionQuery.cs`
- Create: `src/Pacevite.Api/Features/Events/GetPrediction/GetPredictionHandler.cs`
- Create: `src/Pacevite.Api/Features/Events/GetPrediction/GetPredictionValidator.cs`
- Create: `tests/Pacevite.Api.Tests/Unit/Prediction/GetPredictionHandlerTests.cs`

- [ ] **Step 1: Create the response contract**

```csharp
// src/Pacevite.Api/Contracts/Responses/PredictionResponse.cs
namespace Pacevite.Api.Contracts.Responses;

public sealed record PredictionResponse(
    string EventType,
    int PredictedSecs,
    string ConfidenceLabel,
    int AvgImprovementSecs,
    IReadOnlyList<PredictionDataPoint> DataPoints);

public sealed record PredictionDataPoint(
    Guid? EventId,
    DateOnly EventDate,
    int? ElapsedSecs,
    int FittedSecs);
```

- [ ] **Step 2: Create the query record**

```csharp
// src/Pacevite.Api/Features/Events/GetPrediction/GetPredictionQuery.cs
using Mediator;
using Pacevite.Api.Contracts.Responses;

namespace Pacevite.Api.Features.Events.GetPrediction;

public sealed record GetPredictionQuery(string UserId, string EventType)
    : IQuery<PredictionResponse?>;
```

- [ ] **Step 3: Write handler unit tests (failing)**

```csharp
// tests/Pacevite.Api.Tests/Unit/Prediction/GetPredictionHandlerTests.cs
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Features.Events.GetPrediction;
using Pacevite.Api.Infrastructure.Persistence;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Prediction;

[Category("Unit")]
public sealed class GetPredictionHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GetPredictionHandler _handler;
    private const string UserId = "user-predict-test";

    public GetPredictionHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _handler = new GetPredictionHandler(_db);
    }

    public void Dispose() => _db.Dispose();

    private void SeedEvents(int count, int startSecs = 5000, int improvementPerEvent = 200)
    {
        for (int i = 0; i < count; i++)
        {
            _db.Events.Add(new Event
            {
                UserId = UserId,
                EventType = EventType.Hyrox,
                EventName = $"HYROX Event {i + 1}",
                EventDate = new DateOnly(2023, 1, 1).AddMonths(i * 6),
                Completion = CompletionStatus.Finished,
                ElapsedSecs = startSecs - i * improvementPerEvent,
            });
        }
        _db.SaveChanges();
    }

    [Test]
    public async Task Handle_TwoFinishedEvents_ReturnsPrediction()
    {
        // Arrange
        SeedEvents(2);
        var query = new GetPredictionQuery(UserId, "HYROX");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.EventType).IsEqualTo("HYROX");
        await Assert.That(result.PredictedSecs).IsGreaterThan(0);
        await Assert.That(result.DataPoints.Count).IsEqualTo(3); // 2 historical + 1 projected
        await Assert.That(result.DataPoints.Last().EventId).IsNull();
        await Assert.That(result.DataPoints.Last().ElapsedSecs).IsNull();
    }

    [Test]
    public async Task Handle_ThreeEventsWithConsistentImprovement_ReturnsHighConfidence()
    {
        // Arrange
        SeedEvents(3);
        var query = new GetPredictionQuery(UserId, "HYROX");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.ConfidenceLabel).IsEqualTo("High");
    }

    [Test]
    public async Task Handle_OneFinishedEvent_ReturnsNull()
    {
        // Arrange
        SeedEvents(1);
        var query = new GetPredictionQuery(UserId, "HYROX");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Handle_ZeroFinishedEvents_ReturnsNull()
    {
        // Arrange
        _db.Events.Add(new Event
        {
            UserId = UserId,
            EventType = EventType.Hyrox,
            EventName = "HYROX DNF",
            EventDate = new DateOnly(2023, 1, 1),
            Completion = CompletionStatus.Dnf,
            ElapsedSecs = 9999,
        });
        _db.SaveChanges();
        var query = new GetPredictionQuery(UserId, "HYROX");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Handle_AllEventsOnSameDate_ReturnsNull()
    {
        // Arrange — two events on the same date (zero X spread)
        for (int i = 0; i < 2; i++)
        {
            _db.Events.Add(new Event
            {
                UserId = UserId,
                EventType = EventType.Hyrox,
                EventName = $"HYROX {i}",
                EventDate = new DateOnly(2024, 6, 1),
                Completion = CompletionStatus.Finished,
                ElapsedSecs = 4800 - i * 100,
            });
        }
        _db.SaveChanges();
        var query = new GetPredictionQuery(UserId, "HYROX");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Handle_UnknownEventType_ReturnsNull()
    {
        // Arrange
        SeedEvents(3);
        var query = new GetPredictionQuery(UserId, "UNKNOWN_TYPE");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Handle_AvgImprovementSecs_IsCorrect()
    {
        // Arrange: 5000 → 4800 → 4600 (each 200s faster)
        SeedEvents(3, startSecs: 5000, improvementPerEvent: 200);
        var query = new GetPredictionQuery(UserId, "HYROX");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.AvgImprovementSecs).IsEqualTo(200);
    }

    [Test]
    public async Task Handle_PredictionClampedToFloor_ReturnsLowConfidence()
    {
        // Arrange: extreme improvement trend that would predict below 3000s (Hyrox floor)
        for (int i = 0; i < 3; i++)
        {
            _db.Events.Add(new Event
            {
                UserId = UserId,
                EventType = EventType.Hyrox,
                EventName = $"HYROX {i}",
                EventDate = new DateOnly(2020, 1, 1).AddMonths(i),
                Completion = CompletionStatus.Finished,
                ElapsedSecs = 5000 - i * 1500, // absurdly steep slope
            });
        }
        _db.SaveChanges();
        var query = new GetPredictionQuery(UserId, "HYROX");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PredictedSecs).IsGreaterThanOrEqualTo(3000);
        await Assert.That(result.ConfidenceLabel).IsEqualTo("Low");
    }
}
```

- [ ] **Step 4: Run tests to confirm they fail**

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --filter "Category=Unit"
```

Expected: FAIL — `GetPredictionHandler` does not exist.

- [ ] **Step 5: Implement the handler**

```csharp
// src/Pacevite.Api/Features/Events/GetPrediction/GetPredictionHandler.cs
using Mediator;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Persistence;
using Pacevite.Api.Infrastructure.Regression;

namespace Pacevite.Api.Features.Events.GetPrediction;

public sealed class GetPredictionHandler(AppDbContext db)
    : IQueryHandler<GetPredictionQuery, PredictionResponse?>
{
    private static readonly Dictionary<EventType, int> FloorSecs = new()
    {
        [EventType.Hyrox]   = 3000,
        [EventType.Marathon] = 7200,
        [EventType.Spartan]  = 1800,
        [EventType.Generic]  = 60,
    };

    public async ValueTask<PredictionResponse?> Handle(
        GetPredictionQuery query, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<EventType>(query.EventType, ignoreCase: true, out var eventType))
            return null;

        var events = await db.Events
            .Where(e => e.UserId == query.UserId
                     && e.EventType == eventType
                     && e.Completion == CompletionStatus.Finished)
            .OrderBy(e => e.EventDate)
            .ToListAsync(cancellationToken);

        if (events.Count < 2)
            return null;

        var firstDate = events[0].EventDate.ToDateTime(TimeOnly.MinValue);

        var regressionPoints = events
            .Select(e => (
                (double)(e.EventDate.ToDateTime(TimeOnly.MinValue) - firstDate).TotalDays,
                (double)e.ElapsedSecs))
            .ToList();

        if (regressionPoints.All(p => p.Item1 == 0))
            return null;

        var regression = LinearRegression.Fit(regressionPoints);
        var todayDays  = (DateTime.UtcNow.Date - firstDate).TotalDays;
        var rawPredicted = LinearRegression.Predict(regression, todayDays);

        int floor   = FloorSecs[eventType];
        bool clamped = rawPredicted < floor;
        int predictedSecs = (int)Math.Max(rawPredicted, floor);

        var confidence       = DeriveConfidence(regression.RSquared, events.Count, clamped);
        int avgImprovement   = ComputeAvgImprovement(events.Select(e => e.ElapsedSecs).ToList());

        var dataPoints = events
            .Select(e =>
            {
                var days   = (e.EventDate.ToDateTime(TimeOnly.MinValue) - firstDate).TotalDays;
                var fitted = (int)LinearRegression.Predict(regression, days);
                return new PredictionDataPoint(e.Id, e.EventDate, e.ElapsedSecs, fitted);
            })
            .Append(new PredictionDataPoint(
                null,
                DateOnly.FromDateTime(DateTime.UtcNow),
                null,
                predictedSecs))
            .ToList();

        return new PredictionResponse(
            query.EventType.ToUpperInvariant(),
            predictedSecs,
            confidence,
            avgImprovement,
            dataPoints);
    }

    private static string DeriveConfidence(double rSquared, int count, bool clamped)
    {
        if (clamped) return "Low";
        if (rSquared >= 0.85 && count >= 3) return "High";
        if (rSquared >= 0.60 || count == 2) return "Medium";
        return "Low";
    }

    private static int ComputeAvgImprovement(IReadOnlyList<int> elapsed)
    {
        if (elapsed.Count < 2) return 0;
        var deltas = Enumerable.Range(1, elapsed.Count - 1)
            .Select(i => elapsed[i - 1] - elapsed[i]);
        return (int)deltas.Average();
    }
}
```

- [ ] **Step 6: Implement the validator**

```csharp
// src/Pacevite.Api/Features/Events/GetPrediction/GetPredictionValidator.cs
using FluentValidation;
using Pacevite.Api.Domain.Enums;

namespace Pacevite.Api.Features.Events.GetPrediction;

public sealed class GetPredictionValidator : AbstractValidator<GetPredictionQuery>
{
    private static readonly HashSet<string> ValidEventTypes =
        Enum.GetNames<EventType>().ToHashSet(StringComparer.OrdinalIgnoreCase);

    public GetPredictionValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.EventType)
            .NotEmpty()
            .Must(t => ValidEventTypes.Contains(t))
            .WithMessage(x =>
                $"'{x.EventType}' is not a valid event type. Valid: {string.Join(", ", Enum.GetNames<EventType>())}");
    }
}
```

- [ ] **Step 7: Run unit tests — all must pass**

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --filter "Category=Unit"
```

Expected: all 13 unit tests green.

- [ ] **Step 8: Commit**

```bash
git add src/Pacevite.Api/Contracts/Responses/PredictionResponse.cs \
        src/Pacevite.Api/Features/Events/GetPrediction/ \
        tests/Pacevite.Api.Tests/Unit/Prediction/GetPredictionHandlerTests.cs
git commit -m "feat: add GetPrediction query, handler, validator, and response contract"
```

---

## Task 3: Register GET /prediction Endpoint + Wire AnthropicClient in DI

**Files:**
- Modify: `src/Pacevite.Api/Features/Events/EventEndpoints.cs`
- Modify: `src/Pacevite.Api/Program.cs`
- Create: `tests/Pacevite.Api.Tests/Integration/PredictionEndpointsTests.cs` (prediction endpoint only — coaching tests added in Task 4)

- [ ] **Step 1: Write failing integration test**

```csharp
// tests/Pacevite.Api.Tests/Integration/PredictionEndpointsTests.cs
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
public sealed class PredictionEndpointsTests
{
    private PostgreSqlContainer _postgres = null!;
    private HttpClient _client = null!;
    private WebApplicationFactory<Program> _factory = null!;

    [Before(Test)]
    public async Task SetUpAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:17")
            .WithDatabase("pacevite_predict_test")
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
                    if (descriptor is not null) services.Remove(descriptor);

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseNpgsql(_postgres.GetConnectionString()));

                    using var scope = services.BuildServiceProvider().CreateScope();
                    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
                });

                host.UseSetting("Jwt:Secret", "super-secret-key-for-testing-only-32c");
                host.UseSetting("Jwt:Issuer", "pacevite-test");
                host.UseSetting("Jwt:Audience", "pacevite-test");
                host.UseSetting("Anthropic:ApiKey", "test-key");
                host.UseSetting("Anthropic:Model", "claude-haiku-4-5-20251001");
                host.UseSetting("Anthropic:MaxTokens", "1024");
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

    private async Task<string> GetTokenAsync(string email = "predict-user@example.com")
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
        return (await login.Content.ReadFromJsonAsync<AuthResponse>())!.Token;
    }

    private async Task UploadHyroxEventsAsync(string token, int count)
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        for (int i = 0; i < count; i++)
        {
            var date = new DateOnly(2023, 1, 1).AddMonths(i * 6);
            var secs = 5000 - i * 300;
            var json = $$"""
                [{"event_type":"HYROX","event_name":"HYROX {{i+1}}","event_date":"{{date:yyyy-MM-dd}}",
                  "completion":"FINISHED","elapsed_secs":{{secs}},"splits":[]}]
                """;
            var content = new MultipartFormDataContent();
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            content.Add(fileContent, "file", "events.json");
            await _client.PostAsync("/api/events/upload", content);
        }
    }

    [Test]
    public async Task GetPrediction_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/api/events/prediction?eventType=HYROX");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetPrediction_TwoFinishedEvents_Returns200WithPrediction()
    {
        var token = await GetTokenAsync("predict-2events@example.com");
        await UploadHyroxEventsAsync(token, 2);

        var response = await _client.GetAsync("/api/events/prediction?eventType=HYROX");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var prediction = await response.Content.ReadFromJsonAsync<PredictionResponse>();
        await Assert.That(prediction).IsNotNull();
        await Assert.That(prediction!.EventType).IsEqualTo("HYROX");
        await Assert.That(prediction.PredictedSecs).IsGreaterThan(0);
        await Assert.That(prediction.DataPoints.Count).IsEqualTo(3);
    }

    [Test]
    public async Task GetPrediction_OneFinishedEvent_Returns409()
    {
        var token = await GetTokenAsync("predict-1event@example.com");
        await UploadHyroxEventsAsync(token, 1);

        var response = await _client.GetAsync("/api/events/prediction?eventType=HYROX");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task GetPrediction_InvalidEventType_Returns400()
    {
        var token = await GetTokenAsync("predict-bad-type@example.com");
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/events/prediction?eventType=TRIATHLON");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 2: Run integration tests — confirm they fail**

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --filter "Category=Integration"
```

Expected: FAIL — `/api/events/prediction` route not registered.

- [ ] **Step 3: Register `AnthropicOptions` and `AnthropicClient` in `Program.cs`**

Add these lines to `Program.cs` in the `// ── Services ──` section (after `AddScoped<IJwtTokenService, JwtTokenService>()`):

```csharp
// ── Anthropic ─────────────────────────────────────────────────────────────────
builder.Services.Configure<AnthropicOptions>(
    builder.Configuration.GetSection(AnthropicOptions.SectionName));

builder.Services.AddScoped<AnthropicClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
    // Verify constructor signature with Anthropic.SDK v5.10.0 via Context7 if needed
    return new AnthropicClient(new APIAuthentication(opts.ApiKey));
});

builder.Services.AddScoped<PredictionCoachingHandler>();
```

Also add these using directives at the top of `Program.cs`:

```csharp
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Microsoft.Extensions.Options;
using Pacevite.Api.Features.Events.PredictionCoaching;
using Pacevite.Api.Infrastructure.Chat;
```

- [ ] **Step 4: Add the prediction route to `EventEndpoints.cs`**

Add to the `MapEventEndpoints` method (before `return app;`):

```csharp
app.MapGet("/prediction", GetPredictionAsync).WithName("GetPrediction");
app.MapGet("/prediction/coaching", GetPredictionCoachingAsync).WithName("GetPredictionCoaching");
```

Add these using statements at the top of `EventEndpoints.cs`:

```csharp
using Pacevite.Api.Features.Events.GetPrediction;
using Pacevite.Api.Features.Events.PredictionCoaching;
```

Add these two private static methods to the `EventEndpoints` class:

```csharp
private static async Task<Results<Ok<PredictionResponse>, Conflict<object>>> GetPredictionAsync(
    ClaimsPrincipal user,
    IMediator mediator,
    CancellationToken ct,
    string eventType = "")
{
    var userId = GetUserId(user);
    var result = await mediator.Send(new GetPredictionQuery(userId, eventType), ct);
    return result is null
        ? TypedResults.Conflict((object)new { message = $"Need at least 2 finished {eventType.ToUpperInvariant()} events to predict" })
        : TypedResults.Ok(result);
}

private static async Task GetPredictionCoachingAsync(
    HttpContext context,
    ClaimsPrincipal user,
    PredictionCoachingHandler handler,
    CancellationToken ct,
    string eventType = "")
{
    await handler.HandleAsync(context, GetUserId(user), eventType, ct);
}
```

- [ ] **Step 5: Create a stub `PredictionCoachingHandler` so the project compiles**

```csharp
// src/Pacevite.Api/Features/Events/PredictionCoaching/PredictionCoachingHandler.cs
using Pacevite.Api.Infrastructure.Chat;

namespace Pacevite.Api.Features.Events.PredictionCoaching;

public sealed class PredictionCoachingHandler
{
    public Task HandleAsync(HttpContext context, string userId, string eventType, CancellationToken ct)
    {
        // Implemented in Task 4
        context.Response.StatusCode = 501;
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 6: Confirm the project builds**

```bash
dotnet build src/Pacevite.Api
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 7: Run integration tests — all must pass**

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --filter "Category=Integration"
```

Expected: all 4 new prediction tests green (coaching endpoint returns 501 but coaching tests haven't been written yet).

- [ ] **Step 8: Commit**

```bash
git add src/Pacevite.Api/Features/Events/EventEndpoints.cs \
        src/Pacevite.Api/Features/Events/PredictionCoaching/PredictionCoachingHandler.cs \
        src/Pacevite.Api/Program.cs \
        tests/Pacevite.Api.Tests/Integration/PredictionEndpointsTests.cs
git commit -m "feat: register GET /prediction endpoint and wire AnthropicClient in DI"
```

---

## Task 4: PredictionCoaching SSE Handler + Integration Tests

**Files:**
- Modify: `src/Pacevite.Api/Features/Events/PredictionCoaching/PredictionCoachingHandler.cs`
- Modify: `tests/Pacevite.Api.Tests/Integration/PredictionEndpointsTests.cs`

**Note on Anthropic.SDK v5.10.0 streaming API:** Before implementing Step 3, run `mcp__plugin_context7_context7__resolve-library-id` with `"Anthropic.SDK"` and then `mcp__plugin_context7_context7__query-docs` to confirm the exact method name and parameter shape for streaming messages. The implementation below uses the expected pattern — verify it matches v5.10.0.

- [ ] **Step 1: Add coaching integration tests (failing)**

Add these tests to `tests/Pacevite.Api.Tests/Integration/PredictionEndpointsTests.cs`:

```csharp
[Test]
public async Task GetPredictionCoaching_Unauthenticated_Returns401()
{
    var response = await _client.GetAsync(
        "/api/events/prediction/coaching?eventType=HYROX",
        HttpCompletionOption.ResponseHeadersRead);
    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
}

[Test]
public async Task GetPredictionCoaching_InsufficientData_Returns409()
{
    var token = await GetTokenAsync("coaching-1event@example.com");
    await UploadHyroxEventsAsync(token, 1);

    var response = await _client.GetAsync(
        "/api/events/prediction/coaching?eventType=HYROX",
        HttpCompletionOption.ResponseHeadersRead);

    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
}

[Test]
public async Task GetPredictionCoaching_ValidData_ReturnsEventStream()
{
    var token = await GetTokenAsync("coaching-2events@example.com");
    await UploadHyroxEventsAsync(token, 2);

    var response = await _client.GetAsync(
        "/api/events/prediction/coaching?eventType=HYROX",
        HttpCompletionOption.ResponseHeadersRead);

    // The coaching call hits the real Anthropic API in integration tests.
    // Skip if no API key is configured (CI environment).
    if (response.StatusCode == HttpStatusCode.InternalServerError) return;

    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    await Assert.That(response.Content.Headers.ContentType?.MediaType)
        .IsEqualTo("text/event-stream");
}
```

- [ ] **Step 2: Run tests — confirm coaching tests fail**

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --filter "Category=Integration"
```

Expected: the two new coaching tests fail (401 test passes, but 409 and event-stream tests fail because stub returns 501).

- [ ] **Step 3: Implement `PredictionCoachingHandler`**

Replace the stub with the full implementation:

```csharp
// src/Pacevite.Api/Features/Events/PredictionCoaching/PredictionCoachingHandler.cs
using System.Text;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Chat;
using Pacevite.Api.Infrastructure.Persistence;
using Pacevite.Api.Infrastructure.Regression;

namespace Pacevite.Api.Features.Events.PredictionCoaching;

public sealed class PredictionCoachingHandler(
    AppDbContext db,
    AnthropicClient anthropic,
    IOptions<AnthropicOptions> options)
{
    private const string SystemPrompt = """
        You are a performance coach for endurance and functional fitness events.
        Analyse the athlete's split-level trends across their race history.
        For each split, note whether it is improving, plateauing, or declining.
        Identify the 2-3 biggest opportunities for time savings.
        Be specific: name the station/segment, quantify the trend, and give one actionable coaching cue per opportunity.
        Keep the total response under 300 words. Use plain text, no markdown headers.
        """;

    public async Task HandleAsync(
        HttpContext httpContext,
        string userId,
        string eventType,
        CancellationToken ct)
    {
        if (!Enum.TryParse<EventType>(eventType, ignoreCase: true, out var parsedType))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var events = await db.Events
            .Include(e => e.Splits.OrderBy(s => s.CumulativeSecs))
            .Where(e => e.UserId == userId
                     && e.EventType == parsedType
                     && e.Completion == Domain.Enums.CompletionStatus.Finished)
            .OrderBy(e => e.EventDate)
            .ToListAsync(ct);

        if (events.Count < 2)
        {
            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        var firstDate = events[0].EventDate.ToDateTime(TimeOnly.MinValue);
        var points    = events
            .Select(e => (
                (double)(e.EventDate.ToDateTime(TimeOnly.MinValue) - firstDate).TotalDays,
                (double)e.ElapsedSecs))
            .ToList();

        var regression    = LinearRegression.Fit(points);
        var todayDays     = (DateTime.UtcNow.Date - firstDate).TotalDays;
        var predictedSecs = (int)LinearRegression.Predict(regression, todayDays);

        var userMessage = BuildUserMessage(events, eventType, predictedSecs);

        httpContext.Response.Headers.Append("Content-Type", "text/event-stream");
        httpContext.Response.Headers.Append("Cache-Control", "no-cache");
        httpContext.Response.Headers.Append("X-Accel-Buffering", "no");

        try
        {
            // Verify parameter names against Anthropic.SDK v5.10.0 via Context7
            var parameters = new MessageParameters
            {
                Model     = options.Value.Model,
                MaxTokens = options.Value.MaxTokens,
                System    = [new SystemMessage(SystemPrompt)],
                Messages  = [new Message(RoleType.User, userMessage)],
                Stream    = true,
            };

            await foreach (var chunk in anthropic.Messages.StreamClaudeMessageAsync(parameters, ct))
            {
                if (chunk.Delta?.Type == "text_delta" && chunk.Delta.Text is not null)
                    await WriteSseEventAsync(httpContext.Response, SseEvent.Delta(chunk.Delta.Text), ct);
            }

            await WriteSseEventAsync(httpContext.Response, SseEvent.Done(), ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await WriteSseEventAsync(
                httpContext.Response,
                SseEvent.Error("Coaching analysis failed. Please try again."),
                ct);
        }
    }

    private static string BuildUserMessage(
        IReadOnlyList<Domain.Entities.Event> events,
        string eventType,
        int predictedSecs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Here are my {eventType.ToUpperInvariant()} race results, oldest to newest:");
        sb.AppendLine();

        foreach (var ev in events)
        {
            sb.AppendLine($"{ev.EventName} — {ev.EventDate:yyyy-MM-dd} — {FormatTime(ev.ElapsedSecs)}");
            foreach (var split in ev.Splits)
                sb.AppendLine($"  {split.SplitLabel}: {FormatTime(split.SplitSecs)}");
            sb.AppendLine();
        }

        sb.AppendLine($"Algorithmic prediction for my next race: {FormatTime(predictedSecs)}");
        sb.AppendLine();
        sb.AppendLine("Please analyse my split trends and tell me where my next time savings are.");
        return sb.ToString();
    }

    private static async Task WriteSseEventAsync(HttpResponse response, SseEvent evt, CancellationToken ct)
    {
        await response.WriteAsync($"event: {evt.Type}\ndata: {evt.Data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    private static string FormatTime(int secs)
    {
        int h = secs / 3600, m = (secs % 3600) / 60, s = secs % 60;
        return h > 0 ? $"{h}:{m:D2}:{s:D2}" : $"{m}:{s:D2}";
    }
}
```

- [ ] **Step 4: Run all tests**

```bash
dotnet run --project tests/Pacevite.Api.Tests
```

Expected: all tests pass. The `GetPredictionCoaching_ValidData_ReturnsEventStream` test self-skips in CI if the API key is a stub.

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Api/Features/Events/PredictionCoaching/PredictionCoachingHandler.cs \
        tests/Pacevite.Api.Tests/Integration/PredictionEndpointsTests.cs
git commit -m "feat: implement PredictionCoaching SSE handler streaming Claude coaching analysis"
```

---

## Task 5: Frontend Types + usePrediction Hook + MSW Handler

**Files:**
- Modify: `src/Pacevite.Web/src/lib/types.ts`
- Create: `src/Pacevite.Web/src/hooks/usePrediction.ts`
- Modify: `src/Pacevite.Web/src/test/handlers.ts`

- [ ] **Step 1: Add types to `types.ts`**

Add these interfaces to `src/Pacevite.Web/src/lib/types.ts` (after `PersonalBestResponse`):

```typescript
export interface PredictionDataPoint {
  eventId: string | null
  eventDate: string
  elapsedSecs: number | null
  fittedSecs: number
}

export interface PredictionResponse {
  eventType: string
  predictedSecs: number
  confidenceLabel: string
  avgImprovementSecs: number
  dataPoints: PredictionDataPoint[]
}
```

- [ ] **Step 2: Create `usePrediction` hook**

```typescript
// src/Pacevite.Web/src/hooks/usePrediction.ts
import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api'
import type { PredictionResponse } from '@/lib/types'

export function usePrediction(eventType: string | null) {
  return useQuery<PredictionResponse>({
    queryKey: ['prediction', eventType],
    queryFn: async () => {
      const { data } = await apiClient.get<PredictionResponse>(
        `/events/prediction?eventType=${eventType}`
      )
      return data
    },
    enabled: eventType !== null,
    retry: false,
  })
}
```

- [ ] **Step 3: Add MSW handler for `/api/events/prediction`**

Add to `src/Pacevite.Web/src/test/handlers.ts` — insert **before** the `/api/events/:id` handler (MSW matches static paths before dynamic):

```typescript
http.get('http://localhost/api/events/prediction', () =>
  HttpResponse.json({
    eventType: 'HYROX',
    predictedSecs: 4320,
    confidenceLabel: 'High',
    avgImprovementSecs: 215,
    dataPoints: [
      { eventId: 'event-1', eventDate: '2023-10-14', elapsedSecs: 4930, fittedSecs: 4920 },
      { eventId: 'event-2', eventDate: '2024-03-09', elapsedSecs: 4724, fittedSecs: 4710 },
      { eventId: 'event-3', eventDate: '2024-11-16', elapsedSecs: 4501, fittedSecs: 4508 },
      { eventId: null,      eventDate: '2026-04-25', elapsedSecs: null,  fittedSecs: 4320 },
    ],
  })
),
```

- [ ] **Step 4: Run frontend tests — all must still pass**

```bash
cd src/Pacevite.Web && npm test
```

Expected: all existing tests green (new types/hook not yet tested but don't break anything).

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Web/src/lib/types.ts \
        src/Pacevite.Web/src/hooks/usePrediction.ts \
        src/Pacevite.Web/src/test/handlers.ts
git commit -m "feat: add PredictionResponse types, usePrediction hook, and MSW handler"
```

---

## Task 6: PredictionCard Component

**Files:**
- Create: `src/Pacevite.Web/src/components/PredictionCard.tsx`
- Create: `src/Pacevite.Web/src/components/PredictionCard.test.tsx`

- [ ] **Step 1: Write failing tests**

```typescript
// src/Pacevite.Web/src/components/PredictionCard.test.tsx
import { screen } from '@testing-library/react'
import { renderWithProviders } from '@/test/render'
import { PredictionCard } from './PredictionCard'
import type { PredictionResponse } from '@/lib/types'
import { describe, it, expect } from 'vitest'

const prediction: PredictionResponse = {
  eventType: 'HYROX',
  predictedSecs: 4320,
  confidenceLabel: 'High',
  avgImprovementSecs: 215,
  dataPoints: [],
}

describe('PredictionCard', () => {
  it('renders predicted time formatted correctly', () => {
    renderWithProviders(<PredictionCard prediction={prediction} />, { authenticated: true })
    // 4320s = 1:12:00
    expect(screen.getByTestId('prediction-time')).toHaveTextContent('1:12:00')
  })

  it('renders confidence badge', () => {
    renderWithProviders(<PredictionCard prediction={prediction} />, { authenticated: true })
    expect(screen.getByTestId('confidence-badge')).toHaveTextContent('High confidence')
  })

  it('renders avg improvement', () => {
    renderWithProviders(<PredictionCard prediction={prediction} />, { authenticated: true })
    // 215s = 3:35
    expect(screen.getByTestId('avg-improvement')).toHaveTextContent('3:35')
  })

  it('renders Medium confidence badge with different style', () => {
    const medium = { ...prediction, confidenceLabel: 'Medium' }
    renderWithProviders(<PredictionCard prediction={medium} />, { authenticated: true })
    const badge = screen.getByTestId('confidence-badge')
    expect(badge).toHaveTextContent('Medium confidence')
    expect(badge.className).toContain('bg-yellow')
  })
})
```

- [ ] **Step 2: Run tests — confirm they fail**

```bash
cd src/Pacevite.Web && npm test
```

Expected: FAIL — `PredictionCard` not found.

- [ ] **Step 3: Implement `PredictionCard`**

```typescript
// src/Pacevite.Web/src/components/PredictionCard.tsx
import type { PredictionResponse } from '@/lib/types'
import { formatElapsed } from '@/lib/chartUtils'

interface Props {
  prediction: PredictionResponse
}

const confidenceStyles: Record<string, string> = {
  High:   'bg-green-100 text-green-800',
  Medium: 'bg-yellow-100 text-yellow-800',
  Low:    'bg-red-100 text-red-800',
}

export function PredictionCard({ prediction }: Props) {
  const badgeClass = confidenceStyles[prediction.confidenceLabel] ?? confidenceStyles.Low

  return (
    <div className="bg-surface border border-border rounded-xl p-5 flex flex-col gap-3">
      <p className="text-xs font-semibold text-secondary uppercase tracking-widest">
        Next {prediction.eventType}
      </p>

      <p data-testid="prediction-time" className="text-4xl font-bold text-primary leading-none">
        {formatElapsed(prediction.predictedSecs)}
      </p>

      <div className="flex items-center gap-2">
        <span
          data-testid="confidence-badge"
          className={`text-xs font-semibold px-2 py-0.5 rounded-full ${badgeClass}`}
        >
          {prediction.confidenceLabel} confidence
        </span>
      </div>

      <div className="pt-3 border-t border-border grid grid-cols-2 gap-3">
        <div>
          <p className="text-xs text-secondary">Avg improvement</p>
          <p data-testid="avg-improvement" className="text-sm font-semibold text-green-600">
            ↓ {formatElapsed(prediction.avgImprovementSecs)} / race
          </p>
        </div>
      </div>
    </div>
  )
}
```

- [ ] **Step 4: Run tests — all must pass**

```bash
cd src/Pacevite.Web && npm test
```

Expected: all 4 `PredictionCard` tests green.

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Web/src/components/PredictionCard.tsx \
        src/Pacevite.Web/src/components/PredictionCard.test.tsx
git commit -m "feat: add PredictionCard component"
```

---

## Task 7: PredictionChart Component

**Files:**
- Create: `src/Pacevite.Web/src/components/PredictionChart.tsx`
- Create: `src/Pacevite.Web/src/components/PredictionChart.test.tsx`

- [ ] **Step 1: Write failing tests**

```typescript
// src/Pacevite.Web/src/components/PredictionChart.test.tsx
import { screen } from '@testing-library/react'
import { renderWithProviders } from '@/test/render'
import { PredictionChart } from './PredictionChart'
import type { PredictionDataPoint } from '@/lib/types'
import { describe, it, expect } from 'vitest'

const dataPoints: PredictionDataPoint[] = [
  { eventId: 'e1', eventDate: '2023-10-14', elapsedSecs: 4930, fittedSecs: 4920 },
  { eventId: 'e2', eventDate: '2024-03-09', elapsedSecs: 4724, fittedSecs: 4710 },
  { eventId: null, eventDate: '2026-04-25', elapsedSecs: null,  fittedSecs: 4320 },
]

describe('PredictionChart', () => {
  it('renders chart container', () => {
    renderWithProviders(<PredictionChart dataPoints={dataPoints} />, { authenticated: true })
    expect(screen.getByTestId('prediction-chart')).toBeInTheDocument()
  })

  it('renders empty message when no historical data', () => {
    renderWithProviders(<PredictionChart dataPoints={[]} />, { authenticated: true })
    expect(screen.getByTestId('prediction-chart-empty')).toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run tests — confirm they fail**

```bash
cd src/Pacevite.Web && npm test
```

Expected: FAIL — `PredictionChart` not found.

- [ ] **Step 3: Implement `PredictionChart`**

```typescript
// src/Pacevite.Web/src/components/PredictionChart.tsx
import { ComposedChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer } from 'recharts'
import type { PredictionDataPoint } from '@/lib/types'
import { formatElapsed } from '@/lib/chartUtils'
import { useTheme } from '@/context/ThemeContext'

interface Props {
  dataPoints: PredictionDataPoint[]
}

export function PredictionChart({ dataPoints }: Props) {
  useTheme()

  const style     = getComputedStyle(document.documentElement)
  const tickColor = style.getPropertyValue('--color-secondary').trim()
  const tooltipBg = style.getPropertyValue('--color-surface').trim()

  const historical = dataPoints.filter(p => p.eventId !== null)

  if (historical.length === 0) {
    return (
      <p data-testid="prediction-chart-empty" className="text-xs text-muted py-8 text-center">
        No data
      </p>
    )
  }

  // Combine actual + fitted into one dataset; actual is null for the projected point
  const data = dataPoints.map(p => ({
    date:       p.eventDate,
    actual:     p.elapsedSecs ?? undefined,
    fitted:     p.fittedSecs,
    isProjected: p.eventId === null,
  }))

  return (
    <div data-testid="prediction-chart">
      <ResponsiveContainer width="100%" height={160}>
        <ComposedChart data={data} margin={{ top: 4, right: 4, bottom: 4, left: 40 }}>
          <XAxis dataKey="date" tick={{ fontSize: 10, fill: tickColor }} tickLine={false} />
          <YAxis
            tickFormatter={v => formatElapsed(typeof v === 'number' ? v : 0)}
            tick={{ fontSize: 10, fill: tickColor }}
            tickLine={false}
            axisLine={false}
            reversed
            domain={['auto', 'auto']}
          />
          <Tooltip
            formatter={(v) => [formatElapsed(typeof v === 'number' ? v : 0), '']}
            contentStyle={{ background: tooltipBg, border: 'none', fontSize: 12 }}
          />
          {/* Dashed regression/projection line through all points */}
          <Line
            type="monotone"
            dataKey="fitted"
            stroke="#6366f1"
            strokeWidth={2}
            strokeDasharray="6 4"
            dot={false}
            opacity={0.6}
          />
          {/* Solid actual times (undefined gap at projected point) */}
          <Line
            type="monotone"
            dataKey="actual"
            stroke="#6366f1"
            strokeWidth={2}
            connectNulls={false}
            dot={({ cx, cy }) => (
              <circle key={`${cx}-${cy}`} cx={cx} cy={cy} r={4} fill="#6366f1" />
            )}
          />
        </ComposedChart>
      </ResponsiveContainer>
    </div>
  )
}
```

- [ ] **Step 4: Run tests — all must pass**

```bash
cd src/Pacevite.Web && npm test
```

Expected: all `PredictionChart` tests green.

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Web/src/components/PredictionChart.tsx \
        src/Pacevite.Web/src/components/PredictionChart.test.tsx
git commit -m "feat: add PredictionChart component with dashed projection trend line"
```

---

## Task 8: PredictionCoaching Component

**Files:**
- Create: `src/Pacevite.Web/src/components/PredictionCoaching.tsx`
- Create: `src/Pacevite.Web/src/components/PredictionCoaching.test.tsx`

- [ ] **Step 1: Write failing tests**

```typescript
// src/Pacevite.Web/src/components/PredictionCoaching.test.tsx
import { screen, fireEvent } from '@testing-library/react'
import { renderWithProviders } from '@/test/render'
import { PredictionCoaching } from './PredictionCoaching'
import { describe, it, expect, vi, beforeEach } from 'vitest'

describe('PredictionCoaching', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  it('renders generate button initially', () => {
    renderWithProviders(<PredictionCoaching eventType="HYROX" />, { authenticated: true })
    expect(screen.getByRole('button', { name: /generate/i })).toBeInTheDocument()
  })

  it('does not show coaching text before generation', () => {
    renderWithProviders(<PredictionCoaching eventType="HYROX" />, { authenticated: true })
    expect(screen.queryByTestId('coaching-text')).not.toBeInTheDocument()
  })

  it('shows loading state while streaming', async () => {
    const neverResolves = new Promise<Response>(() => {})
    vi.mocked(fetch).mockReturnValue(neverResolves)

    renderWithProviders(<PredictionCoaching eventType="HYROX" />, { authenticated: true })
    fireEvent.click(screen.getByRole('button', { name: /generate/i }))

    expect(await screen.findByText(/generating/i)).toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run tests — confirm they fail**

```bash
cd src/Pacevite.Web && npm test
```

Expected: FAIL — `PredictionCoaching` not found.

- [ ] **Step 3: Implement `PredictionCoaching`**

```typescript
// src/Pacevite.Web/src/components/PredictionCoaching.tsx
import { useState } from 'react'
import { tokenStore } from '@/lib/api'
import { Sparkles } from 'lucide-react'

interface Props {
  eventType: string
}

export function PredictionCoaching({ eventType }: Props) {
  const [coachingText, setCoachingText] = useState('')
  const [isStreaming, setIsStreaming]   = useState(false)
  const [error, setError]              = useState<string | null>(null)
  const [generated, setGenerated]      = useState(false)

  async function handleGenerate() {
    setIsStreaming(true)
    setCoachingText('')
    setError(null)
    setGenerated(true)

    const token = tokenStore.get()

    try {
      const response = await fetch(
        `/api/events/prediction/coaching?eventType=${eventType}`,
        { headers: token ? { Authorization: `Bearer ${token}` } : {} }
      )

      if (!response.ok || !response.body) {
        setError('Failed to generate coaching analysis. Please try again.')
        return
      }

      const reader  = response.body.getReader()
      const decoder = new TextDecoder()
      let buffer    = ''
      let eventType_ = ''

      while (true) {
        const { done, value } = await reader.read()
        if (done) break

        buffer += decoder.decode(value, { stream: true })
        const lines = buffer.split('\n')
        buffer = lines.pop() ?? ''

        for (const line of lines) {
          if (line.startsWith('event: ')) {
            eventType_ = line.slice(7).trim()
          } else if (line.startsWith('data: ')) {
            const raw = line.slice(6)
            if (eventType_ === 'delta') {
              const parsed = JSON.parse(raw) as { text: string }
              setCoachingText(prev => prev + parsed.text)
            } else if (eventType_ === 'done') {
              setIsStreaming(false)
            } else if (eventType_ === 'error') {
              const parsed = JSON.parse(raw) as { message: string }
              setError(parsed.message)
              setIsStreaming(false)
            }
            eventType_ = ''
          }
        }
      }
    } catch {
      setError('Coaching analysis failed. Please try again.')
    } finally {
      setIsStreaming(false)
    }
  }

  return (
    <div className="bg-surface border border-border rounded-xl p-5">
      <div className="flex items-center justify-between mb-2">
        <h3 className="text-sm font-semibold text-primary flex items-center gap-2">
          <Sparkles size={14} className="text-indigo-500" /> AI Coaching Analysis
        </h3>
        {!isStreaming && (
          <button
            onClick={handleGenerate}
            className="inline-flex items-center gap-1.5 bg-action text-action-fg text-xs px-3 py-1.5 rounded-md hover:bg-action-hover"
          >
            {generated ? 'Regenerate' : 'Generate analysis'}
          </button>
        )}
        {isStreaming && (
          <span className="text-xs text-secondary">Generating…</span>
        )}
      </div>

      {!generated && (
        <p className="text-sm text-secondary">
          Claude will analyse your split history and highlight where your next minutes are hiding.
        </p>
      )}

      {error && (
        <p className="text-sm text-red-500 mt-2">{error}</p>
      )}

      {coachingText && (
        <p data-testid="coaching-text" className="text-sm text-primary leading-relaxed whitespace-pre-wrap mt-2">
          {coachingText}
          {isStreaming && (
            <span className="inline-block w-2 h-4 bg-indigo-500 ml-0.5 align-text-bottom rounded-sm animate-pulse" />
          )}
        </p>
      )}
    </div>
  )
}
```

- [ ] **Step 4: Run tests — all must pass**

```bash
cd src/Pacevite.Web && npm test
```

Expected: all 3 `PredictionCoaching` tests green.

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Web/src/components/PredictionCoaching.tsx \
        src/Pacevite.Web/src/components/PredictionCoaching.test.tsx
git commit -m "feat: add PredictionCoaching streaming component"
```

---

## Task 9: PredictionTeaser Component

**Files:**
- Create: `src/Pacevite.Web/src/components/PredictionTeaser.tsx`
- Create: `src/Pacevite.Web/src/components/PredictionTeaser.test.tsx`

- [ ] **Step 1: Write failing tests**

```typescript
// src/Pacevite.Web/src/components/PredictionTeaser.test.tsx
import { screen } from '@testing-library/react'
import { renderWithProviders } from '@/test/render'
import { PredictionTeaser } from './PredictionTeaser'
import { describe, it, expect } from 'vitest'

// MSW handler returns HYROX prediction when /api/events returns events
// and /api/events/prediction returns the fixture defined in handlers.ts

describe('PredictionTeaser', () => {
  it('renders predicted time and link when events exist', async () => {
    renderWithProviders(<PredictionTeaser />, {
      authenticated: true,
      initialEntries: ['/dashboard'],
    })
    // MSW returns MARATHON event for /api/events and HYROX prediction for /api/events/prediction
    expect(await screen.findByTestId('prediction-teaser')).toBeInTheDocument()
    expect(screen.getByTestId('teaser-link')).toHaveAttribute('href', '/predict')
  })

  it('renders nothing while loading', () => {
    renderWithProviders(<PredictionTeaser />, { authenticated: true })
    expect(screen.queryByTestId('prediction-teaser')).not.toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run tests — confirm they fail**

```bash
cd src/Pacevite.Web && npm test
```

Expected: FAIL — `PredictionTeaser` not found.

- [ ] **Step 3: Implement `PredictionTeaser`**

```typescript
// src/Pacevite.Web/src/components/PredictionTeaser.tsx
import { useMemo } from 'react'
import { Link } from 'react-router-dom'
import { useEvents } from '@/hooks/useEvents'
import { usePrediction } from '@/hooks/usePrediction'
import { formatElapsed } from '@/lib/chartUtils'
import { TrendingDown } from 'lucide-react'

export function PredictionTeaser() {
  const { data: events = [], isLoading: eventsLoading } = useEvents()

  const mostRecentType = useMemo(() => {
    const sorted = [...events].sort((a, b) => b.eventDate.localeCompare(a.eventDate))
    return sorted[0]?.eventType ?? null
  }, [events])

  const { data: prediction, isLoading: predLoading, isError } = usePrediction(mostRecentType)

  if (eventsLoading || predLoading || isError || !prediction) return null

  return (
    <div
      data-testid="prediction-teaser"
      className="bg-surface border border-border rounded-xl p-4 flex items-center justify-between"
    >
      <div>
        <p className="text-xs font-semibold text-secondary uppercase tracking-widest">
          Predicted next {prediction.eventType}
        </p>
        <p className="text-2xl font-bold text-primary mt-0.5">
          {formatElapsed(prediction.predictedSecs)}
        </p>
        <p className="text-xs text-green-600 mt-0.5 flex items-center gap-1">
          <TrendingDown size={11} />
          ↓ {formatElapsed(prediction.avgImprovementSecs)} avg / race
        </p>
      </div>
      <Link
        data-testid="teaser-link"
        to="/predict"
        className="text-sm font-medium text-indigo-600 hover:text-indigo-800"
      >
        Full analysis →
      </Link>
    </div>
  )
}
```

- [ ] **Step 4: Run tests — all must pass**

```bash
cd src/Pacevite.Web && npm test
```

Expected: all `PredictionTeaser` tests green.

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Web/src/components/PredictionTeaser.tsx \
        src/Pacevite.Web/src/components/PredictionTeaser.test.tsx
git commit -m "feat: add PredictionTeaser dashboard widget"
```

---

## Task 10: PredictPage + App.tsx Route

**Files:**
- Create: `src/Pacevite.Web/src/pages/PredictPage.tsx`
- Modify: `src/Pacevite.Web/src/App.tsx`

- [ ] **Step 1: Create `PredictPage`**

```typescript
// src/Pacevite.Web/src/pages/PredictPage.tsx
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '@/hooks/useAuth'
import { useEvents } from '@/hooks/useEvents'
import { usePrediction } from '@/hooks/usePrediction'
import { ThemeToggle } from '@/components/ThemeToggle'
import { PredictionCard } from '@/components/PredictionCard'
import { PredictionChart } from '@/components/PredictionChart'
import { PredictionCoaching } from '@/components/PredictionCoaching'
import { Upload, LogOut, ChartNoAxesColumn } from 'lucide-react'

export function PredictPage() {
  const { user, logout } = useAuth()
  const { data: events = [], isLoading: eventsLoading } = useEvents()

  const eligibleTypes = useMemo(() => {
    const counts: Record<string, number> = {}
    for (const ev of events) {
      if (ev.completion === 'FINISHED') {
        counts[ev.eventType] = (counts[ev.eventType] ?? 0) + 1
      }
    }
    return Object.entries(counts)
      .filter(([, n]) => n >= 2)
      .map(([type]) => type)
  }, [events])

  const [selectedType, setSelectedType] = useState<string | null>(null)
  const activeType = selectedType ?? eligibleTypes[0] ?? null

  const { data: prediction, isLoading: predLoading, isError } = usePrediction(activeType)

  return (
    <div className="min-h-screen bg-bg">
      {/* Nav */}
      <header className="bg-surface border-b border-border px-6 py-4 flex items-center justify-between">
        <h1 className="text-lg font-semibold text-primary">Pacevite</h1>
        <div className="flex items-center gap-4">
          <span className="text-sm text-secondary">{user?.email}</span>
          <Link
            to="/upload"
            className="inline-flex items-center gap-2 bg-action text-action-fg text-sm px-3 py-2 rounded-md hover:bg-action-hover"
          >
            <Upload size={14} /> Upload
          </Link>
          <Link
            to="/predict"
            className="inline-flex items-center gap-2 text-sm font-medium text-indigo-600 hover:text-indigo-800"
          >
            <ChartNoAxesColumn size={14} /> Predict
          </Link>
          <ThemeToggle />
          <button
            onClick={logout}
            className="inline-flex items-center gap-2 text-sm text-secondary hover:text-primary"
          >
            <LogOut size={14} /> Sign out
          </button>
        </div>
      </header>

      <main className="max-w-5xl mx-auto px-6 py-8 space-y-6">
        <div>
          <h2 className="text-xl font-semibold text-primary mb-1">Performance Prediction</h2>
          <p className="text-sm text-secondary">
            Based on your finished events. Select an event type to see your trajectory.
          </p>
        </div>

        {eventsLoading && <p className="text-sm text-secondary">Loading…</p>}

        {!eventsLoading && eligibleTypes.length === 0 && (
          <div className="bg-surface border border-dashed border-border rounded-xl p-12 text-center">
            <p className="text-secondary text-sm">
              You need at least 2 finished events of the same type to generate a prediction.
            </p>
            <Link to="/upload" className="text-sm font-medium text-primary underline mt-2 inline-block">
              Upload events
            </Link>
          </div>
        )}

        {eligibleTypes.length > 0 && (
          <>
            {/* Event type selector */}
            <div className="flex gap-2 flex-wrap">
              {eligibleTypes.map(type => (
                <button
                  key={type}
                  onClick={() => setSelectedType(type)}
                  className={`px-3 py-1.5 rounded-lg text-sm font-medium transition-colors ${
                    activeType === type
                      ? 'bg-action text-action-fg'
                      : 'bg-badge text-badge-fg hover:bg-border'
                  }`}
                >
                  {type}
                </button>
              ))}
            </div>

            {predLoading && <p className="text-sm text-secondary">Calculating prediction…</p>}

            {isError && (
              <p className="text-sm text-red-500">
                Not enough data to predict for {activeType}. Upload more events.
              </p>
            )}

            {prediction && !predLoading && (
              <div className="grid grid-cols-1 lg:grid-cols-[280px_1fr] gap-6 items-start">
                <PredictionCard prediction={prediction} />
                <div className="bg-surface border border-border rounded-xl p-5">
                  <p className="text-sm font-medium text-primary mb-3">Trend</p>
                  <PredictionChart dataPoints={prediction.dataPoints} />
                </div>
              </div>
            )}

            {prediction && (
              <PredictionCoaching eventType={activeType ?? ''} />
            )}
          </>
        )}
      </main>
    </div>
  )
}
```

- [ ] **Step 2: Register `/predict` route in `App.tsx`**

Add the import:

```typescript
import { PredictPage } from '@/pages/PredictPage'
```

Add the route inside the `createBrowserRouter` array (after the `/upload` route):

```typescript
{
  path: '/predict',
  element: (
    <AuthGuard>
      <PredictPage />
    </AuthGuard>
  ),
},
```

- [ ] **Step 3: Run frontend tests — all must still pass**

```bash
cd src/Pacevite.Web && npm test
```

Expected: all existing tests green. `PredictPage` has no dedicated unit tests — it's an integration of tested components.

- [ ] **Step 4: Commit**

```bash
git add src/Pacevite.Web/src/pages/PredictPage.tsx \
        src/Pacevite.Web/src/App.tsx
git commit -m "feat: add PredictPage and /predict route"
```

---

## Task 11: Nav Links + Dashboard PredictionTeaser Integration

**Files:**
- Modify: `src/Pacevite.Web/src/pages/DashboardPage.tsx`
- Modify: `src/Pacevite.Web/src/pages/UploadPage.tsx`
- Modify: `src/Pacevite.Web/src/pages/EventDetailPage.tsx`

- [ ] **Step 1: Add Predict nav link and PredictionTeaser to `DashboardPage.tsx`**

Add to existing imports in `DashboardPage.tsx`:

```typescript
import { PredictionTeaser } from '@/components/PredictionTeaser'
import { ChartNoAxesColumn } from 'lucide-react'
```

In the nav `<div className="flex items-center gap-4">`, add the Predict link after the Upload link (before ThemeToggle):

```typescript
<Link
  to="/predict"
  className="inline-flex items-center gap-2 text-sm text-secondary hover:text-primary"
>
  <ChartNoAxesColumn size={14} /> Predict
</Link>
```

In `<main>`, add `<PredictionTeaser />` as the first element inside the `space-y-8` div (before the `{events.length > 0 && ...}` progress chart section):

```typescript
<PredictionTeaser />
```

- [ ] **Step 2: Add Predict nav link to `UploadPage.tsx`**

Add import:
```typescript
import { ChartNoAxesColumn } from 'lucide-react'
```

In the nav `<div className="flex items-center gap-4">`, add after the existing Dashboard link (or after the Upload heading):

```typescript
<Link
  to="/predict"
  className="inline-flex items-center gap-2 text-sm text-secondary hover:text-primary"
>
  <ChartNoAxesColumn size={14} /> Predict
</Link>
```

- [ ] **Step 3: Add Predict nav link to `EventDetailPage.tsx`**

Apply the same change as Step 2 to `EventDetailPage.tsx`.

- [ ] **Step 4: Run all frontend tests**

```bash
cd src/Pacevite.Web && npm test
```

Expected: all tests green. The existing Dashboard test suite should still pass — `PredictionTeaser` renders null during loading so it doesn't affect existing assertions.

- [ ] **Step 5: Run all API tests**

```bash
dotnet run --project tests/Pacevite.Api.Tests
```

Expected: all tests green.

- [ ] **Step 6: Commit**

```bash
git add src/Pacevite.Web/src/pages/DashboardPage.tsx \
        src/Pacevite.Web/src/pages/UploadPage.tsx \
        src/Pacevite.Web/src/pages/EventDetailPage.tsx
git commit -m "feat: add Predict nav link to all pages and PredictionTeaser to Dashboard"
```

---

## Self-Review

**Spec coverage check:**
- ✅ Linear regression algorithm with R²-based confidence — Task 1 + 2
- ✅ `GET /api/events/prediction` → JSON — Task 3
- ✅ `GET /api/events/prediction/coaching` → SSE — Task 4
- ✅ 409 for < 2 finished events — Task 2 (handler), Task 3 (endpoint), Task 4 (coaching)
- ✅ Floor clamping per event type with Low confidence override — Task 2
- ✅ Projected "today" data point (eventId null) — Task 2
- ✅ AnthropicClient + AnthropicOptions registered in DI — Task 3
- ✅ PredictionCard — Task 6
- ✅ PredictionChart with dashed trend line — Task 7
- ✅ PredictionCoaching streaming via `fetch` + `ReadableStream` (not EventSource) — Task 8
- ✅ PredictionTeaser on Dashboard — Task 9 + 11
- ✅ `/predict` route behind AuthGuard — Task 10
- ✅ Predict nav link on all 3 authenticated pages — Task 11
- ✅ MSW handler — Task 5
- ✅ Unit tests: regression math, handler edge cases — Tasks 1–2
- ✅ Integration tests: 401, 409, 200 shape, SSE content-type — Tasks 3–4
- ✅ Frontend unit tests: PredictionCard, PredictionChart, PredictionCoaching, PredictionTeaser — Tasks 6–9

**Type consistency check:**
- `PredictionDataPoint.eventId` is `Guid?` in C# → `string | null` in TypeScript ✅
- `PredictionDataPoint.elapsedSecs` is `int?` in C# → `number | null` in TypeScript ✅
- `formatElapsed` used everywhere (not `formatTime` — that's the old function in types.ts) ✅
- `usePrediction` enabled only when `eventType !== null` ✅
- `PredictionCoaching` receives `eventType: string` ✅
