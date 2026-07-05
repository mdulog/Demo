using Mediator;
using Pacevite.Api.Contracts.Responses;

namespace Pacevite.Api.Features.Events.GetTimeline;

public sealed record GetTimelineQuery(
    string UserId,
    string? EventType = null) : IQuery<IReadOnlyList<TimelineEntryResponse>>;
