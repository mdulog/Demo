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
        ev.NeedsEnrichment,
        ev.CreatedAt,
        ev.Splits.Select(s => new EventSplitResponse(s.Id, s.SplitType, s.SplitLabel, s.SplitSecs, s.CumulativeSecs)).ToList());

    internal static EventDetailResponse ToDetailResponse(
        Event ev, IReadOnlyList<AverageSplitResponse> averageSplits) => new(
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
        ev.NeedsEnrichment,
        ev.CreatedAt,
        ev.Splits.Select(s => new EventSplitResponse(s.Id, s.SplitType, s.SplitLabel, s.SplitSecs, s.CumulativeSecs)).ToList(),
        averageSplits);

    internal static EventSummaryResponse ToSummaryResponse(Event ev) => new(
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
        ev.NeedsEnrichment,
        ev.CreatedAt);
}
