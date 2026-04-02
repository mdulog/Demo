using Mediator;
using Pacevite.Api.Contracts.Responses;

namespace Pacevite.Api.Features.Events.GetEvents;

public sealed record GetEventsQuery(
    string UserId,
    string? EventType = null,
    DateOnly? From = null,
    DateOnly? To = null) : IQuery<IReadOnlyList<EventResponse>>;
