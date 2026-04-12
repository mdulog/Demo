# Claude Chatbot Feature — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a floating Claude-powered chat widget to the Pacevite React app that answers analytical fitness questions using live DB queries and internet scraping as tools.

**Architecture:** A JWT-protected `POST /api/chat/message` SSE endpoint runs a streaming agentic loop via `Anthropic.SDK`. The backend is stateless — the frontend sends full conversation history per request. Claude uses four tools (two DB, two web scrape) dispatched by `ChatToolExecutor`. A floating React widget (bottom-right) consumes the SSE stream token-by-token.

**Tech Stack:** `Anthropic.SDK` (tghamm, .NET), `HtmlAgilityPack` (.NET), `Microsoft.EntityFrameworkCore.InMemory` (tests), `react-markdown` (npm), `fetch` ReadableStream (SSE in browser), TUnit (tests), Mediator v3 stream queries.

---

## File Map

### Backend — New Files
| File | Responsibility |
|---|---|
| `src/Pacevite.Api/Infrastructure/Chat/AnthropicOptions.cs` | Typed config for Anthropic API key, model, max tokens |
| `src/Pacevite.Api/Infrastructure/Chat/SseEvent.cs` | Sealed record: `Type`, `Data`; static factories `Delta`, `ToolStart`, `ToolEnd`, `Done`, `Error` |
| `src/Pacevite.Api/Infrastructure/Chat/ConversationMessage.cs` | Record: `Role` (user/assistant), `Content` |
| `src/Pacevite.Api/Infrastructure/Chat/IChatToolHandler.cs` | `ValueTask<string> ExecuteAsync(JsonNode input, string userId, CancellationToken ct)` |
| `src/Pacevite.Api/Infrastructure/Chat/IChatToolExecutor.cs` | `ValueTask<string> ExecuteAsync(string toolName, JsonNode input, string userId, CancellationToken ct)` |
| `src/Pacevite.Api/Infrastructure/Chat/ChatToolExecutor.cs` | Dispatches tool calls by name to registered `IChatToolHandler` implementations |
| `src/Pacevite.Api/Infrastructure/Chat/Tools/GetEventsToolHandler.cs` | Queries `AppDbContext` for the user's events; scoped to `userId` |
| `src/Pacevite.Api/Infrastructure/Chat/Tools/GetPersonalBestsToolHandler.cs` | Queries `AppDbContext` for PBs; scoped to `userId` |
| `src/Pacevite.Api/Infrastructure/Chat/Tools/ScrapeRaceResultsToolHandler.cs` | `HttpClient` + `HtmlAgilityPack`; allowlisted domains |
| `src/Pacevite.Api/Infrastructure/Chat/Tools/FetchTrainingTipsToolHandler.cs` | `HttpClient` + `HtmlAgilityPack`; allowlisted domains |
| `src/Pacevite.Api/Features/Chat/SendMessageQuery.cs` | `IStreamQuery<SseEvent>`: `UserId`, `Message`, `History` |
| `src/Pacevite.Api/Features/Chat/SendMessageHandler.cs` | `IStreamQueryHandler<SendMessageQuery, SseEvent>`: agentic loop |
| `src/Pacevite.Api/Features/Chat/ChatEndpoints.cs` | Maps `POST /chat/message` → SSE stream; validates input; extracts `userId` from JWT |

### Backend — Modified Files
| File | Change |
|---|---|
| `src/Pacevite.Api/appsettings.json` | Add `"Anthropic"` config section |
| `src/Pacevite.Api/Pacevite.Api.csproj` | Add `Anthropic.SDK` and `HtmlAgilityPack` NuGet references |
| `src/Pacevite.Api/Program.cs` | Register `AnthropicClient`, `ChatToolExecutor`, tool handlers, map `/api/chat` |
| `.env.example` | Add `ANTHROPIC_API_KEY=` |

### Test Files — New
| File | What it tests |
|---|---|
| `tests/Pacevite.Api.Tests/Unit/Chat/ChatToolExecutorTests.cs` | Correct handler dispatched; unknown tool returns structured error |
| `tests/Pacevite.Api.Tests/Unit/Chat/GetEventsToolHandlerTests.cs` | DB query scoped to `userId`; optional filters respected |
| `tests/Pacevite.Api.Tests/Unit/Chat/GetPersonalBestsToolHandlerTests.cs` | Returns fastest per event type; scoped to `userId` |
| `tests/Pacevite.Api.Tests/Unit/Chat/ScrapeRaceResultsToolHandlerTests.cs` | Mocked `HttpClient`; HTML parsing; domain allowlist enforced |
| `tests/Pacevite.Api.Tests/Integration/ChatEndpointsTests.cs` | 401 without JWT; SSE emits `delta` and `done` with fake Anthropic service |

### Test Modified
| File | Change |
|---|---|
| `tests/Pacevite.Api.Tests/Pacevite.Api.Tests.csproj` | Add `Microsoft.EntityFrameworkCore.InMemory` |

### Frontend — New Files
| File | Responsibility |
|---|---|
| `src/Pacevite.Web/src/lib/chatApi.ts` | `POST /api/chat/message` → `ReadableStream` via `fetch`; reads JWT from `tokenStore` |
| `src/Pacevite.Web/src/hooks/useChatStream.ts` | Owns `messages[]`, `streamingText`, `toolStatus`, `isLoading`; drives SSE |
| `src/Pacevite.Web/src/components/chat/ChatMessage.tsx` | Single message bubble; markdown rendered via `react-markdown` |
| `src/Pacevite.Web/src/components/chat/ChatToolStatus.tsx` | Animated indicator between `tool_start` and `tool_end` |
| `src/Pacevite.Web/src/components/chat/ChatPanel.tsx` | Conversation list, streaming message, tool status, text input |
| `src/Pacevite.Web/src/components/chat/ChatWidget.tsx` | Floating button; toggles `ChatPanel`; only renders when authenticated |

### Frontend — Modified
| File | Change |
|---|---|
| `src/Pacevite.Web/src/App.tsx` | Mount `<ChatWidget />` inside `<AuthProvider>` after `<RouterProvider>` |
| `src/Pacevite.Web/package.json` | Add `react-markdown` |

---

## Task 1: NuGet packages, config model, appsettings

**Files:**
- Modify: `src/Pacevite.Api/Pacevite.Api.csproj`
- Create: `src/Pacevite.Api/Infrastructure/Chat/AnthropicOptions.cs`
- Modify: `src/Pacevite.Api/appsettings.json`
- Modify: `.env.example`

- [ ] **Step 1: Add NuGet packages**

```bash
cd src/Pacevite.Api
dotnet add package Anthropic.SDK
dotnet add package HtmlAgilityPack
```

Expected: packages appear in `Pacevite.Api.csproj` under `<ItemGroup>`.

- [ ] **Step 2: Create `AnthropicOptions.cs`**

```csharp
namespace Pacevite.Api.Infrastructure.Chat;

public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public required string ApiKey { get; init; }
    public required string Model { get; init; }
    public required int MaxTokens { get; init; }
}
```

- [ ] **Step 3: Add Anthropic section to `appsettings.json`**

```json
{
  "Logging": { ... },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=pacevite;Username=${DB_USER};Password=${DB_PASSWORD}"
  },
  "Jwt": {
    "Secret": "${JWT_SECRET}",
    "Issuer": "${JWT_ISSUER}",
    "Audience": "${JWT_AUDIENCE}"
  },
  "Anthropic": {
    "ApiKey": "${ANTHROPIC_API_KEY}",
    "Model": "claude-sonnet-4-6",
    "MaxTokens": 1024
  }
}
```

- [ ] **Step 4: Add `ANTHROPIC_API_KEY` to `.env.example`**

Open `.env.example` and add:
```
ANTHROPIC_API_KEY=your-anthropic-api-key-here
```

- [ ] **Step 5: Build to verify**

```bash
cd src/Pacevite.Api && dotnet build
```

Expected: `Build succeeded` with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Pacevite.Api/Pacevite.Api.csproj src/Pacevite.Api/Infrastructure/Chat/AnthropicOptions.cs src/Pacevite.Api/appsettings.json .env.example
git commit -m "feat(chat): add Anthropic.SDK + HtmlAgilityPack packages and config model"
```

---

## Task 2: SSE types and conversation model

**Files:**
- Create: `src/Pacevite.Api/Infrastructure/Chat/SseEvent.cs`
- Create: `src/Pacevite.Api/Infrastructure/Chat/ConversationMessage.cs`

- [ ] **Step 1: Create `SseEvent.cs`**

```csharp
using System.Text.Json;

