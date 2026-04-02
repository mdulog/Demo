using Mediator;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Parsing;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Features.Events.Upload;

public sealed class UploadEventHandler(
    AppDbContext db,
    IEnumerable<IEventParser> parsers,
    ILogger<UploadEventHandler> logger) : ICommandHandler<UploadEventCommand, IReadOnlyList<EventResponse>>
{
    public async ValueTask<IReadOnlyList<EventResponse>> Handle(
        UploadEventCommand command, CancellationToken cancellationToken)
    {
        var parser = parsers.FirstOrDefault(p => p.CanParse(command.ContentType))
            ?? throw new InvalidOperationException($"No parser available for content type '{command.ContentType}'.");

        var parsed = parser.Parse(command.FileStream);
        var created = new List<Event>(parsed.Count);

        // Idempotency key: same user + event_type + event_name + event_date = duplicate.
        // Duplicates are silently skipped rather than failing the entire upload.
        var existingKeys = db.Events
            .Where(e => e.UserId == command.UserId)
            .Select(e => new { e.EventType, e.EventName, e.EventDate })
            .ToHashSet();

        foreach (var p in parsed)
        {
            if (!Enum.TryParse<EventType>(p.EventType, ignoreCase: true, out var eventType))
            {
                logger.LogWarning("Skipping event with unknown EventType '{EventType}'", p.EventType);
                continue;
            }

            if (!Enum.TryParse<CompletionStatus>(p.Completion, ignoreCase: true, out var completion))
            {
                logger.LogWarning("Skipping event with unknown Completion '{Completion}'", p.Completion);
                continue;
            }

            var key = new { EventType = eventType, p.EventName, p.EventDate };
            if (existingKeys.Contains(key))
            {
                logger.LogInformation("Skipping duplicate event: {EventType} {EventName} {EventDate} for {UserId}",
                    eventType, p.EventName, p.EventDate, command.UserId);
                continue;
            }

            var ev = new Event
            {
                UserId = command.UserId,
                EventType = eventType,
                EventName = p.EventName,
                EventDate = p.EventDate,
                Completion = completion,
                ElapsedSecs = p.ElapsedSecs,
                OverallRank = p.OverallRank,
                AgeGroupRank = p.AgeGroupRank,
                FieldSize = p.FieldSize,
                AgeGroupFieldSize = p.AgeGroupFieldSize,
                Location = p.Location,
                Metadata = p.Metadata
            };

            foreach (var split in p.Splits)
            {
                ev.Splits.Add(new EventSplit
                {
                    EventId = ev.Id,
                    SplitType = split.SplitType,
                    SplitLabel = split.SplitLabel,
                    SplitSecs = split.SplitSecs,
                    CumulativeSecs = split.CumulativeSecs,
                    Metadata = split.Metadata
                });
            }

            db.Events.Add(ev);
            created.Add(ev);
            existingKeys.Add(key);
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Uploaded {Count} events for {UserId}", created.Count, command.UserId);
        return created.Select(EventMapper.ToResponse).ToList();
    }
}
