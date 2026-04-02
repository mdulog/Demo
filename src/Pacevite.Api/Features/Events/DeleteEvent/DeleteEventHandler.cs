using Mediator;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Features.Events.DeleteEvent;

public sealed class DeleteEventHandler(AppDbContext db, ILogger<DeleteEventHandler> logger)
    : ICommandHandler<DeleteEventCommand>
{
    public async ValueTask<Unit> Handle(DeleteEventCommand command, CancellationToken cancellationToken)
    {
        // Scoped delete — the UserId check ensures users cannot delete each other's events (A01).
        var deleted = await db.Events
            .Where(e => e.Id == command.EventId && e.UserId == command.UserId)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted == 0)
        {
            logger.LogWarning("Delete attempted for EventId {EventId} by {UserId} — not found or not owned",
                command.EventId, command.UserId);
        }
        else
        {
            logger.LogInformation("Event {EventId} deleted by {UserId}", command.EventId, command.UserId);
        }

        return Unit.Value;
    }
}
