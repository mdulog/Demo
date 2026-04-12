using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Chat.Tools;
using Pacevite.Api.Infrastructure.Persistence;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Chat;

[Category("Unit")]
public sealed class GetEventsToolHandlerTests
{
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

        var handler = new GetEventsToolHandler(db, NullLogger<GetEventsToolHandler>.Instance);
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
            new Event { UserId = "user-1", EventType = EventType.Marathon, EventName = "Berlin Marathon", EventDate = new DateOnly(2024, 9, 29), Completion = CompletionStatus.Finished, ElapsedSecs = 14400 },
            new Event { UserId = "user-1", EventType = EventType.Hyrox, EventName = "HYROX Berlin", EventDate = new DateOnly(2024, 11, 10), Completion = CompletionStatus.Finished, ElapsedSecs = 5400 }
        );
        await db.SaveChangesAsync();

        var handler = new GetEventsToolHandler(db, NullLogger<GetEventsToolHandler>.Instance);
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

        var handler = new GetEventsToolHandler(db, NullLogger<GetEventsToolHandler>.Instance);
        var result = await handler.ExecuteAsync(JsonNode.Parse("{}")!, "user-with-no-events", CancellationToken.None);

        await Assert.That(result).Contains("No events found");
    }

    [Test]
    public async Task ExecuteAsync_WithFromDateFilter_ExcludesOlderEvents()
    {
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        db.Events.AddRange(
            new Event { UserId = "user-1", EventType = EventType.Marathon, EventName = "Manchester Marathon", EventDate = new DateOnly(2024, 4, 14), Completion = CompletionStatus.Finished, ElapsedSecs = 15300 },
            new Event { UserId = "user-1", EventType = EventType.Marathon, EventName = "Chicago Marathon", EventDate = new DateOnly(2024, 10, 13), Completion = CompletionStatus.Finished, ElapsedSecs = 14100 }
        );
        await db.SaveChangesAsync();

        var handler = new GetEventsToolHandler(db, NullLogger<GetEventsToolHandler>.Instance);
        var result = await handler.ExecuteAsync(
            JsonNode.Parse("""{"from":"2024-06-01"}""")!, "user-1", CancellationToken.None);

        await Assert.That(result).Contains("Chicago Marathon");
        await Assert.That(result).DoesNotContain("Manchester Marathon");
    }

    [Test]
    public async Task ExecuteAsync_WithToDateFilter_ExcludesNewerEvents()
    {
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        db.Events.AddRange(
            new Event { UserId = "user-1", EventType = EventType.Marathon, EventName = "Manchester Marathon", EventDate = new DateOnly(2024, 4, 14), Completion = CompletionStatus.Finished, ElapsedSecs = 15300 },
            new Event { UserId = "user-1", EventType = EventType.Marathon, EventName = "Chicago Marathon", EventDate = new DateOnly(2024, 10, 13), Completion = CompletionStatus.Finished, ElapsedSecs = 14100 }
        );
        await db.SaveChangesAsync();

        var handler = new GetEventsToolHandler(db, NullLogger<GetEventsToolHandler>.Instance);
        var result = await handler.ExecuteAsync(
            JsonNode.Parse("""{"to":"2024-06-01"}""")!, "user-1", CancellationToken.None);

        await Assert.That(result).Contains("Manchester Marathon");
        await Assert.That(result).DoesNotContain("Chicago Marathon");
    }
}
