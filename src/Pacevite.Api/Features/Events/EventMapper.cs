using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Domain.Entities;

namespace Pacevite.Api.Features.Events;

internal static class EventMapper
{
    internal static EventResponse ToResponse(Event ev) => new(
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
        ev.CreatedAt,
        ev.Splits.Select(s => new EventSplitResponse(s.Id, s.SplitType, s.SplitLabel, s.SplitSecs, s.CumulativeSecs)).ToList());
}
