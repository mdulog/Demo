using Mediator;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Features.Events.GetPersonalBests;

public sealed class GetPersonalBestsHandler(AppDbContext db)
    : IQueryHandler<GetPersonalBestsQuery, IReadOnlyList<PersonalBestResponse>>
{
    public async ValueTask<IReadOnlyList<PersonalBestResponse>> Handle(
        GetPersonalBestsQuery query, CancellationToken cancellationToken)
    {
        // Fastest (minimum elapsed_secs) per event_type where completion = FINISHED.
        // GROUP BY in EF Core requires projecting to an anonymous type first, then
        // selecting the min-time event from each group.
        var personalBests = await db.Events
            .Where(e => e.UserId == query.UserId && e.Completion == CompletionStatus.Finished)
            .GroupBy(e => e.EventType)
            .Select(g => g.OrderBy(e => e.ElapsedSecs).First())
            .ToListAsync(cancellationToken);

        return personalBests
            .Select(e => new PersonalBestResponse(
                e.EventType.ToString().ToUpperInvariant(),
                e.Id,
                e.EventName,
                e.EventDate,
                e.ElapsedSecs))
            .ToList();
    }
}
