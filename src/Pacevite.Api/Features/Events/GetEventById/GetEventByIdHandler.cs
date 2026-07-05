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