namespace Pacevite.Api.Infrastructure.Chat;

public sealed record SseEvent(string Type, string Data)
{
    public static SseEvent Delta(string text) =>
        new("delta", JsonSerializer.Serialize(new { text }));

    public static SseEvent ToolStart(string tool, string label) =>
        new("tool_start", JsonSerializer.Serialize(new { tool, label }));

    public static SseEvent ToolEnd() =>
        new("tool_end", "{}");

    public static SseEvent Done() =>
        new("done", "{}");

    public static SseEvent Error(string message) =>
        new("error", JsonSerializer.Serialize(new { message }));
}
```

- [ ] **Step 2: Create `ConversationMessage.cs`**

```csharp
namespace Pacevite.Api.Infrastructure.Chat;

public sealed record ConversationMessage(string Role, string Content);
```

- [ ] **Step 3: Build to verify**

```bash
cd src/Pacevite.Api && dotnet build
```

Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add src/Pacevite.Api/Infrastructure/Chat/SseEvent.cs src/Pacevite.Api/Infrastructure/Chat/ConversationMessage.cs
git commit -m "feat(chat): add SseEvent and ConversationMessage types"
```

---

## Task 3: Tool handler interface and ChatToolExecutor

**Files:**
- Create: `src/Pacevite.Api/Infrastructure/Chat/IChatToolHandler.cs`
- Create: `src/Pacevite.Api/Infrastructure/Chat/IChatToolExecutor.cs`
- Create: `src/Pacevite.Api/Infrastructure/Chat/ChatToolExecutor.cs`
- Create: `tests/Pacevite.Api.Tests/Unit/Chat/ChatToolExecutorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Pacevite.Api.Tests/Unit/Chat/ChatToolExecutorTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Pacevite.Api.Infrastructure.Chat;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Chat;

[Category("Unit")]
public sealed class ChatToolExecutorTests
{
    private sealed class StubHandler : IChatToolHandler
    {
        public string CalledWith { get; private set; } = string.Empty;

        public ValueTask<string> ExecuteAsync(JsonNode input, string userId, CancellationToken ct)
        {
            CalledWith = input?.ToString() ?? string.Empty;
            return ValueTask.FromResult("stub-result");
        }
    }

    [Test]
    public async Task ExecuteAsync_KnownTool_DispatchesToCorrectHandler()
    {
        var stub = new StubHandler();
        var executor = new ChatToolExecutor(new Dictionary<string, IChatToolHandler>
        {
            ["get_events"] = stub
        });

        var result = await executor.ExecuteAsync("get_events", JsonNode.Parse("{}")!, "user-42", CancellationToken.None);

        await Assert.That(result).IsEqualTo("stub-result");
    }

    [Test]
    public async Task ExecuteAsync_UnknownTool_ReturnsErrorMessage()
    {
        var executor = new ChatToolExecutor(new Dictionary<string, IChatToolHandler>());

        var result = await executor.ExecuteAsync("nonexistent_tool", JsonNode.Parse("{}")!, "user-42", CancellationToken.None);

        await Assert.That(result).Contains("Unknown tool");
    }

    [Test]
    public async Task ExecuteAsync_KnownTool_PassesInputAndUserId()
    {
        var stub = new StubHandler();
        var executor = new ChatToolExecutor(new Dictionary<string, IChatToolHandler>
        {
            ["get_events"] = stub
        });
        var input = JsonNode.Parse("""{"event_type":"Marathon"}""")!;

        await executor.ExecuteAsync("get_events", input, "user-99", CancellationToken.None);

        await Assert.That(stub.CalledWith).Contains("Marathon");
    }
}
```

- [ ] **Step 2: Run to confirm they fail**

```bash
cd tests/Pacevite.Api.Tests && dotnet test --filter "Category=Unit&FullyQualifiedName~ChatToolExecutorTests"
```

Expected: compile error — `IChatToolHandler`, `IChatToolExecutor`, `ChatToolExecutor` not found.

- [ ] **Step 3: Create `IChatToolHandler.cs`**

```csharp
using System.Text.Json.Nodes;

namespace Pacevite.Api.Infrastructure.Chat;

public interface IChatToolHandler
{
    ValueTask<string> ExecuteAsync(JsonNode input, string userId, CancellationToken ct);
}
```

- [ ] **Step 4: Create `IChatToolExecutor.cs`**

```csharp
using System.Text.Json.Nodes;

namespace Pacevite.Api.Infrastructure.Chat;

public interface IChatToolExecutor
{
    ValueTask<string> ExecuteAsync(string toolName, JsonNode input, string userId, CancellationToken ct);
}
```

- [ ] **Step 5: Create `ChatToolExecutor.cs`**

```csharp
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Pacevite.Api.Infrastructure.Chat;

public sealed class ChatToolExecutor(
    IReadOnlyDictionary<string, IChatToolHandler> handlers,
    ILogger<ChatToolExecutor> logger) : IChatToolExecutor
{
    // Constructor for DI — receives all handlers keyed by tool name
    public ChatToolExecutor(IReadOnlyDictionary<string, IChatToolHandler> handlers)
        : this(handlers, Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatToolExecutor>.Instance) { }

    public async ValueTask<string> ExecuteAsync(
        string toolName, JsonNode input, string userId, CancellationToken ct)
    {
        if (!handlers.TryGetValue(toolName, out var handler))
        {
            logger.LogWarning("Unknown tool requested: {ToolName}", toolName);
            return $"Unknown tool: {toolName}. Available tools: {string.Join(", ", handlers.Keys)}";
        }

        try
        {
            return await handler.ExecuteAsync(input, userId, ct);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Tool handler {ToolName} threw for user {UserId}", toolName, userId);
            return $"Tool {toolName} failed: {ex.Message}";
        }
    }
}
```

- [ ] **Step 6: Run tests to confirm they pass**

```bash
cd tests/Pacevite.Api.Tests && dotnet test --filter "Category=Unit&FullyQualifiedName~ChatToolExecutorTests"
```

Expected: 3 tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/Pacevite.Api/Infrastructure/Chat/IChatToolHandler.cs \
        src/Pacevite.Api/Infrastructure/Chat/IChatToolExecutor.cs \
        src/Pacevite.Api/Infrastructure/Chat/ChatToolExecutor.cs \
        tests/Pacevite.Api.Tests/Unit/Chat/ChatToolExecutorTests.cs
git commit -m "feat(chat): add IChatToolHandler, IChatToolExecutor, ChatToolExecutor with tests"
```

---

## Task 4: GetEventsToolHandler

**Files:**
- Create: `src/Pacevite.Api/Infrastructure/Chat/Tools/GetEventsToolHandler.cs`
- Create: `tests/Pacevite.Api.Tests/Unit/Chat/GetEventsToolHandlerTests.cs`
- Modify: `tests/Pacevite.Api.Tests/Pacevite.Api.Tests.csproj`

- [ ] **Step 1: Add EF Core in-memory package to test project**

```bash
cd tests/Pacevite.Api.Tests
dotnet add package Microsoft.EntityFrameworkCore.InMemory
```

- [ ] **Step 2: Write the failing tests**

Create `tests/Pacevite.Api.Tests/Unit/Chat/GetEventsToolHandlerTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Chat.Tools;
using Pacevite.Api.Infrastructure.Persistence;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Chat;

