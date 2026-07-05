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
