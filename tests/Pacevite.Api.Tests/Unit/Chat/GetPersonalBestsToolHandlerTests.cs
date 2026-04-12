using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Chat.Tools;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Tests.Unit.Chat;

[Category("Unit")]
public sealed class GetPersonalBestsToolHandlerTests
{
    [Test]
    public async Task ExecuteAsync_ReturnsOnlyFinishedEvents()
    {
        // Arrange
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        db.Events.AddRange(
            new Event { UserId = "user-1", EventType = EventType.Marathon, EventName = "Berlin Marathon", EventDate = new DateOnly(2024, 9, 29), Completion = CompletionStatus.Finished, ElapsedSecs = 14400 },
            new Event { UserId = "user-1", EventType = EventType.Marathon, EventName = "London Marathon DNF", EventDate = new DateOnly(2024, 4, 21), Completion = CompletionStatus.Dnf, ElapsedSecs = 7200 }
        );
        await db.SaveChangesAsync();

        var handler = new GetPersonalBestsToolHandler(db, NullLogger<GetPersonalBestsToolHandler>.Instance);

        // Act
        var result = await handler.ExecuteAsync(JsonNode.Parse("{}")!, "user-1", CancellationToken.None);

        // Assert
        await Assert.That(result).Contains("Berlin Marathon");
        await Assert.That(result).DoesNotContain("London Marathon DNF");
    }

    [Test]
    public async Task ExecuteAsync_ReturnsOnlyFastestPerEventType()
    {
        // Arrange
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        db.Events.AddRange(
            new Event { UserId = "user-1", EventType = EventType.Marathon, EventName = "Chicago Marathon", EventDate = new DateOnly(2024, 10, 13), Completion = CompletionStatus.Finished, ElapsedSecs = 13200 },
            new Event { UserId = "user-1", EventType = EventType.Marathon, EventName = "Manchester Marathon", EventDate = new DateOnly(2024, 4, 14), Completion = CompletionStatus.Finished, ElapsedSecs = 15300 }
        );
        await db.SaveChangesAsync();

        var handler = new GetPersonalBestsToolHandler(db, NullLogger<GetPersonalBestsToolHandler>.Instance);

        // Act
        var result = await handler.ExecuteAsync(JsonNode.Parse("{}")!, "user-1", CancellationToken.None);

        // Assert — only one entry because both are the same EventType, and Chicago is faster
        await Assert.That(result).Contains("Chicago Marathon");
        await Assert.That(result).DoesNotContain("Manchester Marathon");
    }

    [Test]
    public async Task ExecuteAsync_ReturnsOnlyEventsForUserId()
    {
        // Arrange
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        db.Events.AddRange(
            new Event { UserId = "user-1", EventType = EventType.Marathon, EventName = "Berlin Marathon", EventDate = new DateOnly(2024, 9, 29), Completion = CompletionStatus.Finished, ElapsedSecs = 14400 },
            new Event { UserId = "user-2", EventType = EventType.Marathon, EventName = "Tokyo Marathon", EventDate = new DateOnly(2024, 3, 3), Completion = CompletionStatus.Finished, ElapsedSecs = 12600 }
        );
        await db.SaveChangesAsync();

        var handler = new GetPersonalBestsToolHandler(db, NullLogger<GetPersonalBestsToolHandler>.Instance);

        // Act
        var result = await handler.ExecuteAsync(JsonNode.Parse("{}")!, "user-1", CancellationToken.None);

        // Assert
        await Assert.That(result).Contains("Berlin Marathon");
        await Assert.That(result).DoesNotContain("Tokyo Marathon");
    }

    [Test]
    public async Task ExecuteAsync_NoFinishedEvents_ReturnsNoPersonalBestsMessage()
    {
        // Arrange
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var handler = new GetPersonalBestsToolHandler(db, NullLogger<GetPersonalBestsToolHandler>.Instance);

        // Act
        var result = await handler.ExecuteAsync(JsonNode.Parse("{}")!, "user-with-no-events", CancellationToken.None);

        // Assert
        await Assert.That(result).IsEqualTo("No personal bests found. The user has no finished events.");
    }
}