[Category("Unit")]
public sealed class GetEventsToolHandlerTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, new UserManager<IdentityUser>(
            new UserStore<IdentityUser>(new AppDbContext(options, null!)), null, null, null, null, null, null, null, null));
    }

    private static AppDbContext BuildDb(out AppDbContext db)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        db = new AppDbContext(options);
        return db;
    }

    [Test]
    public async Task ExecuteAsync_ReturnsOnlyEventsForUserId()
    {
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        db.Events.AddRange(
            new Event { UserId = "user-1", EventType = EventType.Marathon, EventName = "Berlin", EventDate = new DateOnly(2024, 9, 29), Completion = CompletionStatus.Finished, ElapsedSecs = 14400 },
            new Event { UserId = "user-2", EventType = EventType.Marathon, EventName = "London", EventDate = new DateOnly(2024, 4, 21), Completion = CompletionStatus.Finished, ElapsedSecs = 13200 }
        );
        await db.SaveChangesAsync();

        var handler = new GetEventsToolHandler(db);
        var result = await handler.ExecuteAsync(JsonNode.Parse("{}")!, "user-1", CancellationToken.None);

        await Assert.That(result).Contains("Berlin");
        await Assert.That(result).DoesNotContain("London");
    }

    [Test]
    public async Task ExecuteAsync_WithEventTypeFilter_ReturnsFilteredEvents()
    {
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        db.Events.AddRange(
            new Event { UserId = "user-1", EventType = EventType.Marathon, EventName = "Berlin", EventDate = new DateOnly(2024, 9, 29), Completion = CompletionStatus.Finished, ElapsedSecs = 14400 },
            new Event { UserId = "user-1", EventType = EventType.Hyrox, EventName = "HYROX Berlin", EventDate = new DateOnly(2024, 11, 10), Completion = CompletionStatus.Finished, ElapsedSecs = 5400 }
        );
        await db.SaveChangesAsync();

        var handler = new GetEventsToolHandler(db);
        var result = await handler.ExecuteAsync(
            JsonNode.Parse("""{"event_type":"Marathon"}""")!, "user-1", CancellationToken.None);

        await Assert.That(result).Contains("Berlin Marathon");
        await Assert.That(result).DoesNotContain("HYROX");
    }

    [Test]
    public async Task ExecuteAsync_NoEvents_ReturnsNoEventsMessage()
    {
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var handler = new GetEventsToolHandler(db);
        var result = await handler.ExecuteAsync(JsonNode.Parse("{}")!, "user-with-no-events", CancellationToken.None);

        await Assert.That(result).Contains("No events found");
    }
}
```

- [ ] **Step 3: Run to confirm they fail**

```bash
cd tests/Pacevite.Api.Tests && dotnet test --filter "Category=Unit&FullyQualifiedName~GetEventsToolHandlerTests"
```

Expected: compile error — `GetEventsToolHandler` not found.

- [ ] **Step 4: Check `AppDbContext` constructor signature**

Read `src/Pacevite.Api/Infrastructure/Persistence/AppDbContext.cs` to confirm constructor parameters before writing the handler.

- [ ] **Step 5: Create `GetEventsToolHandler.cs`**

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Infrastructure.Chat.Tools;

public sealed class GetEventsToolHandler(AppDbContext db) : IChatToolHandler
{
    public async ValueTask<string> ExecuteAsync(JsonNode input, string userId, CancellationToken ct)
    {
        var eventTypeStr = input["event_type"]?.GetValue<string>();
        var fromStr = input["from"]?.GetValue<string>();
        var toStr = input["to"]?.GetValue<string>();

        var query = db.Events.Where(e => e.UserId == userId);

        if (!string.IsNullOrEmpty(eventTypeStr) &&
            Enum.TryParse<EventType>(eventTypeStr, ignoreCase: true, out var eventType))
        {
            query = query.Where(e => e.EventType == eventType);
        }

        if (DateOnly.TryParse(fromStr, out var from))
            query = query.Where(e => e.EventDate >= from);

        if (DateOnly.TryParse(toStr, out var to))
            query = query.Where(e => e.EventDate <= to);

        var events = await query
            .OrderByDescending(e => e.EventDate)
            .Select(e => new
            {
                e.Id,
                EventType = e.EventType.ToString(),
                e.EventName,
                EventDate = e.EventDate.ToString("yyyy-MM-dd"),
                Completion = e.Completion.ToString(),
                e.ElapsedSecs,
                e.OverallRank,
                e.AgeGroupRank,
                e.FieldSize,
            })
            .ToListAsync(ct);

        if (events.Count == 0)
            return "No events found for the given filters.";

        return JsonSerializer.Serialize(events);
    }
}
```

- [ ] **Step 6: Fix test constructor call**

The `AppDbContext` constructor in tests must match what's in the persistence layer. If `AppDbContext` requires only `DbContextOptions<AppDbContext>`, update the tests:

```csharp
// In each test method, replace the db creation with:
using var db = new AppDbContext(
    new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
```

Read `src/Pacevite.Api/Infrastructure/Persistence/AppDbContext.cs` and adjust constructor calls in the test to match before running.

- [ ] **Step 7: Run tests to confirm they pass**

```bash
cd tests/Pacevite.Api.Tests && dotnet test --filter "Category=Unit&FullyQualifiedName~GetEventsToolHandlerTests"
```

Expected: 3 tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/Pacevite.Api/Infrastructure/Chat/Tools/GetEventsToolHandler.cs \
        tests/Pacevite.Api.Tests/Unit/Chat/GetEventsToolHandlerTests.cs \
        tests/Pacevite.Api.Tests/Pacevite.Api.Tests.csproj
git commit -m "feat(chat): add GetEventsToolHandler with unit tests"
```

---

## Task 5: GetPersonalBestsToolHandler

**Files:**
- Create: `src/Pacevite.Api/Infrastructure/Chat/Tools/GetPersonalBestsToolHandler.cs`
- Create: `tests/Pacevite.Api.Tests/Unit/Chat/GetPersonalBestsToolHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Chat.Tools;
using Pacevite.Api.Infrastructure.Persistence;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Chat;

[Category("Unit")]
public sealed class GetPersonalBestsToolHandlerTests
{
    [Test]
    public async Task ExecuteAsync_ReturnsOnlyFinishedEvents()
    {
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        db.Events.AddRange(
            new Event { UserId = "user-1", EventType = EventType.Marathon, EventName = "Berlin", EventDate = new DateOnly(2024, 9, 29), Completion = CompletionStatus.Finished, ElapsedSecs = 14400 },
            new Event { UserId = "user-1", EventType = EventType.Marathon, EventName = "DNF Race", EventDate = new DateOnly(2024, 4, 1), Completion = CompletionStatus.Dnf, ElapsedSecs = 99999 }
        );
        await db.SaveChangesAsync();

        var handler = new GetPersonalBestsToolHandler(db);
        var result = await handler.ExecuteAsync(JsonNode.Parse("{}")!, "user-1", CancellationToken.None);

        await Assert.That(result).Contains("Berlin");
        await Assert.That(result).DoesNotContain("DNF Race");
    }

    [Test]
    public async Task ExecuteAsync_ReturnsOnlyFastestPerEventType()
    {
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        db.Events.AddRange(
            new Event { UserId = "user-1", EventType = EventType.Marathon, EventName = "Berlin Fast", EventDate = new DateOnly(2024, 9, 29), Completion = CompletionStatus.Finished, ElapsedSecs = 12000 },
            new Event { UserId = "user-1", EventType = EventType.Marathon, EventName = "London Slow", EventDate = new DateOnly(2024, 4, 21), Completion = CompletionStatus.Finished, ElapsedSecs = 15000 }
        );
        await db.SaveChangesAsync();

        var handler = new GetPersonalBestsToolHandler(db);
        var result = await handler.ExecuteAsync(JsonNode.Parse("{}")!, "user-1", CancellationToken.None);

        await Assert.That(result).Contains("Berlin Fast");
        await Assert.That(result).DoesNotContain("London Slow");
    }

    [Test]
    public async Task ExecuteAsync_ReturnsOnlyEventsForUserId()
    {
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        db.Events.Add(new Event
        {
            UserId = "user-other",
            EventType = EventType.Hyrox,
            EventName = "HYROX Other User",
            EventDate = new DateOnly(2024, 6, 1),
            Completion = CompletionStatus.Finished,
            ElapsedSecs = 4800
        });
        await db.SaveChangesAsync();

        var handler = new GetPersonalBestsToolHandler(db);
        var result = await handler.ExecuteAsync(JsonNode.Parse("{}")!, "user-1", CancellationToken.None);

        await Assert.That(result).DoesNotContain("HYROX Other User");
    }
}
```

- [ ] **Step 2: Run to confirm they fail**

```bash
cd tests/Pacevite.Api.Tests && dotnet test --filter "Category=Unit&FullyQualifiedName~GetPersonalBestsToolHandlerTests"
```

Expected: compile error.

- [ ] **Step 3: Create `GetPersonalBestsToolHandler.cs`**

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Infrastructure.Chat.Tools;

public sealed class GetPersonalBestsToolHandler(AppDbContext db) : IChatToolHandler
{
    public async ValueTask<string> ExecuteAsync(JsonNode input, string userId, CancellationToken ct)
    {
        var personalBests = await db.Events
            .Where(e => e.UserId == userId && e.Completion == CompletionStatus.Finished)
            .GroupBy(e => e.EventType)
            .Select(g => g.OrderBy(e => e.ElapsedSecs).First())
            .Select(e => new
            {
                EventType = e.EventType.ToString(),
                e.EventName,
                EventDate = e.EventDate.ToString("yyyy-MM-dd"),
                e.ElapsedSecs,
            })
            .ToListAsync(ct);

        if (personalBests.Count == 0)
            return "No personal bests found. The user has no finished events.";

        return JsonSerializer.Serialize(personalBests);
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
cd tests/Pacevite.Api.Tests && dotnet test --filter "Category=Unit&FullyQualifiedName~GetPersonalBestsToolHandlerTests"
```

Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Api/Infrastructure/Chat/Tools/GetPersonalBestsToolHandler.cs \
        tests/Pacevite.Api.Tests/Unit/Chat/GetPersonalBestsToolHandlerTests.cs
