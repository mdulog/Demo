using Mediator;
using Pacevite.Api.Contracts.Responses;

namespace Pacevite.Api.Features.Events.GetEventById;

public sealed record GetEventByIdQuery(Guid EventId, string UserId) : IQuery<EventDetailResponse?>;
