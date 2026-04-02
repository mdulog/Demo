using Mediator;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Features.Events.GetEvents;

public sealed class GetEventsHandler(AppDbContext db)
    : IQueryHandler<GetEventsQuery, IReadOnlyList<EventResponse>>
{
    public async ValueTask<IReadOnlyList<EventResponse>> Handle(
        GetEventsQuery query, CancellationToken cancellationToken)
    {
        var q = db.Events
            .Include(e => e.Splits)
            .Where(e => e.UserId == query.UserId)
            .AsQueryable();

        if (query.EventType is not null && Enum.TryParse<EventType>(query.EventType, ignoreCase: true, out var eventType))
            q = q.Where(e => e.EventType == eventType);

        if (query.From.HasValue)
            q = q.Where(e => e.EventDate >= query.From.Value);

        if (query.To.HasValue)
            q = q.Where(e => e.EventDate <= query.To.Value);

        var events = await q
            .OrderByDescending(e => e.EventDate)
            .ToListAsync(cancellationToken);

        return events.Select(EventMapper.ToResponse).ToList();
    }
}