git commit -m "feat(chat): add GetPersonalBestsToolHandler with unit tests"
```

---

## Task 6: ScrapeRaceResultsToolHandler

**Files:**
- Create: `src/Pacevite.Api/Infrastructure/Chat/Tools/ScrapeRaceResultsToolHandler.cs`
- Create: `tests/Pacevite.Api.Tests/Unit/Chat/ScrapeRaceResultsToolHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Text.Json.Nodes;
using Pacevite.Api.Infrastructure.Chat.Tools;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Chat;

[Category("Unit")]
public sealed class ScrapeRaceResultsToolHandlerTests
{
    private static HttpClient BuildClient(string html, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHttpMessageHandler(html, status);
        return new HttpClient(handler) { BaseAddress = new Uri("https://www.worldathletics.org") };
    }

    private sealed class FakeHttpMessageHandler(string content, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(content)
            });
    }

    [Test]
    public async Task ExecuteAsync_ParsesHtmlAndReturnsText()
    {
        const string html = "<html><body><p>John Doe finished the 2024 London Marathon in 2:32:45.</p></body></html>";
        var handler = new ScrapeRaceResultsToolHandler(BuildClient(html));

        var result = await handler.ExecuteAsync(
            JsonNode.Parse("""{"race_name":"London Marathon","year":2024}""")!,
            "user-1",
            CancellationToken.None);

        await Assert.That(result).Contains("John Doe");
        await Assert.That(result).Contains("2:32:45");
    }

    [Test]
    public async Task ExecuteAsync_HttpFailure_ReturnsNoResultsMessage()
    {
        var handler = new ScrapeRaceResultsToolHandler(BuildClient(string.Empty, HttpStatusCode.NotFound));

        var result = await handler.ExecuteAsync(
            JsonNode.Parse("""{"race_name":"Unknown Race"}""")!,
            "user-1",
            CancellationToken.None);

        await Assert.That(result).Contains("No results found");
    }
}
```

- [ ] **Step 2: Run to confirm they fail**

```bash
cd tests/Pacevite.Api.Tests && dotnet test --filter "Category=Unit&FullyQualifiedName~ScrapeRaceResultsToolHandlerTests"
```

Expected: compile error.

- [ ] **Step 3: Create `ScrapeRaceResultsToolHandler.cs`**

```csharp
using System.Text.Json.Nodes;
using System.Web;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace Pacevite.Api.Infrastructure.Chat.Tools;

public sealed class ScrapeRaceResultsToolHandler(
    HttpClient httpClient,
    ILogger<ScrapeRaceResultsToolHandler> logger) : IChatToolHandler
{
    // Allowed domains (SSRF protection — A10)
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "www.worldathletics.org",
        "results.sporthive.com",
        "www.hyrox.com",
        "www.parkrun.org.uk",
        "www.spartanrace.com",
    };

    public ScrapeRaceResultsToolHandler(HttpClient httpClient)
        : this(httpClient, Microsoft.Extensions.Logging.Abstractions.NullLogger<ScrapeRaceResultsToolHandler>.Instance) { }

    public async ValueTask<string> ExecuteAsync(JsonNode input, string userId, CancellationToken ct)
    {
        var raceName = input["race_name"]?.GetValue<string>() ?? string.Empty;
        var year = input["year"]?.GetValue<int?>();

        var query = year.HasValue ? $"{raceName} {year} results" : $"{raceName} results";
        var encodedQuery = HttpUtility.UrlEncode(query);

        // Search worldathletics.org as primary source
        var url = $"https://www.worldathletics.org/search?query={encodedQuery}";

        return await ScrapeAsync(url, ct);
    }

    private async ValueTask<string> ScrapeAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !AllowedHosts.Contains(uri.Host))
        {
            return "No results found: domain not permitted.";
        }

        try
        {
            var html = await httpClient.GetStringAsync(url, ct);

            if (string.IsNullOrWhiteSpace(html))
                return "No results found.";

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Remove scripts and styles
            var nodesToRemove = doc.DocumentNode
                .SelectNodes("//script|//style|//nav|//footer|//header")
                ?? Enumerable.Empty<HtmlNode>();
            foreach (var node in nodesToRemove.ToList())
                node.Remove();

            var text = doc.DocumentNode.InnerText;
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

            return text.Length > 3000
                ? text[..3000] + "… (truncated)"
                : text;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Scrape failed for URL {Url}", url);
            return "No results found.";
        }
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
cd tests/Pacevite.Api.Tests && dotnet test --filter "Category=Unit&FullyQualifiedName~ScrapeRaceResultsToolHandlerTests"
```

Expected: 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Api/Infrastructure/Chat/Tools/ScrapeRaceResultsToolHandler.cs \
        tests/Pacevite.Api.Tests/Unit/Chat/ScrapeRaceResultsToolHandlerTests.cs
git commit -m "feat(chat): add ScrapeRaceResultsToolHandler with unit tests"
```

---

## Task 7: FetchTrainingTipsToolHandler

**Files:**
- Create: `src/Pacevite.Api/Infrastructure/Chat/Tools/FetchTrainingTipsToolHandler.cs`
- Create: `tests/Pacevite.Api.Tests/Unit/Chat/FetchTrainingTipsToolHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Text.Json.Nodes;
using Pacevite.Api.Infrastructure.Chat.Tools;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Chat;

[Category("Unit")]
public sealed class FetchTrainingTipsToolHandlerTests
{
    private sealed class FakeHttpMessageHandler(string content, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(content)
            });
    }

    [Test]
    public async Task ExecuteAsync_ParsesArticleContent()
    {
        const string html = "<html><body><article><p>To improve your Hyrox time, focus on sled pushes and burpee broad jumps.</p></article></body></html>";
        var httpClient = new HttpClient(new FakeHttpMessageHandler(html, HttpStatusCode.OK))
        {
            BaseAddress = new Uri("https://www.runnersworld.com")
        };

        var handler = new FetchTrainingTipsToolHandler(httpClient);
        var result = await handler.ExecuteAsync(
            JsonNode.Parse("""{"query":"how to improve Hyrox time"}""")!,
            "user-1",
            CancellationToken.None);

        await Assert.That(result).Contains("Hyrox");
    }

    [Test]
    public async Task ExecuteAsync_HttpFailure_ReturnsNoResultsMessage()
    {
        var httpClient = new HttpClient(new FakeHttpMessageHandler(string.Empty, HttpStatusCode.ServiceUnavailable));
        var handler = new FetchTrainingTipsToolHandler(httpClient);

        var result = await handler.ExecuteAsync(
            JsonNode.Parse("""{"query":"marathon training"}""")!,
            "user-1",
            CancellationToken.None);

        await Assert.That(result).Contains("No results found");
    }
}
```

- [ ] **Step 2: Run to confirm they fail**

```bash
cd tests/Pacevite.Api.Tests && dotnet test --filter "Category=Unit&FullyQualifiedName~FetchTrainingTipsToolHandlerTests"
```

Expected: compile error.

- [ ] **Step 3: Create `FetchTrainingTipsToolHandler.cs`**

