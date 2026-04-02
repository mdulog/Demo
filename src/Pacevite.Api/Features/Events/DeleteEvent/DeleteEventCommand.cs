using Mediator;

namespace Pacevite.Api.Features.Events.DeleteEvent;

public sealed record DeleteEventCommand(Guid EventId, string UserId) : ICommand;