```csharp
using System.Text.Json.Nodes;
using System.Web;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace Pacevite.Api.Infrastructure.Chat.Tools;

public sealed class FetchTrainingTipsToolHandler(
    HttpClient httpClient,
    ILogger<FetchTrainingTipsToolHandler> logger) : IChatToolHandler
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "www.runnersworld.com",
        "www.triathlete.com",
        "www.hyrox.com",
        "www.outsideonline.com",
        "www.verywellfit.com",
    };

    public FetchTrainingTipsToolHandler(HttpClient httpClient)
        : this(httpClient, Microsoft.Extensions.Logging.Abstractions.NullLogger<FetchTrainingTipsToolHandler>.Instance) { }

    public async ValueTask<string> ExecuteAsync(JsonNode input, string userId, CancellationToken ct)
    {
        var query = input["query"]?.GetValue<string>() ?? string.Empty;
        var encodedQuery = HttpUtility.UrlEncode(query);

        var url = $"https://www.runnersworld.com/search?q={encodedQuery}";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !AllowedHosts.Contains(uri.Host))
        {
            return "No results found: domain not permitted.";
        }

        try
        {
            var html = await httpClient.GetStringAsync(url, ct);

            if (string.IsNullOrWhiteSpace(html))
                return "No results found.";

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var nodesToRemove = doc.DocumentNode
                .SelectNodes("//script|//style|//nav|//footer|//header|//aside")
                ?? Enumerable.Empty<HtmlNode>();
            foreach (var node in nodesToRemove.ToList())
                node.Remove();

            var text = doc.DocumentNode.InnerText;
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

            return text.Length > 3000
                ? text[..3000] + "… (truncated)"
                : text;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Training tips scrape failed for query {Query}", query);
            return "No results found.";
        }
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
cd tests/Pacevite.Api.Tests && dotnet test --filter "Category=Unit&FullyQualifiedName~FetchTrainingTipsToolHandlerTests"
```

Expected: 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Api/Infrastructure/Chat/Tools/FetchTrainingTipsToolHandler.cs \
        tests/Pacevite.Api.Tests/Unit/Chat/FetchTrainingTipsToolHandlerTests.cs
git commit -m "feat(chat): add FetchTrainingTipsToolHandler with unit tests"
```

---

## Task 8: SendMessageQuery and SendMessageHandler (agentic loop)

**Files:**
- Create: `src/Pacevite.Api/Features/Chat/SendMessageQuery.cs`
- Create: `src/Pacevite.Api/Features/Chat/SendMessageHandler.cs`

> ⚠️ **Before writing this task:** Run `mcp__plugin_context7_context7__query-docs` against `/tghamm/anthropic.sdk` to verify: `MessageParameters.System` type, `AnthropicModels` constant for claude-sonnet-4-6, and exact streaming event property names (`Delta.Text`, `ToolCalls`, `StopReason`). The code below matches current SDK v3 but verify if unsure.

- [ ] **Step 1: Create `SendMessageQuery.cs`**

```csharp
using Mediator;
using Pacevite.Api.Infrastructure.Chat;

namespace Pacevite.Api.Features.Chat;

public sealed record SendMessageQuery(
    string UserId,
    string Message,
    IReadOnlyList<ConversationMessage> History) : IStreamQuery<SseEvent>;
```

- [ ] **Step 2: Create `SendMessageHandler.cs`**

```csharp
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using Mediator;
using Microsoft.Extensions.Options;
using Pacevite.Api.Infrastructure.Chat;

namespace Pacevite.Api.Features.Chat;

public sealed class SendMessageHandler(
    AnthropicClient anthropic,
    IChatToolExecutor toolExecutor,
    IOptions<AnthropicOptions> options) : IStreamQueryHandler<SendMessageQuery, SseEvent>
{
    private static readonly string SystemPrompt =
        "You are a fitness analytics assistant for Pacevite. " +
        "Help users understand their race performance, analyse trends, and compare against other athletes. " +
        "You have tools to query the user's race data and search the internet for race results and training tips. " +
        "Elapsed times are in seconds — always convert to h:mm:ss format in your responses. " +
        "Be encouraging, specific, and data-driven.";

    // Tool definitions sent to Claude on every request
    private static readonly List<Anthropic.SDK.Common.Tool> ToolDefinitions = BuildToolDefinitions();

    public async IAsyncEnumerable<SseEvent> Handle(
        SendMessageQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var messages = BuildMessageHistory(query);

        while (true)
        {
            var parameters = new MessageParameters
            {
                Messages = messages,
                Model = options.Value.Model,
                MaxTokens = options.Value.MaxTokens,
                Stream = true,
                Tools = ToolDefinitions,
                System = [new SystemMessage(SystemPrompt)],
            };

            // Stream this turn — yield text deltas live; collect full outputs for tool detection
            var outputs = new List<MessageResponse>();

            await foreach (var response in anthropic.Messages.StreamClaudeMessageAsync(parameters, ct))
            {
                if (response.Delta?.Text is { Length: > 0 } text)
                    yield return SseEvent.Delta(text);

                outputs.Add(response);
            }

            // Add assistant message to history (reconstructs full content including tool_use blocks)
            var assistantMessage = new Message(outputs);
            messages.Add(assistantMessage);

            // Extract tool use content blocks from the assembled assistant message
            var toolUses = (assistantMessage.Content as List<ContentBase> ?? [])
                .OfType<ToolUseContent>()
                .ToList();

            if (toolUses.Count == 0)
            {
                yield return SseEvent.Done();
                yield break;
            }

            // Execute each tool, stream status events, add results to history
            foreach (var toolUse in toolUses)
            {
                yield return SseEvent.ToolStart(toolUse.Name, GetToolLabel(toolUse.Name));

                var inputJson = JsonNode.Parse(JsonSerializer.Serialize(toolUse.Input))
                    ?? JsonNode.Parse("{}")!;
                var result = await toolExecutor.ExecuteAsync(toolUse.Name, inputJson, query.UserId, ct);

                yield return SseEvent.ToolEnd();

                messages.Add(new Message
                {
                    Role = RoleType.User,
                    Content = new List<ContentBase>
                    {
                        new ToolResultContent
                        {
                            ToolUseId = toolUse.Id,
                            Content = new List<ContentBase> { new TextContent { Text = result } },
                        }
                    }
                });
            }
            // Loop: Claude will now respond using the tool results
        }
    }

    private static List<Message> BuildMessageHistory(SendMessageQuery query)
    {
        var messages = query.History
            .Select(h => new Message(
                h.Role == "user" ? RoleType.User : RoleType.Assistant,
                h.Content))
            .ToList();

        messages.Add(new Message(RoleType.User, query.Message));
        return messages;
    }

    private static string GetToolLabel(string toolName) => toolName switch
    {
        "get_events"            => "Looking up your events…",
        "get_personal_bests"    => "Looking up your personal bests…",
        "scrape_race_results"   => "Searching race results…",
        "fetch_training_tips"   => "Fetching training tips…",
        _                       => "Running tool…",
    };

    private static List<Anthropic.SDK.Common.Tool> BuildToolDefinitions()
    {
        var jsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        return
        [
            BuildTool("get_events", "Retrieve the user's fitness events. Optionally filter by event_type (Marathon, Hyrox, Spartan, Generic), from (ISO date), or to (ISO date).",
                new InputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, Property>
                    {
                        ["event_type"] = new() { Type = "string", Description = "Event type: Marathon, Hyrox, Spartan, or Generic" },
                        ["from"]       = new() { Type = "string", Description = "Start date (YYYY-MM-DD)" },
                        ["to"]         = new() { Type = "string", Description = "End date (YYYY-MM-DD)" },
                    },
                }, jsonOptions),

            BuildTool("get_personal_bests", "Retrieve the user's personal best (fastest finished) time per event type.",
                new InputSchema { Type = "object", Properties = new Dictionary<string, Property>() },
                jsonOptions),

            BuildTool("scrape_race_results", "Search for published race results for a specific race.",
                new InputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, Property>
                    {
                        ["race_name"] = new() { Type = "string", Description = "Name of the race, e.g. 'London Marathon'" },
                        ["year"]      = new() { Type = "integer", Description = "Optional race year" },
                    },
                    Required = ["race_name"],
                }, jsonOptions),

            BuildTool("fetch_training_tips", "Search for training advice and tips relevant to a query.",
                new InputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, Property>
                    {
                        ["query"] = new() { Type = "string", Description = "Training question or topic, e.g. 'how to improve Hyrox sled push'" },
                    },
                    Required = ["query"],
                }, jsonOptions),
        ];
    }

    private static Anthropic.SDK.Common.Tool BuildTool(
        string name, string description, InputSchema schema, JsonSerializerOptions jsonOptions)
    {
        var json = JsonSerializer.Serialize(schema, jsonOptions);
        return new Anthropic.SDK.Common.Function(name, description, System.Text.Json.Nodes.JsonNode.Parse(json));
    }
}
```

- [ ] **Step 3: Build to verify**

```bash
cd src/Pacevite.Api && dotnet build
```

Expected: `Build succeeded`. If SDK type names differ (e.g. `ToolUseContent`, `TextContent`, `SystemMessage`), query Context7 `/tghamm/anthropic.sdk` with `"ToolUseContent TextContent SystemMessage constructor"` and adjust.

- [ ] **Step 4: Commit**

```bash
git add src/Pacevite.Api/Features/Chat/SendMessageQuery.cs \
        src/Pacevite.Api/Features/Chat/SendMessageHandler.cs
git commit -m "feat(chat): add SendMessageHandler streaming agentic loop"
```

---

## Task 9: ChatEndpoints

**Files:**
- Create: `src/Pacevite.Api/Features/Chat/ChatEndpoints.cs`
- Create: `src/Pacevite.Api/Contracts/Requests/SendMessageRequest.cs`

- [ ] **Step 1: Create `SendMessageRequest.cs`**

```csharp
using Pacevite.Api.Infrastructure.Chat;

namespace Pacevite.Api.Contracts.Requests;

public sealed record SendMessageRequest(
    string Message,
    IReadOnlyList<ConversationMessage> History);
```

- [ ] **Step 2: Create `ChatEndpoints.cs`**

```csharp
using System.Security.Claims;
using System.Text.Json;
using Mediator;
using Pacevite.Api.Contracts.Requests;
using Pacevite.Api.Features.Chat;
using Pacevite.Api.Infrastructure.Chat;

namespace Pacevite.Api.Features.Chat;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/message", StreamMessageAsync).WithName("ChatMessage");
        return app;
    }

    private static async Task StreamMessageAsync(
        SendMessageRequest request,
        ClaimsPrincipal user,
        IMediator mediator,
        HttpResponse response,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 2000)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "Message must be 1–2000 characters." }, ct);
            return;
        }

        if (request.History.Any(m => m.Role is not "user" and not "assistant"))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "History roles must be 'user' or 'assistant'." }, ct);
            return;
        }

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub")
            ?? throw new InvalidOperationException("User ID claim missing from token.");

        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        var query = new SendMessageQuery(userId, request.Message, request.History);

        try
        {
            await foreach (var sseEvent in mediator.CreateStream(query, ct))
            {
                await response.WriteAsync($"event: {sseEvent.Type}\ndata: {sseEvent.Data}\n\n", ct);
                await response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — no action needed
        }
        catch (Exception ex)
        {
            var errorPayload = JsonSerializer.Serialize(new { message = "An error occurred. Please try again." });
            await response.WriteAsync($"event: error\ndata: {errorPayload}\n\n", ct);
            await response.Body.FlushAsync(ct);
        }
    }
}
```

- [ ] **Step 3: Build**

```bash
cd src/Pacevite.Api && dotnet build
```

Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add src/Pacevite.Api/Features/Chat/ChatEndpoints.cs \
        src/Pacevite.Api/Contracts/Requests/SendMessageRequest.cs
git commit -m "feat(chat): add ChatEndpoints SSE streaming endpoint"
```

---

## Task 10: DI wiring in Program.cs

**Files:**
- Modify: `src/Pacevite.Api/Program.cs`

- [ ] **Step 1: Register all chat services**

In `Program.cs`, after the `// ── Services ──` block, add:

```csharp
// ── Anthropic ─────────────────────────────────────────────────────────────────
var anthropicApiKey = builder.Configuration["Anthropic:ApiKey"]
    ?? throw new InvalidOperationException("Anthropic:ApiKey is required.");

builder.Services.Configure<AnthropicOptions>(
    builder.Configuration.GetSection(AnthropicOptions.SectionName));

builder.Services.AddSingleton(new AnthropicClient(anthropicApiKey));

// ── Chat Tool Handlers ────────────────────────────────────────────────────────
builder.Services.AddHttpClient<ScrapeRaceResultsToolHandler>();
builder.Services.AddHttpClient<FetchTrainingTipsToolHandler>();
builder.Services.AddScoped<GetEventsToolHandler>();
builder.Services.AddScoped<GetPersonalBestsToolHandler>();

builder.Services.AddScoped<IChatToolExecutor>(sp =>
    new ChatToolExecutor(
        new Dictionary<string, IChatToolHandler>
        {
            ["get_events"]          = sp.GetRequiredService<GetEventsToolHandler>(),
            ["get_personal_bests"]  = sp.GetRequiredService<GetPersonalBestsToolHandler>(),
            ["scrape_race_results"] = sp.GetRequiredService<ScrapeRaceResultsToolHandler>(),
            ["fetch_training_tips"] = sp.GetRequiredService<FetchTrainingTipsToolHandler>(),
        },
        sp.GetRequiredService<ILogger<ChatToolExecutor>>()));
```

Add the required `using` statements at the top of `Program.cs`:

```csharp
using Pacevite.Api.Features.Chat;
using Pacevite.Api.Infrastructure.Chat;
using Pacevite.Api.Infrastructure.Chat.Tools;
using Anthropic.SDK;
```

- [ ] **Step 2: Map the chat endpoint group**

After `app.MapGroup("/api/events").RequireAuthorization().MapEventEndpoints();`, add:

```csharp
app.MapGroup("/api/chat").RequireAuthorization().MapChatEndpoints();
```

- [ ] **Step 3: Build and run**

```bash
cd src/Pacevite.Api && dotnet run
```

Expected: App starts without throwing. Navigate to `http://localhost:5xxx/scalar` — verify `/api/chat/message` appears in the API explorer.

- [ ] **Step 4: Commit**

```bash
git add src/Pacevite.Api/Program.cs
git commit -m "feat(chat): wire up chat DI and map /api/chat/message endpoint"
```

---

## Task 11: Integration tests for ChatEndpoints

**Files:**
- Create: `tests/Pacevite.Api.Tests/Integration/ChatEndpointsTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pacevite.Api.Contracts.Requests;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Infrastructure.Chat;
using Pacevite.Api.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using TUnit.Core;

namespace Pacevite.Api.Tests.Integration;

/// <summary>
/// Fake AnthropicClient stand-in — replaces the real client in DI so tests
/// never call the live Anthropic API. Emits one text delta then done.
/// </summary>
internal sealed class FakeAnthropicMessagesClient : IAnthropicMessageService
{
    // Implement whichever interface the handler depends on — if AnthropicClient
    // is injected directly (not via interface), replace it in DI via
    // services.AddSingleton(new AnthropicClient("fake-key")) with a subclass or
    // restructure SendMessageHandler to accept IAnthropicMessageService.
    //
    // See Task 8 note — if SendMessageHandler takes AnthropicClient directly,
    // add IAnthropicMessageService abstraction before running these tests.
}

[Category("Integration")]
public sealed class ChatEndpointsTests
{
    private PostgreSqlContainer _postgres = null!;
    private HttpClient _client = null!;
    private WebApplicationFactory<Program> _factory = null!;

    [Before(Test)]
    public async Task SetUpAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:17")
            .WithDatabase("pacevite_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.ConfigureServices(services =>
                {
                    // Replace real DB
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (descriptor is not null) services.Remove(descriptor);
                    services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(_postgres.GetConnectionString()));

                    using var scope = services.BuildServiceProvider().CreateScope();
                    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
                });

                host.UseSetting("Jwt:Secret", "super-secret-key-for-testing-only-32c");
                host.UseSetting("Jwt:Issuer", "pacevite-test");
                host.UseSetting("Jwt:Audience", "pacevite-test");
                host.UseSetting("Anthropic:ApiKey", "fake-key-for-tests");
                host.UseSetting("Anthropic:Model", "claude-sonnet-4-6");
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

    [Test]
    public async Task PostMessage_WithoutToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/chat/message",
            new SendMessageRequest("Am I getting faster?", []));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task PostMessage_WithEmptyMessage_Returns400()
    {
        // Register and log in to get a token
        await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("chat-empty@example.com", "P@ssword1!"));
        var loginRes = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("chat-empty@example.com", "P@ssword1!"));
        var auth = await loginRes.Content.ReadFromJsonAsync<AuthResponse>();

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth!.Token);

        var response = await _client.PostAsJsonAsync("/api/chat/message",
            new SendMessageRequest(string.Empty, []));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }
}
```

> **Note:** The integration test for the SSE stream itself requires `SendMessageHandler` to accept an `IAnthropicMessageService` abstraction so the real Anthropic SDK can be swapped out. If you injected `AnthropicClient` directly in Task 8, add an `IAnthropicMessageService` wrapper interface now:
>
> ```csharp
> // src/Pacevite.Api/Infrastructure/Chat/IAnthropicMessageService.cs
> public interface IAnthropicMessageService
> {
>     IAsyncEnumerable<MessageResponse> StreamAsync(MessageParameters parameters, CancellationToken ct);
> }
>
> // src/Pacevite.Api/Infrastructure/Chat/AnthropicMessageService.cs
> public sealed class AnthropicMessageService(AnthropicClient client) : IAnthropicMessageService
> {
>     public IAsyncEnumerable<MessageResponse> StreamAsync(MessageParameters parameters, CancellationToken ct)
>         => client.Messages.StreamClaudeMessageAsync(parameters, ct);
> }
> ```
>
> Update `SendMessageHandler` to accept `IAnthropicMessageService` instead of `AnthropicClient`. Register it in `Program.cs`. Then add a `FakeAnthropicMessageService` in the test that yields a `delta` event and a `done` event without calling the real API.

- [ ] **Step 2: Run to confirm 401 and 400 tests pass**

```bash
cd tests/Pacevite.Api.Tests && dotnet test --filter "Category=Integration&FullyQualifiedName~ChatEndpointsTests"
```

Expected: 2 tests pass.

- [ ] **Step 3: Commit**

```bash
git add tests/Pacevite.Api.Tests/Integration/ChatEndpointsTests.cs
git commit -m "test(chat): add ChatEndpoints integration tests for auth and validation"
```

---

## Task 12: Frontend — install react-markdown and chatApi.ts

**Files:**
- Modify: `src/Pacevite.Web/package.json` (via npm install)
- Create: `src/Pacevite.Web/src/lib/chatApi.ts`

- [ ] **Step 1: Install react-markdown**

```bash
cd src/Pacevite.Web && npm install react-markdown
```

Expected: `react-markdown` appears in `package.json` dependencies.

- [ ] **Step 2: Create `chatApi.ts`**

```typescript
import { tokenStore } from './api'

export interface ConversationMessage {
  role: 'user' | 'assistant'
  content: string
}

export interface SseCallbacks {
  onDelta: (text: string) => void
  onToolStart: (tool: string, label: string) => void
  onToolEnd: () => void
  onDone: () => void
  onError: (message: string) => void
}

export async function streamChatMessage(
  message: string,
  history: ConversationMessage[],
  callbacks: SseCallbacks,
  signal: AbortSignal,
): Promise<void> {
  const token = tokenStore.get()

  const response = await fetch('/api/chat/message', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify({ message, history }),
    signal,
  })

  if (!response.ok) {
    callbacks.onError(`Request failed: ${response.status}`)
    return
  }

  const reader = response.body!.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  while (true) {
    const { done, value } = await reader.read()
    if (done) break

    buffer += decoder.decode(value, { stream: true })
    const lines = buffer.split('\n')
    buffer = lines.pop() ?? ''

    let currentEvent = ''
    for (const line of lines) {
      if (line.startsWith('event: ')) {
        currentEvent = line.slice(7).trim()
      } else if (line.startsWith('data: ')) {
        const data = JSON.parse(line.slice(6).trim())
        switch (currentEvent) {
          case 'delta':
            callbacks.onDelta(data.text)
            break
          case 'tool_start':
            callbacks.onToolStart(data.tool, data.label)
            break
          case 'tool_end':
            callbacks.onToolEnd()
            break
          case 'done':
            callbacks.onDone()
            return
          case 'error':
            callbacks.onError(data.message)
            return
        }
        currentEvent = ''
      }
    }
  }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/Pacevite.Web/package.json src/Pacevite.Web/package-lock.json src/Pacevite.Web/src/lib/chatApi.ts
git commit -m "feat(chat): add react-markdown and chatApi SSE stream client"
```

---

## Task 13: useChatStream hook

**Files:**
- Create: `src/Pacevite.Web/src/hooks/useChatStream.ts`

- [ ] **Step 1: Create `useChatStream.ts`**

```typescript
import { useCallback, useRef, useState } from 'react'
import { streamChatMessage, type ConversationMessage } from '@/lib/chatApi'

export interface ChatMessage {
  id: string
  role: 'user' | 'assistant'
  content: string
}

export interface UseChatStreamResult {
  messages: ChatMessage[]
  streamingText: string
  toolStatus: string
  isLoading: boolean
  error: string | null
  sendMessage: (text: string) => Promise<void>
  clearError: () => void
}

export function useChatStream(): UseChatStreamResult {
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [streamingText, setStreamingText] = useState('')
  const [toolStatus, setToolStatus] = useState('')
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const abortRef = useRef<AbortController | null>(null)

  const sendMessage = useCallback(async (text: string) => {
    if (isLoading) return

    // Cancel any in-flight stream
    abortRef.current?.abort()
    abortRef.current = new AbortController()

    const userMessage: ChatMessage = { id: crypto.randomUUID(), role: 'user', content: text }
    setMessages(prev => [...prev, userMessage])
    setStreamingText('')
    setToolStatus('')
    setError(null)
    setIsLoading(true)

    // Build history from committed messages (not the one we just added — it's the current turn)
    const history: ConversationMessage[] = messages.map(m => ({
      role: m.role,
      content: m.content,
    }))

    let accumulated = ''

    try {
      await streamChatMessage(
        text,
        history,
        {
          onDelta: delta => {
            accumulated += delta
            setStreamingText(accumulated)
          },
          onToolStart: (_tool, label) => setToolStatus(label),
          onToolEnd: () => setToolStatus(''),
          onDone: () => {
            const assistantMessage: ChatMessage = {
              id: crypto.randomUUID(),
              role: 'assistant',
              content: accumulated,
            }
            setMessages(prev => [...prev, assistantMessage])
            setStreamingText('')
            setToolStatus('')
            setIsLoading(false)
          },
          onError: message => {
            setError(message)
            setStreamingText('')
            setToolStatus('')
            setIsLoading(false)
          },
        },
        abortRef.current.signal,
      )
    } catch (err) {
      if (err instanceof Error && err.name !== 'AbortError') {
        setError('Connection lost. Please try again.')
      }
      setStreamingText('')
      setToolStatus('')
      setIsLoading(false)
    }
  }, [isLoading, messages])

  return {
    messages,
    streamingText,
    toolStatus,
    isLoading,
    error,
    sendMessage,
    clearError: () => setError(null),
  }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/Pacevite.Web/src/hooks/useChatStream.ts
git commit -m "feat(chat): add useChatStream hook"
```

---

## Task 14: ChatMessage and ChatToolStatus components

**Files:**
- Create: `src/Pacevite.Web/src/components/chat/ChatMessage.tsx`
- Create: `src/Pacevite.Web/src/components/chat/ChatToolStatus.tsx`

- [ ] **Step 1: Create `ChatMessage.tsx`**

```tsx
import Markdown from 'react-markdown'
import type { ChatMessage as ChatMessageType } from '@/hooks/useChatStream'

interface Props {
  message: ChatMessageType
}

export function ChatMessage({ message }: Props) {
  const isUser = message.role === 'user'

  return (
    <div className={`flex ${isUser ? 'justify-end' : 'justify-start'} mb-3`}>
      {!isUser && (
        <div className="w-7 h-7 rounded-full bg-blue-600 flex items-center justify-center text-white text-xs font-bold mr-2 flex-shrink-0 mt-1">
          P
        </div>
      )}
      <div
        className={`max-w-[80%] rounded-2xl px-4 py-2 text-sm ${
          isUser
            ? 'bg-blue-600 text-white rounded-tr-sm'
            : 'bg-gray-100 text-gray-900 rounded-tl-sm'
        }`}
      >
        {isUser ? (
          <p>{message.content}</p>
        ) : (
          <Markdown className="prose prose-sm max-w-none">{message.content}</Markdown>
        )}
      </div>
    </div>
  )
}
```

- [ ] **Step 2: Create `ChatToolStatus.tsx`**

```tsx
interface Props {
  label: string
}

export function ChatToolStatus({ label }: Props) {
  return (
    <div className="flex justify-start mb-3">
      <div className="w-7 h-7 rounded-full bg-blue-600 flex items-center justify-center text-white text-xs font-bold mr-2 flex-shrink-0">
        P
      </div>
      <div className="bg-gray-100 rounded-2xl rounded-tl-sm px-4 py-2 text-sm text-gray-500 flex items-center gap-2">
        <span className="flex gap-1">
          <span className="w-1.5 h-1.5 bg-gray-400 rounded-full animate-bounce [animation-delay:0ms]" />
          <span className="w-1.5 h-1.5 bg-gray-400 rounded-full animate-bounce [animation-delay:150ms]" />
          <span className="w-1.5 h-1.5 bg-gray-400 rounded-full animate-bounce [animation-delay:300ms]" />
        </span>
        {label}
      </div>
    </div>
  )
}
```

- [ ] **Step 3: Commit**

```bash
git add src/Pacevite.Web/src/components/chat/ChatMessage.tsx \
        src/Pacevite.Web/src/components/chat/ChatToolStatus.tsx
git commit -m "feat(chat): add ChatMessage and ChatToolStatus components"
```

---

## Task 15: ChatPanel component

**Files:**
- Create: `src/Pacevite.Web/src/components/chat/ChatPanel.tsx`

- [ ] **Step 1: Create `ChatPanel.tsx`**

```tsx
import { useEffect, useRef, useState } from 'react'
import { ChatMessage } from './ChatMessage'
import { ChatToolStatus } from './ChatToolStatus'
import type { UseChatStreamResult } from '@/hooks/useChatStream'

interface Props {
  chat: UseChatStreamResult
}

export function ChatPanel({ chat }: Props) {
  const { messages, streamingText, toolStatus, isLoading, error, sendMessage, clearError } = chat
  const [input, setInput] = useState('')
  const bottomRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages, streamingText, toolStatus])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    const text = input.trim()
    if (!text || isLoading) return
    setInput('')
    await sendMessage(text)
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      handleSubmit(e as unknown as React.FormEvent)
    }
  }

  return (
    <div className="flex flex-col h-full">
      {/* Header */}
      <div className="px-4 py-3 border-b border-gray-200 bg-white rounded-t-2xl">
        <h2 className="text-sm font-semibold text-gray-900">Pacevite Assistant</h2>
        <p className="text-xs text-gray-500">Ask about your performance</p>
      </div>

      {/* Messages */}
      <div className="flex-1 overflow-y-auto px-4 py-3">
        {messages.length === 0 && (
          <p className="text-center text-xs text-gray-400 mt-8">
            Ask me about your race history, trends, or how to improve.
          </p>
        )}

        {messages.map(msg => (
          <ChatMessage key={msg.id} message={msg} />
        ))}

        {toolStatus && <ChatToolStatus label={toolStatus} />}

        {streamingText && !toolStatus && (
          <div className="flex justify-start mb-3">
            <div className="w-7 h-7 rounded-full bg-blue-600 flex items-center justify-center text-white text-xs font-bold mr-2 flex-shrink-0 mt-1">
              P
            </div>
            <div className="max-w-[80%] bg-gray-100 rounded-2xl rounded-tl-sm px-4 py-2 text-sm text-gray-900">
              {streamingText}
              <span className="inline-block w-0.5 h-4 bg-gray-600 animate-pulse ml-0.5 align-text-bottom" />
            </div>
          </div>
        )}

        {error && (
          <div className="bg-red-50 border border-red-200 rounded-lg px-3 py-2 text-xs text-red-700 flex justify-between items-center mb-3">
            {error}
            <button onClick={clearError} className="ml-2 text-red-500 hover:text-red-700">✕</button>
          </div>
        )}

        <div ref={bottomRef} />
      </div>

      {/* Input */}
      <form onSubmit={handleSubmit} className="px-4 py-3 border-t border-gray-200 bg-white rounded-b-2xl">
        <div className="flex gap-2 items-end">
          <textarea
            value={input}
            onChange={e => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Ask about your performance…"
            rows={1}
            disabled={isLoading}
            className="flex-1 resize-none rounded-xl border border-gray-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50"
          />
          <button
            type="submit"
            disabled={!input.trim() || isLoading}
            className="w-8 h-8 flex items-center justify-center rounded-full bg-blue-600 text-white disabled:opacity-40 hover:bg-blue-700 transition-colors flex-shrink-0"
          >
            ↑
          </button>
        </div>
      </form>
    </div>
  )
}
```

- [ ] **Step 2: Commit**

```bash
git add src/Pacevite.Web/src/components/chat/ChatPanel.tsx
git commit -m "feat(chat): add ChatPanel component"
```

---

## Task 16: ChatWidget (floating button)

**Files:**
- Create: `src/Pacevite.Web/src/components/chat/ChatWidget.tsx`

- [ ] **Step 1: Create `ChatWidget.tsx`**

```tsx
import { useState } from 'react'
import { ChatPanel } from './ChatPanel'
import { useChatStream } from '@/hooks/useChatStream'
import { useAuth } from '@/hooks/useAuth'

export function ChatWidget() {
  const { isAuthenticated } = useAuth()
  const [isOpen, setIsOpen] = useState(false)
  const chat = useChatStream()

  // Only render for authenticated users
  if (!isAuthenticated) return null

  return (
    <div className="fixed bottom-6 right-6 z-50 flex flex-col items-end gap-3">
      {isOpen && (
        <div className="w-80 h-[480px] bg-white rounded-2xl shadow-2xl border border-gray-200 flex flex-col overflow-hidden">
          <ChatPanel chat={chat} />
        </div>
      )}

      <button
        onClick={() => setIsOpen(prev => !prev)}
        aria-label={isOpen ? 'Close chat' : 'Open chat assistant'}
        className="w-14 h-14 rounded-full bg-blue-600 text-white shadow-lg hover:bg-blue-700 active:scale-95 transition-all flex items-center justify-center text-2xl"
      >
        {isOpen ? '✕' : '💬'}
      </button>
    </div>
  )
}
```

- [ ] **Step 2: Commit**

```bash
git add src/Pacevite.Web/src/components/chat/ChatWidget.tsx
git commit -m "feat(chat): add ChatWidget floating button component"
```

---

## Task 17: Mount ChatWidget in App.tsx

**Files:**
- Modify: `src/Pacevite.Web/src/App.tsx`

- [ ] **Step 1: Add `ChatWidget` to `App.tsx`**

```tsx
import { createBrowserRouter, RouterProvider, Navigate } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from '@/context/AuthContext'
import { AuthGuard } from '@/components/AuthGuard'
import { LoginPage } from '@/pages/LoginPage'
import { RegisterPage } from '@/pages/RegisterPage'
import { DashboardPage } from '@/pages/DashboardPage'
import { UploadPage } from '@/pages/UploadPage'
import { ChatWidget } from '@/components/chat/ChatWidget'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      staleTime: 30_000,
    },
  },
})

const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  { path: '/register', element: <RegisterPage /> },
  {
    path: '/dashboard',
    element: (
      <AuthGuard>
        <DashboardPage />
      </AuthGuard>
    ),
  },
  {
    path: '/upload',
    element: (
      <AuthGuard>
        <UploadPage />
      </AuthGuard>
    ),
  },
  { path: '/', element: <Navigate to="/dashboard" replace /> },
  { path: '*', element: <Navigate to="/dashboard" replace /> },
])

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <RouterProvider router={router} />
        <ChatWidget />
      </AuthProvider>
    </QueryClientProvider>
  )
}
```

- [ ] **Step 2: Check `useAuth` returns `isAuthenticated`**

Read `src/Pacevite.Web/src/context/AuthContext.tsx` to confirm `isAuthenticated` is on the auth context. If it uses a different field name (e.g. `user !== null`), update `ChatWidget.tsx` accordingly.

- [ ] **Step 3: Start the dev server and verify the widget appears**

```bash
cd src/Pacevite.Web && npm run dev
```

Navigate to `http://localhost:5173/dashboard` after logging in. The 💬 button should appear in the bottom-right. Clicking it should open the chat panel. Closing should collapse it.

- [ ] **Step 4: Run full test suite**

```bash
cd tests/Pacevite.Api.Tests && dotnet test
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Web/src/App.tsx
git commit -m "feat(chat): mount ChatWidget in App — floating chat panel complete"
```

---

## Self-Review Checklist (run before marking complete)

- [ ] All spec sections have a corresponding task: architecture ✅, tools ✅, SSE endpoint ✅, streaming handler ✅, integration test ✅, all four tool handlers ✅, all frontend components ✅
- [ ] `userId` is always extracted from JWT — never from request body (Task 9 ✅)
- [ ] SSRF allowlist is enforced in both scrape handlers (Tasks 6, 7 ✅)
- [ ] Tool input JSON uses `JsonNode` throughout — no string concatenation (Tasks 4, 5, 6, 7, 8 ✅)
- [ ] All tests follow AAA, use domain-meaningful data, and cover the happy path + boundary (Tasks 3–7 ✅)
- [ ] `AnthropicClient` is registered as `Singleton` — expensive to construct, safe to share (Task 10 ✅)
- [ ] Frontend token comes from `tokenStore.get()` — never `localStorage` (Task 12 ✅)
